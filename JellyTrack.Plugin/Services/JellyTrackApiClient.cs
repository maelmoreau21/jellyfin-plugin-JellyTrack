using Microsoft.Extensions.Hosting;
using MediaBrowser.Controller;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JellyTrack.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public sealed record TestConnectionResult(bool Success, HttpStatusCode? StatusCode, string Message, string Endpoint);
public sealed record PluginRuntimeMetricsSnapshot(int QueueDepth, int RetryAttempts, int? LastHttpCode, int CoalescedProgressEvents);

public class JellyTrackApiClient : BackgroundService
{
    public const int MaxResponseBodyChars = 4096;
    private const string DefaultPluginEventsPath = "/api/plugin/events";

    private readonly HttpClient _httpClient;
    private readonly ILogger<JellyTrackApiClient> _logger;
    private readonly bool _ownsClient;
    private readonly LinkedList<PluginEvent> _deliveryQueue = new();
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _queueSignal = new(0, 1);
    private readonly Func<PluginConfiguration?> _configuration;
    private readonly string? _serverId;
    private readonly string? _serverName;
    private PluginEvent? _inFlight;
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
    private int _retryAttempts;
    private int _coalescedProgressEventsCount;
    private int? _lastHttpCode;
    private readonly object _telemetryLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public JellyTrackApiClient(IHttpClientFactory httpClientFactory, ILogger<JellyTrackApiClient> logger, IServerApplicationHost applicationHost)
        : this(CreateSafeHttpClient(), logger, ownsClient: true, serverId: applicationHost.SystemId, serverName: applicationHost.FriendlyName)
    {
    }

    internal JellyTrackApiClient(HttpClient httpClient, ILogger<JellyTrackApiClient> logger, bool ownsClient = false, Func<PluginConfiguration?>? configuration = null, string? serverId = null, string? serverName = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownsClient = ownsClient;
        _configuration = configuration ?? (() => Plugin.Instance?.Configuration);
        _serverId = serverId;
        _serverName = serverName;
    }

