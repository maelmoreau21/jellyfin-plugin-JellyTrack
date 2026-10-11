using System.Diagnostics;
using System.Text.Json;
using JellyTrack.Plugin.Services;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class TelemetryLoadTests
{
    [Fact]
    public void HundredSessionsThrottleOneHundredThousandObservations()
    {
        var state = new PlaybackSessionTelemetryState();
        var now = DateTime.UtcNow;
        var sessionIds = Enumerable.Range(0, 100).Select(i => "load-session-" + i).ToArray();
        foreach (var sessionId in sessionIds) state.InitializeSession(Observe(sessionId, now, 0));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var emitted = 0;
        for (var second = 1; second <= 1000; second++)
        {
            foreach (var sessionId in sessionIds)
            {
                var decision = state.ObserveProgress(Observe(sessionId, now.AddSeconds(second), second * 10_000_000L));
                if (decision.ShouldSendProgress) emitted++;
                Assert.Empty(decision.StateChanges);
            }
        }
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(20_000, emitted);
        if (Environment.GetEnvironmentVariable("JELLYTRACK_CONTRACT_OUTPUT") is { Length: > 0 } output)
        {
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "telemetry-load.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { sessions = 100, observations = 100_000, emitted, elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytesOnTestThread = allocated }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static PlaybackProgressObservation Observe(string sessionId, DateTime time, long ticks) => new(
        sessionId, ticks, false, 1, -1, 5, 30, 20, true, true, true, time);
}
