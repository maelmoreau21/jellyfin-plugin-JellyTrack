using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyTrack.Plugin.Notifiers;

public class PlaybackProgressNotifier : IEventConsumer<PlaybackProgressEventArgs>
{
    private readonly JellyTrackApiClient _apiClient;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly PlaybackMediaStreamCache _streamCache;
    private readonly PlaybackSessionTelemetryState _telemetryState;
    private readonly ILogger<PlaybackProgressNotifier> _logger;

    public PlaybackProgressNotifier(
        JellyTrackApiClient apiClient,
        IMediaSourceManager mediaSourceManager,
        PlaybackMediaStreamCache streamCache,
        PlaybackSessionTelemetryState telemetryState,
        ILogger<PlaybackProgressNotifier> logger)
    {
        _apiClient = apiClient;
        _mediaSourceManager = mediaSourceManager;
        _streamCache = streamCache;
        _telemetryState = telemetryState;
        _logger = logger;
    }

    public async Task OnEvent(PlaybackProgressEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            return;
        }

        if (e.Item is null || e.Session is null)
        {
            _logger.LogDebug("PlaybackProgress ignored: missing item or session");
            return;
        }

        var (jellyfinUserId, username) = UserSnapshotResolver.ResolveUserFromSession(e.Session);
        if (string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            _logger.LogWarning("PlaybackProgress ignored: could not resolve user for session {SessionId}", e.Session.Id);
            return;
        }

        var sessionId = e.Session.Id;
        var item = e.Item;
        var positionTicks = e.PlaybackPositionTicks ?? e.Session.PlayState?.PositionTicks ?? 0;
        var isPaused = e.Session.PlayState?.IsPaused ?? e.IsPaused;
        var audioStreamIndex = e.Session.PlayState?.AudioStreamIndex;
        var subtitleStreamIndex = e.Session.PlayState?.SubtitleStreamIndex;

        var decision = _telemetryState.ObserveProgress(new PlaybackProgressObservation(
            SessionId: sessionId,
            PositionTicks: positionTicks,
            IsPaused: isPaused,
            AudioStreamIndex: audioStreamIndex,
            SubtitleStreamIndex: subtitleStreamIndex,
            PlayingProgressIntervalSeconds: config.ProgressIntervalSeconds,
            PausedProgressIntervalSeconds: config.PausedProgressIntervalSeconds,
            SeekThresholdSeconds: config.SeekThresholdSeconds,
            TrackPauseResume: config.TrackPauseResume,
            TrackSeek: config.TrackSeek,
            TrackAudioSubtitleChanges: config.TrackAudioSubtitleChanges,
            TimestampUtc: DateTime.UtcNow));

        if (!decision.ShouldSendProgress && decision.StateChanges.Count == 0)
        {
            return;
        }

        _logger.LogDebug("PlaybackProgress: {User} at {Position} for {Item}", username ?? jellyfinUserId, e.PlaybackPositionTicks, item.Name);

        PlaybackProgressEvent payload;
        try
        {
            payload = new PlaybackProgressEvent
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                User = new EventUser
                {
                    JellyfinUserId = jellyfinUserId,
                    Username = username
                },
                Media = new PlaybackProgressMedia
                {
                    JellyfinMediaId = item.Id.ToString(),
                    Title = item.Name,
                    Type = item.GetBaseItemKind().ToString(),
                    CollectionType = InferCollectionType(item),
                    DurationMs = item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value / 10000 : 0,
                },
                Session = BuildSessionInfo(item, e.Session),
                PositionTicks = positionTicks,
                IsPaused = isPaused,
                AudioStreamIndex = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex
            };

            if (item is Episode episode)
            {
                payload.Media.SeriesName = episode.SeriesName;
                payload.Media.SeasonName = episode.Season?.Name;
                payload.Media.ParentId = episode.SeasonId.ToString();
            }

            if (item is Audio audio)
            {
                payload.Media.AlbumName = audio.Album;
                payload.Media.AlbumArtist = audio.AlbumArtists?.FirstOrDefault();
                payload.Media.Artist = audio.Artists?.FirstOrDefault();
                if (item.ParentId != Guid.Empty)
                {
                    payload.Media.ParentId = item.ParentId.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PlaybackProgress enrichment failed for session {SessionId}. Sending minimal payload.", sessionId);
            payload = new PlaybackProgressEvent
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                User = new EventUser
                {
                    JellyfinUserId = jellyfinUserId,
                    Username = username
                },
                Media = new PlaybackProgressMedia
                {
                    JellyfinMediaId = item.Id.ToString(),
                    Title = item.Name,
                    Type = item.GetBaseItemKind().ToString(),
                    DurationMs = item.RunTimeTicks.HasValue ? item.RunTimeTicks.Value / 10000 : 0,
                },
                Session = new EventSession
                {
                    SessionId = e.Session.Id,
                    ClientName = e.Session.Client,
                    DeviceName = e.Session.DeviceName,
                    PlayMethod = e.Session.PlayState?.PlayMethod?.ToString(),
                    IpAddress = e.Session.RemoteEndPoint,
                    PositionTicks = e.Session.PlayState?.PositionTicks ?? 0,
                    IsPaused = e.Session.PlayState?.IsPaused
                },
                PositionTicks = positionTicks,
                IsPaused = isPaused,
                AudioStreamIndex = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex
            };
        }

