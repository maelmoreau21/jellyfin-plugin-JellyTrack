using JellyTrack.Plugin.Services;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class PlaybackSessionTelemetryStateTests
{
    [Fact]
    public void PauseImmediatelyAfterStartIsCaptured()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        state.InitializeSession(Observation(timestampUtc: now));
        var decision = state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(1), isPaused: true));
        Assert.Equal("pause", Assert.Single(decision.StateChanges).ChangeType);
    }

    [Fact]
    public void ResumeAndTrackChangesBypassThrottleWithoutDuplicates()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        state.InitializeSession(Observation(timestampUtc: now, isPaused: true, subtitleStreamIndex: -1));
        var next = Observation(timestampUtc: now.AddSeconds(1), audioStreamIndex: 2, subtitleStreamIndex: 3);
        var decision = state.ObserveProgress(next);
        Assert.Equal(new[] { "resume", "audio_change", "subtitle_change" }, decision.StateChanges.Select(x => x.ChangeType));
        Assert.False(decision.ShouldSendProgress);
        Assert.Empty(state.ObserveProgress(next).StateChanges);
    }

    [Fact]
    public void BackwardSeekAfterLongGapIsDetected()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        state.InitializeSession(Observation(timestampUtc: now, positionTicks: 1_000_000_000));
        var decision = state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(120), positionTicks: 400_000_000));
        Assert.Equal("backward", Assert.Single(decision.StateChanges).Metadata["direction"]);
    }

    [Fact]
    public void ConcurrentDuplicateObservationsProduceOneTransition()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        state.InitializeSession(Observation(timestampUtc: now));
        var changes = new System.Collections.Concurrent.ConcurrentBag<PlaybackStateChange>();
        Parallel.For(0, 100, _ =>
        {
            foreach (var change in state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(1), isPaused: true)).StateChanges)
                changes.Add(change);
        });
        Assert.Equal("pause", Assert.Single(changes).ChangeType);
    }

    [Fact]
    public void PlayingAndPausedProgressUseConfiguredCadence()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        state.InitializeSession(Observation(timestampUtc: now));
        Assert.False(state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(4), positionTicks: 40_000_000)).ShouldSendProgress);
        Assert.True(state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(5), positionTicks: 50_000_000)).ShouldSendProgress);
        state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(6), positionTicks: 50_000_000, isPaused: true));
        Assert.False(state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(34), positionTicks: 50_000_000, isPaused: true)).ShouldSendProgress);
        Assert.True(state.ObserveProgress(Observation(timestampUtc: now.AddSeconds(35), positionTicks: 50_000_000, isPaused: true)).ShouldSendProgress);
    }

    [Fact]
    public void FirstObservationSendsProgressWithoutTransitions()
    {
        var state = new PlaybackSessionTelemetryState();

        var decision = state.ObserveProgress(Observation(positionTicks: 10_000_000));

        Assert.True(decision.ShouldSendProgress);
        Assert.Empty(decision.StateChanges);
    }

    [Fact]
    public void PauseBypassesProgressThrottle()
    {
        var state = new PlaybackSessionTelemetryState();
        var start = DateTime.UtcNow;
        state.ObserveProgress(Observation(timestampUtc: start, positionTicks: 10_000_000));

        var decision = state.ObserveProgress(Observation(
            timestampUtc: start.AddSeconds(1),
            positionTicks: 11_000_000,
            isPaused: true));

        Assert.False(decision.ShouldSendProgress);
        var change = Assert.Single(decision.StateChanges);
        Assert.Equal("pause", change.ChangeType);
    }

    [Fact]
    public void SeekBypassesProgressThrottle()
    {
        var state = new PlaybackSessionTelemetryState();
        var start = DateTime.UtcNow;
        state.ObserveProgress(Observation(timestampUtc: start, positionTicks: 10_000_000));

        var decision = state.ObserveProgress(Observation(
            timestampUtc: start.AddSeconds(2),
            positionTicks: 90_000_000,
            seekThresholdSeconds: 5));

        Assert.False(decision.ShouldSendProgress);
        var change = Assert.Single(decision.StateChanges);
        Assert.Equal("seek", change.ChangeType);
    }

    [Fact]
    public void CleanupSessionResetsThrottleState()
    {
        var state = new PlaybackSessionTelemetryState();
        var start = DateTime.UtcNow;
        state.ObserveProgress(Observation(timestampUtc: start, positionTicks: 10_000_000));

        state.CleanupSession("session-1");
        var decision = state.ObserveProgress(Observation(timestampUtc: start.AddSeconds(1), positionTicks: 11_000_000));

        Assert.True(decision.ShouldSendProgress);
        Assert.Empty(decision.StateChanges);
    }

    private static PlaybackProgressObservation Observation(
        DateTime? timestampUtc = null,
        long positionTicks = 0,
        bool isPaused = false,
        int? audioStreamIndex = 1,
        int? subtitleStreamIndex = null,
        int seekThresholdSeconds = 20)
    {
        return new PlaybackProgressObservation(
            SessionId: "session-1",
            PositionTicks: positionTicks,
            IsPaused: isPaused,
            AudioStreamIndex: audioStreamIndex,
            SubtitleStreamIndex: subtitleStreamIndex,
            PlayingProgressIntervalSeconds: 5,
            PausedProgressIntervalSeconds: 30,
            SeekThresholdSeconds: seekThresholdSeconds,
            TrackPauseResume: true,
            TrackSeek: true,
            TrackAudioSubtitleChanges: true,
            TimestampUtc: timestampUtc ?? DateTime.UtcNow);
    }
}
