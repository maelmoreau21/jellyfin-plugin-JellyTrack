using System.Reflection;
using JellyTrack.Plugin.Models;
using System.Globalization;
using System.Linq;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Services;

public class HeartbeatService : IScheduledTask, IHostedService, IDisposable
{
    private readonly JellyTrackApiClient _apiClient;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundLoop;

    public HeartbeatService(
        JellyTrackApiClient apiClient,
        IUserManager userManager,
        IServerApplicationHost appHost,
        ILogger<HeartbeatService> logger)
    {
        _apiClient = apiClient;
        _userManager = userManager;
        _appHost = appHost;
        _logger = logger;
    }

    public string Name => "JellyTrack Heartbeat";

    public string Key => "JellyTrackHeartbeat";

    public string Description => "Envoie un heartbeat périodique à JellyTrack avec la liste des utilisateurs.";

    public string Category => "JellyTrack";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await SendHeartbeatInternalAsync("scheduler", cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_backgroundLoop is not null)
        {
            return Task.CompletedTask;
        }

        MigrateLegacyHeartbeatIntervalIfNeeded();

        _backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundLoop = RunBackgroundLoopAsync(_backgroundCts.Token);
        var loadedVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        _logger.LogInformation("JellyTrack heartbeat background service started (plugin assembly v{Version})", loadedVersion);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_backgroundCts is null || _backgroundLoop is null)
        {
            return;
        }

        try
        {
            _backgroundCts.Cancel();
            await _backgroundLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        finally
        {
            _backgroundCts.Dispose();
            _backgroundCts = null;
            _backgroundLoop = null;
            _logger.LogInformation("JellyTrack heartbeat background service stopped");
        }
    }

    private async Task RunBackgroundLoopAsync(CancellationToken cancellationToken)
    {
        // First heartbeat is sent immediately on startup to mark plugin online quickly.
        await SendHeartbeatInternalAsync("background-startup", cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var intervalSeconds = GetHeartbeatIntervalSeconds();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await SendHeartbeatInternalAsync("background-interval", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendHeartbeatInternalAsync(string source, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return;
        }

        if (!IsConfigurationReadyForSend(config, out var skipReason))
        {
            _logger.LogDebug("Skipping JellyTrack heartbeat ({Source}): {Reason}", source, skipReason);
            return;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LogContainerLocalhostHint(config.JellyTrackUrl);
            var pluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            var users = UserSnapshotResolver.ResolveHeartbeatUsers(_userManager, _logger);
            var runtimeMetrics = _apiClient.GetRuntimeMetricsSnapshot();

            var payload = new HeartbeatEvent
            {
                PluginVersion = pluginVersion,
                ServerName = _appHost.FriendlyName,
                JellyfinVersion = _appHost.ApplicationVersionString,
                Users = users,
                ServerLanguage = !string.IsNullOrWhiteSpace(config.PreferredLanguage)
                    ? config.PreferredLanguage
                    : CultureInfo.CurrentUICulture.Name,
                PluginMetrics = new HeartbeatPluginMetrics
                {
                    QueueDepth = runtimeMetrics.QueueDepth,
                    Retries = runtimeMetrics.RetryAttempts,
                    LastHttpCode = runtimeMetrics.LastHttpCode,
                    CoalescedProgressEvents = runtimeMetrics.CoalescedProgressEvents,
                },
            };

            var success = await _apiClient.SendEventAsync(payload, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation(
                    "JellyTrack heartbeat sent ({Source}) with {UserCount} users",
                    source,
                    users.Count);
            }
            else
            {
                _logger.LogWarning(
                    "JellyTrack heartbeat failed ({Source}). Verify URL/API key/network reachability from Jellyfin host.",
                    source);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool IsConfigurationReadyForSend(PluginConfiguration config, out string reason)
    {
        if (string.IsNullOrWhiteSpace(config.JellyTrackUrl))
        {
            reason = "JellyTrack URL is empty";
            return false;
        }

        var apiKey = config.ApiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            reason = "API key is empty";
            return false;
        }

        if (string.Equals(apiKey, "jt_xxxxxxxxxxxx", StringComparison.OrdinalIgnoreCase))
        {
            reason = "API key still uses placeholder value";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private int GetHeartbeatIntervalSeconds()
    {
        var configured = Plugin.Instance?.Configuration.HeartbeatIntervalSeconds
            ?? PluginConfiguration.DefaultHeartbeatIntervalSeconds;

        var normalized = PluginConfiguration.NormalizeHeartbeatIntervalSeconds(configured);
        if (configured != normalized)
        {
            _logger.LogWarning(
                "Configured heartbeat interval {ConfiguredSeconds}s is legacy/invalid. Forcing default interval of {DefaultSeconds}s.",
                configured,
                PluginConfiguration.DefaultHeartbeatIntervalSeconds);
        }

        return normalized;
    }

    private void MigrateLegacyHeartbeatIntervalIfNeeded()
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration;
        if (plugin is null || config is null)
        {
            return;
        }

        var configured = config.HeartbeatIntervalSeconds;
        var normalized = PluginConfiguration.NormalizeHeartbeatIntervalSeconds(configured);
        if (configured == normalized)
        {
            return;
        }

        config.HeartbeatIntervalSeconds = normalized;
        _logger.LogWarning(
            "Migrating legacy heartbeat interval from {ConfiguredSeconds}s to {DefaultSeconds}s.",
            configured,
            normalized);

        TryPersistConfigurationMigration(plugin, config);
    }

    private void TryPersistConfigurationMigration(Plugin plugin, PluginConfiguration config)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            var updateMethod = plugin
                .GetType()
                .GetMethods(Flags)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "UpdateConfiguration", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 &&
                           parameters[0].ParameterType.IsAssignableFrom(typeof(PluginConfiguration));
                });

            if (updateMethod is not null)
            {
                updateMethod.Invoke(plugin, new object[] { config });
                _logger.LogInformation(
                    "Persisted migrated heartbeat interval: {DefaultSeconds}s.",
                    config.HeartbeatIntervalSeconds);
                return;
            }

            var saveMethod = plugin
                .GetType()
                .GetMethods(Flags)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "SaveConfiguration", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 0);

            if (saveMethod is not null)
            {
                saveMethod.Invoke(plugin, null);
                _logger.LogInformation(
                    "Persisted migrated heartbeat interval: {DefaultSeconds}s.",
                    config.HeartbeatIntervalSeconds);
                return;
            }

            _logger.LogWarning(
                "Heartbeat interval migration applied in memory ({DefaultSeconds}s), but no persistence method was found.",
                config.HeartbeatIntervalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Heartbeat interval migration applied in memory but persistence failed.");
        }
    }

    private void LogContainerLocalhostHint(string? configuredUrl)
    {
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var host = uri.Host?.Trim().ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1")
        {
            _logger.LogWarning(
                "JellyTrack URL uses localhost ({Url}). If Jellyfin runs in Docker, localhost points to the Jellyfin container itself. Use host IP, host.docker.internal, or a Docker service name.",
                configuredUrl);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var intervalSeconds = GetHeartbeatIntervalSeconds();

        return new[]
        {
            new TaskTriggerInfo
            {
                // Use enum values from MediaBrowser.Model.Tasks.TaskTriggerInfoType (IntervalTrigger, StartupTrigger)
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromSeconds(intervalSeconds).Ticks
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            }
        };
    }

    public void Dispose()
    {
        _backgroundCts?.Cancel();
        _backgroundCts?.Dispose();
        _sendLock.Dispose();
    }
}
