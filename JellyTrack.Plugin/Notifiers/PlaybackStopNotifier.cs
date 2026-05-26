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

        var (jellyfinUserId, username) = UserSnapshotResolver.ResolveUserFromSession(e.Session);
        if (string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            _logger.LogWarning("PlaybackStop ignored: could not resolve user for session {SessionId}", e.Session.Id);
            return;
        }

        var item = e.Item;

        _logger.LogInformation("PlaybackStop captured: user={UserId}, item={ItemId}, session={SessionId}", jellyfinUserId, item.Id, e.Session.Id);
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
