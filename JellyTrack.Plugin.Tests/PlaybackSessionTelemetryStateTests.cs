using JellyTrack.Plugin.Services;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class PlaybackSessionTelemetryStateTests
{
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
