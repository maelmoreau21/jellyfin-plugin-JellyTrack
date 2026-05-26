using MediaBrowser.Model.Plugins;

namespace JellyTrack.Plugin;

public class PluginConfiguration : BasePluginConfiguration
{
    public const int DefaultHeartbeatIntervalSeconds = 600;
    public const int MinimumHeartbeatIntervalSeconds = 300;
    public const int DefaultProgressIntervalSeconds = 5;
    public const int DefaultPausedProgressIntervalSeconds = 30;
    public const int DefaultSeekThresholdSeconds = 20;
    public const int DefaultRetryQueueSize = 500;
    public const int DefaultRetryFlushBatchSize = 50;

    private int _heartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds;
    private int _progressIntervalSeconds = DefaultProgressIntervalSeconds;
    private int _pausedProgressIntervalSeconds = DefaultPausedProgressIntervalSeconds;
    private int _seekThresholdSeconds = DefaultSeekThresholdSeconds;
    private int _retryQueueSize = DefaultRetryQueueSize;
    private int _retryFlushBatchSize = DefaultRetryFlushBatchSize;

    public string JellyTrackUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int HeartbeatIntervalSeconds
    {
        get => _heartbeatIntervalSeconds;
        set => _heartbeatIntervalSeconds = NormalizeHeartbeatIntervalSeconds(value);
    }

    public int ProgressIntervalSeconds
    {
        get => _progressIntervalSeconds;
        set => _progressIntervalSeconds = NormalizePositive(value, DefaultProgressIntervalSeconds, 1, 3600);
    }

    public int PausedProgressIntervalSeconds
    {
        get => _pausedProgressIntervalSeconds;
        set => _pausedProgressIntervalSeconds = NormalizePositive(value, DefaultPausedProgressIntervalSeconds, 5, 3600);
    }

    public int SeekThresholdSeconds
    {
        get => _seekThresholdSeconds;
        set => _seekThresholdSeconds = NormalizePositive(value, DefaultSeekThresholdSeconds, 5, 300);
    }

    public int RetryQueueSize
    {
        get => _retryQueueSize;
        set => _retryQueueSize = NormalizePositive(value, DefaultRetryQueueSize, 10, 5000);
    }

    public int RetryFlushBatchSize
    {
        get => _retryFlushBatchSize;
        set => _retryFlushBatchSize = NormalizePositive(value, DefaultRetryFlushBatchSize, 1, 500);
    }

    public bool TrackPauseResume { get; set; } = true;

    public bool TrackSeek { get; set; } = true;

    public bool TrackAudioSubtitleChanges { get; set; } = true;

    public bool TrackSessionEnded { get; set; } = true;

    // Keep disabled by default so a fresh install does not emit network traffic
    // before the admin validates URL and API key.
    public bool Enabled { get; set; } = false;

    // Optional: preferred language for the plugin. Leave empty to use Jellyfin's current UI language.
    public string PreferredLanguage { get; set; } = string.Empty;

    public static int NormalizeHeartbeatIntervalSeconds(int configuredValue)
    {
        if (configuredValue < MinimumHeartbeatIntervalSeconds)
        {
            return DefaultHeartbeatIntervalSeconds;
        }

        return configuredValue;
    }

    private static int NormalizePositive(int configuredValue, int fallback, int min, int max)
    {
        if (configuredValue < min)
        {
            return fallback;
        }

        return configuredValue > max ? max : configuredValue;
    }
}