    private static HttpClient CreateSafeHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    // The result means accepted into the bounded queue, not delivered over HTTP.
    public Task<bool> SendEventAsync(PluginEvent eventPayload, CancellationToken cancellationToken = default)
    {
        var config = _configuration();
        if (cancellationToken.IsCancellationRequested || config is null || !config.Enabled
            || string.IsNullOrWhiteSpace(config.ApiKey) || !TryResolveEndpoint(config.JellyTrackUrl, out _))
        {
            return Task.FromResult(false);
        }

        eventPayload.ServerId ??= _serverId;
        eventPayload.ServerName ??= _serverName;
        lock (_queueLock)
        {
            // Only replace adjacent progress observations. Never move progress across
            // pause/seek/stop/start boundaries, including a reused Jellyfin session ID.
            if (eventPayload is PlaybackProgressEvent progress
                && _deliveryQueue.Last?.Value is PlaybackProgressEvent previous
                && progress.SessionId == previous.SessionId && progress.Media.JellyfinMediaId == previous.Media.JellyfinMediaId)
            {
                _deliveryQueue.Last.Value = eventPayload;
                Interlocked.Increment(ref _coalescedProgressEventsCount);
            }
            else
            {
                while (_deliveryQueue.Count + (_inFlight is null ? 0 : 1) >= config.RetryQueueSize)
                {
                    var disposable = _deliveryQueue.First;
                    while (disposable is not null && disposable.Value is not PlaybackProgressEvent)
                    {
                        disposable = disposable.Next;
                    }
                    _deliveryQueue.Remove(disposable ?? _deliveryQueue.First!);
                    _logger.LogWarning("JellyTrack delivery queue full; oldest pending event dropped");
                }
                _deliveryQueue.AddLast(eventPayload);
            }
        }
        if (_queueSignal.CurrentCount == 0)
        {
            try { _queueSignal.Release(); } catch (SemaphoreFullException) { }
        }
        return Task.FromResult(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _queueSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                var deliveredInBatch = 0;
                while (!stoppingToken.IsCancellationRequested)
                {
                    var config = _configuration();
                    if (config is null || !config.Enabled || string.IsNullOrWhiteSpace(config.ApiKey)
                        || !TryResolveEndpoint(config.JellyTrackUrl, out var endpoint))
                    {
                        await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }
                    lock (_queueLock)
                    {
                        if (_inFlight is null && _deliveryQueue.First is { } first)
                        {
                            _inFlight = first.Value;
                            _deliveryQueue.RemoveFirst();
                        }
                    }
                    if (_inFlight is null) break;
                    var sent = await SendSingleEventAsync(endpoint, config.ApiKey, _inFlight, stoppingToken).ConfigureAwait(false);
                    if (sent)
                    {
                        lock (_queueLock) { _inFlight = null; }
                        if (++deliveredInBatch >= config.RetryFlushBatchSize)
                        {
                            deliveredInBatch = 0;
                            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // Keep the failed event ahead of later lifecycle events.
                        Interlocked.Increment(ref _retryAttempts);
                        await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyTrack delivery worker failed; retrying");
                try { await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                if (_queueSignal.CurrentCount == 0) _queueSignal.Release();
            }
        }
    }

    public async Task<TestConnectionResult> TestConnectionAsync(
        string configuredUrl,
        string apiKey,
        PluginEvent eventPayload,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = configuredUrl?.Trim() ?? string.Empty;
        var normalizedApiKey = apiKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedApiKey))
        {
            return new TestConnectionResult(false, null, "API key is required.", string.Empty);
        }

        if (!TryResolveEndpoint(normalizedUrl, out var endpoint))
        {
            return new TestConnectionResult(false, null, "Invalid or disallowed JellyTrack URL.", normalizedUrl);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            cancellationToken = timeout.Token;
            using var request = BuildAuthenticatedRequest(endpoint, normalizedApiKey, eventPayload);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var responseBody = (await ReadResponseBodyAsync(response, cancellationToken).ConfigureAwait(false))
                .Replace(normalizedApiKey, "[redacted]", StringComparison.Ordinal);

            if (response.IsSuccessStatusCode)
            {
                var message = string.IsNullOrWhiteSpace(responseBody)
                    ? "Connection successful."
                    : responseBody;
                return new TestConnectionResult(true, response.StatusCode, message, endpoint.ToString());
            }

            _logger.LogWarning(
                "JellyTrack test connection failed with {StatusCode}. Response: {Response}",
                (int)response.StatusCode,
                responseBody.Replace(normalizedApiKey, "[redacted]", StringComparison.Ordinal));

            var failureMessage = string.IsNullOrWhiteSpace(responseBody)
                ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                : responseBody;

            return new TestConnectionResult(false, response.StatusCode, failureMessage, endpoint.ToString());
        }
        catch (TaskCanceledException)
        {
            return new TestConnectionResult(false, null, "Request timed out while contacting JellyTrack.", endpoint.ToString());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "JellyTrack test connection request failed");
            return new TestConnectionResult(false, null, "Failed to connect to JellyTrack server. Verify URL and network reachability.", endpoint.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error testing connection to JellyTrack");
            return new TestConnectionResult(false, null, "Unexpected connection error. Check server logs for details.", endpoint.ToString());
        }
    }

    public PluginRuntimeMetricsSnapshot GetRuntimeMetricsSnapshot()
    {
        int? lastHttpCode;
        lock (_telemetryLock)
        {
            lastHttpCode = _lastHttpCode;
        }

        return new PluginRuntimeMetricsSnapshot(
            QueueDepth: GetQueueDepth(),
            RetryAttempts: Volatile.Read(ref _retryAttempts),
            LastHttpCode: lastHttpCode,
            CoalescedProgressEvents: Volatile.Read(ref _coalescedProgressEventsCount));
    }

    private async Task<bool> SendSingleEventAsync(Uri endpoint, string apiKey, PluginEvent eventPayload, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            cancellationToken = timeout.Token;
            using var request = BuildAuthenticatedRequest(endpoint, apiKey, eventPayload);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            lock (_telemetryLock)
            {
                _lastHttpCode = (int)response.StatusCode;
            }

            if (response.IsSuccessStatusCode)
            {
                if (string.Equals(eventPayload.Event, "PlaybackProgress", StringComparison.Ordinal))
                {
                    _logger.LogDebug("JellyTrack event {Event} sent successfully", eventPayload.Event);
                }
                else
                {
                    _logger.LogInformation("JellyTrack event {Event} sent successfully", eventPayload.Event);
                }
                return true;
            }

            var responseBody = await ReadResponseBodyAsync(response, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "JellyTrack API returned {StatusCode} for event {Event}. Response: {Response}",
                response.StatusCode,
                eventPayload.Event,
                responseBody.Replace(apiKey, "[redacted]", StringComparison.Ordinal));
            // Invalid payloads cannot heal on retry; authentication may heal after key rotation.
            var code = (int)response.StatusCode;
            if (code is >= 400 and < 500 && code is not (401 or 403 or 408 or 429))
            {
                _logger.LogWarning("Discarding rejected JellyTrack event {Event} (HTTP {Code})", eventPayload.Event, code);
                return true;
            }
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("JellyTrack API request timed out for event {Event}", eventPayload.Event);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to send event {Event} to JellyTrack", eventPayload.Event);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending event {Event} to JellyTrack", eventPayload.Event);
            return false;
        }
    }

    private int GetQueueDepth()
    {
        lock (_queueLock) { return _deliveryQueue.Count + (_inFlight is null ? 0 : 1); }
    }

    private HttpRequestMessage BuildAuthenticatedRequest(Uri endpoint, string apiKey, PluginEvent eventPayload)
    {
        eventPayload.ServerId ??= _serverId;
        eventPayload.ServerName ??= _serverName;
        var normalizedApiKey = apiKey.Trim();
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedApiKey);
        request.Headers.TryAddWithoutValidation("X-Api-Key", normalizedApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(eventPayload, eventPayload.GetType(), JsonOptions),
            Encoding.UTF8,
            "application/json");

        return request;
    }

    public static bool IsBlockedHostOrIp(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var normalizedHost = host.Trim().Trim('[', ']').ToLowerInvariant();

        // Block known cloud metadata domain names and subdomains
        if (normalizedHost is "metadata.google.internal" or "metadata.azure.com" or "metadata.internal" or "instance-data" or "100.100.100.200"
            || normalizedHost.EndsWith(".metadata.google.internal", StringComparison.Ordinal)
            || normalizedHost.EndsWith(".metadata.azure.com", StringComparison.Ordinal)
            || normalizedHost.EndsWith(".metadata.internal", StringComparison.Ordinal)
            || normalizedHost.EndsWith(".instance-data", StringComparison.Ordinal))
        {
            return true;
        }

        // Block embedded cloud metadata IP domains (e.g., 169.254.169.254.nip.io, 169-254-169-254.sslip.io)
        if (normalizedHost.Contains("169.254.", StringComparison.Ordinal)
            || normalizedHost.Contains("169-254-", StringComparison.Ordinal)
            || normalizedHost.Contains("100.100.100.200", StringComparison.Ordinal)
            || normalizedHost.Contains("100-100-100-200", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(normalizedHost, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // Block 169.254.0.0/16 (Link-Local & Cloud Metadata IMDSv1/v2)
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }

                // Block Alibaba Cloud Metadata IP (100.100.100.200)
                if (bytes[0] == 100 && bytes[1] == 100 && bytes[2] == 100 && bytes[3] == 200)
                {
                    return true;
                }

                // Block 224.0.0.0/4 (Multicast) and 255.255.255.255 (Broadcast)
                if (bytes[0] >= 224)
                {
                    return true;
                }

                // Block 0.0.0.0/8
                if (bytes[0] == 0)
                {
                    return true;
                }
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // Block IPv6 Link-Local (fe80::/10)
                if (ip.IsIPv6LinkLocal)
                {
                    return true;
                }

                // Block IPv6 Multicast (ff00::/8)
                if (ip.IsIPv6Multicast)
                {
                    return true;
                }

                // Block AWS EC2 IMDSv6 (fd00:ec2::254)
                if (string.Equals(ip.ToString(), "fd00:ec2::254", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Block IPv6 Any (::)
                if (ip.Equals(IPAddress.IPv6Any))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryResolveEndpoint(string configuredUrl, out Uri endpoint)
    {
        endpoint = default!;

        if (!Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // Avoid accepting URLs with embedded credentials (userinfo),
        // which are easy to leak in logs and browser history.
        if (!string.IsNullOrWhiteSpace(parsed.UserInfo))
        {
            return false;
        }

        // Block SSRF targets (link-local, cloud metadata)
        if (IsBlockedHostOrIp(parsed.Host))
        {
            return false;
        }

        var builder = new UriBuilder(parsed);
        builder.Path = NormalizeEndpointPath(builder.Path);
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;

        endpoint = builder.Uri;
        return true;
    }

    private static string NormalizeEndpointPath(string? originalPath)
    {
        var trimmed = (originalPath ?? string.Empty).Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            // Backward compatibility: a bare host still targets the plugin endpoint.
            return DefaultPluginEventsPath;
        }

        // If a path is explicitly provided by the user, keep it as-is.
        return "/" + trimmed;
    }

    internal static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[MaxResponseBodyChars];
            var charsRead = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (charsRead <= 0) return string.Empty;

            var sb = new StringBuilder(charsRead);
            for (int i = 0; i < charsRead; i++)
            {
                var c = buffer[i];
                if (!char.IsControl(c) || c == '\r' || c == '\n' || c == '\t')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }


}
