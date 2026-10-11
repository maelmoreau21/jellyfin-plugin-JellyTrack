using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using JellyTrack.Plugin.Models;
using JellyTrack.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class EventDeliveryTests
{
    private static PluginConfiguration Configuration() => new()
    {
        Enabled = true, JellyTrackUrl = "https://jellytrack.example.com", ApiKey = "test-key",
    };

    private static JellyTrackApiClient Client(HttpClient http, PluginConfiguration config) => new(
        http, NullLogger<JellyTrackApiClient>.Instance, configuration: () => config, serverId: "jellyfin-server-id")
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public async Task EnqueueReturnsWhileHttpIsBlockedAndShutdownCancelsRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var client = Client(http, Configuration());
        await client.StartAsync(default);
        Assert.True(await client.SendEventAsync(new PlaybackStartEvent()).WaitAsync(TimeSpan.FromSeconds(1)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(await client.SendEventAsync(new PlaybackStopEvent()).WaitAsync(TimeSpan.FromSeconds(1)));
        await client.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ReconnectRetriesWithoutNewEventsAndPreservesLifecycleOrder()
    {
        var attempts = new ConcurrentQueue<string>();
        var first = true;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            attempts.Enqueue(body.RootElement.GetProperty("event").GetString()!);
            var code = first ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            first = false;
            return new HttpResponseMessage(code);
        }));
        using var client = Client(http, Configuration());
        await client.SendEventAsync(new PlaybackStartEvent());
        await client.SendEventAsync(new PlaybackStopEvent());
        await client.StartAsync(default);
        await Eventually(() => attempts.Count == 3 && client.GetRuntimeMetricsSnapshot().QueueDepth == 0);
        Assert.Equal(new[] { "PlaybackStart", "PlaybackStart", "PlaybackStop" }, attempts.ToArray());
        Assert.Equal(1, client.GetRuntimeMetricsSnapshot().RetryAttempts);
        await client.StopAsync(default);
    }

    [Fact]
    public async Task AdjacentProgressCoalescesWithoutCrossingStopAndResume()
    {
        var sent = new ConcurrentQueue<string>();
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            sent.Enqueue(root.GetProperty("event").GetString() + ":" +
                (root.TryGetProperty("positionTicks", out var position) ? position.GetInt64() : 0));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var client = Client(http, Configuration());
        await client.SendEventAsync(new PlaybackStartEvent());
        await client.SendEventAsync(Progress(1));
        await client.SendEventAsync(Progress(2));
        await client.SendEventAsync(new PlaybackStopEvent());
        await client.SendEventAsync(new PlaybackStartEvent());
        await client.SendEventAsync(Progress(3));
        Assert.Equal(1, client.GetRuntimeMetricsSnapshot().CoalescedProgressEvents);
        await client.StartAsync(default);
        await Eventually(() => sent.Count == 5);
        Assert.Equal(new[] { "PlaybackStart:0", "PlaybackProgress:2", "PlaybackStop:0", "PlaybackStart:0", "PlaybackProgress:3" }, sent.ToArray());
        await client.StopAsync(default);
    }

    [Fact]
    public async Task ConcurrentQueueRemainsBounded()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var config = Configuration();
        config.RetryQueueSize = 10;
        using var client = Client(http, config);
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(_ => Task.Run(() => client.SendEventAsync(new PlaybackStartEvent()))));
        Assert.Equal(10, client.GetRuntimeMetricsSnapshot().QueueDepth);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    public async Task PermanentRejectionsDoNotBlockQueueButTransientFailuresRetry(HttpStatusCode status, bool retry)
    {
        var count = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            Interlocked.Increment(ref count) == 1 ? status : HttpStatusCode.OK))));
        using var client = Client(http, Configuration());
        await client.SendEventAsync(new HeartbeatEvent());
        await client.StartAsync(default);
        await Eventually(() => client.GetRuntimeMetricsSnapshot().QueueDepth == 0);
        Assert.Equal(retry ? 2 : 1, count);
        await client.StopAsync(default);
    }

    [Fact]
    public async Task WirePayloadIncludesServerIdentityAuthenticationSchemaAndUtcResumePosition()
    {
        var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            Assert.Equal("/api/plugin/events", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            delivered.TrySetResult(await request.Content.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var client = Client(http, Configuration());
        var user = Guid.NewGuid().ToString("D");
        var media = Guid.NewGuid().ToString("D");
        await client.SendEventAsync(new PlaybackStartEvent
        {
            Timestamp = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc),
            User = new EventUser { JellyfinUserId = user },
            Media = new EventMedia { JellyfinMediaId = media },
            Session = new EventSession { SessionId = "session-id", PositionTicks = 600_000_000 },
        });
        await client.StartAsync(default);
        using var json = JsonDocument.Parse(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal("jellyfin-server-id", json.RootElement.GetProperty("serverId").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("eventSchemaVersion").GetInt32());
        Assert.Equal("2026-10-11T12:00:00Z", json.RootElement.GetProperty("timestamp").GetString());
        Assert.Equal(user, json.RootElement.GetProperty("user").GetProperty("jellyfinUserId").GetString());
        Assert.Equal(media, json.RootElement.GetProperty("media").GetProperty("jellyfinMediaId").GetString());
        Assert.Equal(600_000_000, json.RootElement.GetProperty("session").GetProperty("positionTicks").GetInt64());
        await client.StopAsync(default);
    }

    [Fact]
    public async Task DisabledInvalidAndCancelledEventsAreNotQueued()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("HTTP should not run")));
        var config = Configuration();
        using var client = Client(http, config);
        config.Enabled = false;
        Assert.False(await client.SendEventAsync(new HeartbeatEvent()));
        config.Enabled = true;
        config.JellyTrackUrl = "http://169.254.169.254";
        Assert.False(await client.SendEventAsync(new HeartbeatEvent()));
        config.JellyTrackUrl = "https://example.com";
        Assert.False(await client.SendEventAsync(new HeartbeatEvent(), new CancellationToken(true)));
        Assert.Equal(0, client.GetRuntimeMetricsSnapshot().QueueDepth);
    }

    private static PlaybackProgressEvent Progress(long ticks) => new()
    {
        SessionId = "same-session", PositionTicks = ticks,
        Media = new PlaybackProgressMedia { JellyfinMediaId = "same-media" },
    };

    [Fact]
    public async Task ConnectionTestRedactsAnApiKeyEchoedByTheRemoteServer()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Rejected test-key"),
        })));
        using var client = Client(http, Configuration());
        var result = await client.TestConnectionAsync("https://example.com", "test-key", new HeartbeatEvent());
        Assert.False(result.Success);
        Assert.DoesNotContain("test-key", result.Message);
        Assert.Contains("[redacted]", result.Message);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