        await SendStateChangesAsync(payload, decision.StateChanges).ConfigureAwait(false);

        if (decision.ShouldSendProgress)
        {
            await _apiClient.SendEventAsync(payload).ConfigureAwait(false);
        }
    }

    private EventSession BuildSessionInfo(BaseItem item, MediaBrowser.Controller.Session.SessionInfo session)
    {
        var sessionInfo = new EventSession
        {
            SessionId = session.Id,
            ClientName = session.Client,
            DeviceName = session.DeviceName,
            PlayMethod = session.PlayState?.PlayMethod?.ToString(),
            IpAddress = session.RemoteEndPoint,
            PositionTicks = session.PlayState?.PositionTicks ?? 0,
            IsPaused = session.PlayState?.IsPaused,
        };

        if (session.TranscodingInfo is not null)
        {
            sessionInfo.TranscodeFps = session.TranscodingInfo.Framerate;
            sessionInfo.Bitrate = session.TranscodingInfo.Bitrate;
            sessionInfo.VideoCodec = session.TranscodingInfo.VideoCodec;
            sessionInfo.AudioCodec = session.TranscodingInfo.AudioCodec;
        }

        var streams = _streamCache.GetStreams(item.Id, () => _mediaSourceManager.GetMediaStreams(item.Id)?.ToList() ?? new List<MediaStream>());
        if (streams is not null)
        {
            var audioIdx = session.PlayState?.AudioStreamIndex;
            var audioStream = audioIdx.HasValue
                ? streams.FirstOrDefault(s => s.Index == audioIdx.Value && s.Type == MediaStreamType.Audio)
                : streams.FirstOrDefault(s => s.Type == MediaStreamType.Audio && s.IsDefault);

            if (audioStream is not null)
            {
                sessionInfo.AudioLanguage = audioStream.Language;
                sessionInfo.AudioCodec ??= audioStream.Codec;
            }

            var subIdx = session.PlayState?.SubtitleStreamIndex;
            if (subIdx.HasValue && subIdx.Value >= 0)
            {
                var subStream = streams.FirstOrDefault(s => s.Index == subIdx.Value && s.Type == MediaStreamType.Subtitle);
                if (subStream is not null)
                {
                    sessionInfo.SubtitleLanguage = subStream.Language;
                    sessionInfo.SubtitleCodec = subStream.Codec;
                }
            }

            if (string.IsNullOrEmpty(sessionInfo.VideoCodec))
            {
                var videoStream = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
                sessionInfo.VideoCodec = videoStream?.Codec;
            }
        }

        return sessionInfo;
    }

    private static string InferCollectionType(BaseItem item)
    {
        return item switch
        {
            Episode => "tvshows",
            Audio => "music",
            _ => "movies"
        };
    }

    /// <summary>
    /// Clean up stale session entries to prevent memory leaks.
    /// Called periodically or on session end.
    /// </summary>
    internal void CleanupSession(string sessionId)
    {
        _telemetryState.CleanupSession(sessionId);
    }

    private async Task SendStateChangesAsync(PlaybackProgressEvent progressPayload, IReadOnlyList<PlaybackStateChange> changes)
    {
        foreach (var change in changes)
        {
            var payload = new PlaybackStateChangedEvent
            {
                Timestamp = DateTime.UtcNow,
                SessionId = progressPayload.SessionId,
                ChangeType = change.ChangeType,
                User = progressPayload.User,
                Media = progressPayload.Media,
                Session = progressPayload.Session,
                PositionTicks = progressPayload.PositionTicks,
                PreviousPositionTicks = change.PreviousPositionTicks,
                IsPaused = progressPayload.IsPaused,
                AudioStreamIndex = progressPayload.AudioStreamIndex,
                SubtitleStreamIndex = progressPayload.SubtitleStreamIndex,
                Metadata = change.Metadata,
            };

            await _apiClient.SendEventAsync(payload).ConfigureAwait(false);
        }
    }
}
