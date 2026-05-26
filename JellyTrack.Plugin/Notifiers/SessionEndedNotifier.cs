using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Notifiers;

public class SessionEndedNotifier : IEventConsumer<SessionEndedEventArgs>
{
    private readonly JellyTrackApiClient _apiClient;
    private readonly PlaybackSessionTelemetryState _telemetryState;
    private readonly ILogger<SessionEndedNotifier> _logger;

    public SessionEndedNotifier(
        JellyTrackApiClient apiClient,
        PlaybackSessionTelemetryState telemetryState,
        ILogger<SessionEndedNotifier> logger)
    {
        _apiClient = apiClient;
        _telemetryState = telemetryState;
        _logger = logger;
    }

    public async Task OnEvent(SessionEndedEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled || !config.TrackSessionEnded)
        {
            return;
        }

        var session = e.Argument;
        if (session is null || string.IsNullOrWhiteSpace(session.Id))
        {
            return;
        }

        var (jellyfinUserId, username) = UserSnapshotResolver.ResolveUserFromSession(session);
        _telemetryState.CleanupSession(session.Id);

        var payload = new SessionEndedEvent
        {
            Timestamp = DateTime.UtcNow,
            SessionId = session.Id,
            User = string.IsNullOrWhiteSpace(jellyfinUserId)
                ? null
                : new EventUser
                {
                    JellyfinUserId = jellyfinUserId,
                    Username = username,
                },
            Session = new EventSession
            {
                SessionId = session.Id,
                ClientName = session.Client,
                DeviceName = session.DeviceName,
                PlayMethod = session.PlayState?.PlayMethod?.ToString(),
                IpAddress = session.RemoteEndPoint,
                PositionTicks = session.PlayState?.PositionTicks ?? 0,
                IsPaused = session.PlayState?.IsPaused,
            },
        };

        _logger.LogInformation("SessionEnded captured: session={SessionId}, user={UserId}", session.Id, jellyfinUserId ?? "unknown");
        await _apiClient.SendEventAsync(payload).ConfigureAwait(false);
    }
}
