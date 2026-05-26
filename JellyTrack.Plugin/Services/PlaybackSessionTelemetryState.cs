using System.Collections.Concurrent;

namespace JellyTrack.Plugin.Services;

public sealed class PlaybackSessionTelemetryState
{
    private readonly ConcurrentDictionary<string, PlaybackSessionSnapshot> _sessions = new();

    public PlaybackProgressDecision ObserveProgress(PlaybackProgressObservation observation)
    {
        var now = observation.TimestampUtc;
        var thresholdTicks = Math.Max(1, observation.SeekThresholdSeconds) * 10_000_000L;
        var interval = TimeSpan.FromSeconds(observation.IsPaused
            ? Math.Max(1, observation.PausedProgressIntervalSeconds)
            : Math.Max(1, observation.PlayingProgressIntervalSeconds));

        var changes = new List<PlaybackStateChange>();
        var shouldSendProgress = false;

        _sessions.AddOrUpdate(
            observation.SessionId,
            _ =>
            {
                shouldSendProgress = true;
                return PlaybackSessionSnapshot.FromObservation(observation, now, progressSentUtc: now);
            },
            (_, previous) =>
            {
                if (observation.TrackPauseResume && previous.IsPaused != observation.IsPaused)
                {
                    changes.Add(new PlaybackStateChange(
                        observation.IsPaused ? "pause" : "resume",
                        previous.PositionTicks,
                        new Dictionary<string, object?>
                        {
                            ["fromPaused"] = previous.IsPaused,
                            ["toPaused"] = observation.IsPaused,
                        }));
                }

                if (observation.TrackSeek)
                {
                    var wallDeltaTicks = Math.Max(0, (long)(now - previous.LastSeenUtc).TotalSeconds * 10_000_000L);
                    var tickDelta = observation.PositionTicks - previous.PositionTicks;
                    var expectedBudget = Math.Max(thresholdTicks, wallDeltaTicks + thresholdTicks);
                    if (Math.Abs(tickDelta) >= thresholdTicks && Math.Abs(tickDelta) > expectedBudget)
                    {
                        changes.Add(new PlaybackStateChange(
                            "seek",
                            previous.PositionTicks,
                            new Dictionary<string, object?>
                            {
                                ["fromTicks"] = previous.PositionTicks,
                                ["toTicks"] = observation.PositionTicks,
                                ["deltaTicks"] = tickDelta,
                                ["direction"] = tickDelta >= 0 ? "forward" : "backward",
                            }));
                    }
                }

                if (observation.TrackAudioSubtitleChanges
                    && previous.AudioStreamIndex.HasValue
                    && observation.AudioStreamIndex.HasValue
                    && previous.AudioStreamIndex.Value != observation.AudioStreamIndex.Value)
                {
                    changes.Add(new PlaybackStateChange(
                        "audio_change",
                        previous.PositionTicks,
                        new Dictionary<string, object?>
                        {
                            ["fromIndex"] = previous.AudioStreamIndex.Value,
                            ["toIndex"] = observation.AudioStreamIndex.Value,
                        }));
                }

                if (observation.TrackAudioSubtitleChanges
                    && previous.SubtitleStreamIndex != observation.SubtitleStreamIndex)
                {
                    changes.Add(new PlaybackStateChange(
                        "subtitle_change",
                        previous.PositionTicks,
                        new Dictionary<string, object?>
                        {
                            ["fromIndex"] = previous.SubtitleStreamIndex,
                            ["toIndex"] = observation.SubtitleStreamIndex,
                        }));
                }

                shouldSendProgress = now - previous.LastProgressSentUtc >= interval;
                return PlaybackSessionSnapshot.FromObservation(
                    observation,
                    now,
                    progressSentUtc: shouldSendProgress ? now : previous.LastProgressSentUtc);
            });

        return new PlaybackProgressDecision(shouldSendProgress, changes);
    }

    public void CleanupSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    public void Clear()
    {
        _sessions.Clear();
    }

    private sealed record PlaybackSessionSnapshot(
        long PositionTicks,
        bool IsPaused,
        int? AudioStreamIndex,
        int? SubtitleStreamIndex,
        DateTime LastSeenUtc,
        DateTime LastProgressSentUtc)
    {
        public static PlaybackSessionSnapshot FromObservation(
            PlaybackProgressObservation observation,
            DateTime now,
            DateTime progressSentUtc)
        {
            return new PlaybackSessionSnapshot(
                observation.PositionTicks,
                observation.IsPaused,
                observation.AudioStreamIndex,
                observation.SubtitleStreamIndex,
                now,
                progressSentUtc);
        }
    }
}

public sealed record PlaybackProgressObservation(
    string SessionId,
    long PositionTicks,
    bool IsPaused,
    int? AudioStreamIndex,
    int? SubtitleStreamIndex,
    int PlayingProgressIntervalSeconds,
    int PausedProgressIntervalSeconds,
    int SeekThresholdSeconds,
    bool TrackPauseResume,
    bool TrackSeek,
    bool TrackAudioSubtitleChanges,
    DateTime TimestampUtc);

public sealed record PlaybackProgressDecision(
    bool ShouldSendProgress,
    IReadOnlyList<PlaybackStateChange> StateChanges);

public sealed record PlaybackStateChange(
    string ChangeType,
    long? PreviousPositionTicks,
    Dictionary<string, object?> Metadata);
