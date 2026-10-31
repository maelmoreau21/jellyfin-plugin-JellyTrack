using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Notifiers;

public class PlaybackStopNotifier : IEventConsumer<PlaybackStopEventArgs>
{
    private readonly JellyTrackApiClient _apiClient;
    private readonly PlaybackSessionTelemetryState _telemetryState;
    private readonly ILogger<PlaybackStopNotifier> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _recentStops = new();


    public PlaybackStopNotifier(
        JellyTrackApiClient apiClient,
        PlaybackSessionTelemetryState telemetryState,
        ILogger<PlaybackStopNotifier> logger)
    {
        _apiClient = apiClient;
        _telemetryState = telemetryState;
        _logger = logger;
    }

    public async Task OnEvent(PlaybackStopEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return;
        }

        if (e.Item is null || e.Session is null)
        {
            _logger.LogDebug("PlaybackStop ignored: missing item or session");
            return;
        }

        var (jellyfinUserId, username) = UserSnapshotResolver.ResolveUserFromSession(e.Session, e.Users);
        if (string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            _logger.LogWarning("PlaybackStop ignored: could not resolve user for session {SessionId}", e.Session.Id);
            return;
        }

        var item = e.Item;

        _logger.LogInformation("PlaybackStop captured: user={UserId}, item={ItemId}, session={SessionId}", jellyfinUserId, item.Id, e.Session.Id);
        var now = DateTime.UtcNow;
        if (_recentStops.TryGetValue(e.Session.Id, out var recentStop) && (now - recentStop).TotalSeconds < 2)
        {
            return;
        }
        _recentStops[e.Session.Id] = now;
        if (_recentStops.Count > 100)
        {
            foreach (var kvp in _recentStops)
            {
                if ((now - kvp.Value).TotalSeconds > 10)
                    _recentStops.TryRemove(kvp.Key, out _);
            }
        }

        _telemetryState.CleanupSession(e.Session.Id);

        var payload = new PlaybackStopEvent
        {
            Timestamp = DateTime.UtcNow,
            SessionId = e.Session.Id,
            User = new EventUser
            {
                JellyfinUserId = jellyfinUserId,
                Username = username
            },
            Media = new PlaybackStopMedia
            {
                JellyfinMediaId = item.Id.ToString()
            },
            PositionTicks = e.PlaybackPositionTicks ?? 0,
            DurationTicks = item.RunTimeTicks ?? 0
        };

        await _apiClient.SendEventAsync(payload).ConfigureAwait(false);
    }

}
