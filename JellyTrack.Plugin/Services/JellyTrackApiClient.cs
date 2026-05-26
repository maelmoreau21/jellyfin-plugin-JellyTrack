using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JellyTrack.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public sealed record TestConnectionResult(bool Success, HttpStatusCode? StatusCode, string Message, string Endpoint);
public sealed record PluginRuntimeMetricsSnapshot(int QueueDepth, int RetryAttempts, int? LastHttpCode, int CoalescedProgressEvents);

public class JellyTrackApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JellyTrackApiClient> _logger;
    private readonly ConcurrentQueue<QueuedPluginEvent> _retryQueue = new();
    private readonly ConcurrentDictionary<string, PluginEvent> _coalescedProgressEvents = new();
    private int _retryAttempts;
    private int _coalescedProgressEventsCount;
    private int? _lastHttpCode;
    private readonly object _telemetryLock = new();
    private const string DefaultPluginEventsPath = "/api/plugin/events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public JellyTrackApiClient(IHttpClientFactory httpClientFactory, ILogger<JellyTrackApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(JellyTrackApiClient));
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _logger = logger;
    }

    public async Task<bool> SendEventAsync(PluginEvent eventPayload, CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return false;
        }

        var configuredUrl = config.JellyTrackUrl?.Trim() ?? string.Empty;
        var apiKey = config.ApiKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(configuredUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("JellyTrack URL or API key is not configured");
            return false;
        }

        if (!TryResolveEndpoint(configuredUrl, out var endpoint))
        {
            _logger.LogWarning("Invalid JellyTrack URL configured: {Url}", configuredUrl);
            return false;
        }

        // Attempt to flush queued events first
        await FlushRetryQueueAsync(endpoint, apiKey, cancellationToken).ConfigureAwait(false);

        return await SendSingleEventAsync(endpoint, apiKey, eventPayload, cancellationToken).ConfigureAwait(false);
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
            return new TestConnectionResult(false, null, "Invalid JellyTrack URL.", normalizedUrl);
        }

        try
        {
            using var request = BuildAuthenticatedRequest(endpoint, normalizedApiKey, eventPayload);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await ReadResponseBodyAsync(response).ConfigureAwait(false);

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
                responseBody);

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
            return new TestConnectionResult(false, null, ex.Message, endpoint.ToString());
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
            QueueDepth: _retryQueue.Count,
            RetryAttempts: Volatile.Read(ref _retryAttempts),
            LastHttpCode: lastHttpCode,
            CoalescedProgressEvents: Volatile.Read(ref _coalescedProgressEventsCount));
    }

    private async Task<bool> SendSingleEventAsync(Uri endpoint, string apiKey, PluginEvent eventPayload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildAuthenticatedRequest(endpoint, apiKey, eventPayload);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

            var responseBody = await ReadResponseBodyAsync(response).ConfigureAwait(false);
            _logger.LogWarning(
                "JellyTrack API returned {StatusCode} for event {Event}. Response: {Response}",
                response.StatusCode,
                eventPayload.Event,
                responseBody);
            EnqueueForRetry(eventPayload);
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("JellyTrack API request timed out for event {Event}", eventPayload.Event);
            EnqueueForRetry(eventPayload);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to send event {Event} to JellyTrack", eventPayload.Event);
            EnqueueForRetry(eventPayload);
            return false;
        }
    }

    private void EnqueueForRetry(PluginEvent eventPayload)
    {
        var maxQueueSize = Plugin.Instance?.Configuration.RetryQueueSize ?? PluginConfiguration.DefaultRetryQueueSize;
        maxQueueSize = Math.Max(10, maxQueueSize);

        var coalesceKey = GetCoalesceKey(eventPayload);
        if (coalesceKey is not null)
        {
            if (_coalescedProgressEvents.TryAdd(coalesceKey, eventPayload))
            {
                _retryQueue.Enqueue(new QueuedPluginEvent(coalesceKey, eventPayload));
            }
            else
            {
                _coalescedProgressEvents[coalesceKey] = eventPayload;
                Interlocked.Increment(ref _coalescedProgressEventsCount);
                return;
            }
        }
        else
        {
            _retryQueue.Enqueue(new QueuedPluginEvent(null, eventPayload));
        }

        while (_retryQueue.Count > maxQueueSize && _retryQueue.TryDequeue(out var dropped))
        {
            if (dropped.CoalesceKey is not null)
            {
                _coalescedProgressEvents.TryRemove(dropped.CoalesceKey, out _);
            }
        }
    }

    private async Task FlushRetryQueueAsync(Uri endpoint, string apiKey, CancellationToken cancellationToken)
    {
        var flushBatchSize = Plugin.Instance?.Configuration.RetryFlushBatchSize ?? PluginConfiguration.DefaultRetryFlushBatchSize;
        var count = Math.Min(_retryQueue.Count, Math.Max(1, flushBatchSize));
        for (int i = 0; i < count; i++)
        {
            if (!_retryQueue.TryDequeue(out var queued))
            {
                break;
            }

            var eventToSend = queued.EventPayload;
            if (queued.CoalesceKey is not null
                && !_coalescedProgressEvents.TryRemove(queued.CoalesceKey, out eventToSend))
            {
                continue;
            }

            try
            {
                Interlocked.Increment(ref _retryAttempts);
                using var request = BuildAuthenticatedRequest(endpoint, apiKey, eventToSend);

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                lock (_telemetryLock)
                {
                    _lastHttpCode = (int)response.StatusCode;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Retry failed for queued event {Event}, re-queuing", eventToSend.Event);
                    EnqueueForRetry(eventToSend);
                    break; // stop flushing if server is still down
                }
            }
            catch (Exception)
            {
                EnqueueForRetry(eventToSend);
                break;
            }
        }
    }

    private static string? GetCoalesceKey(PluginEvent eventPayload)
    {
        if (eventPayload is PlaybackProgressEvent progress
            && !string.IsNullOrWhiteSpace(progress.SessionId))
        {
            return $"PlaybackProgress:{progress.SessionId}";
        }

        return null;
    }

    private HttpRequestMessage BuildAuthenticatedRequest(Uri endpoint, string apiKey, PluginEvent eventPayload)
    {
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

    private static bool TryResolveEndpoint(string configuredUrl, out Uri endpoint)
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

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record QueuedPluginEvent(string? CoalesceKey, PluginEvent EventPayload);
}
