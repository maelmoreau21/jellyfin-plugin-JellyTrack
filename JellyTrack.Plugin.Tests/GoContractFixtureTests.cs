using System.Text.Json;
using System.Text.Json.Serialization;
using JellyTrack.Plugin.Models;
using Xunit;

namespace JellyTrack.Plugin.Tests;

public sealed class GoContractFixtureTests
{
    [Fact]
    public void SerializeAllEventFamiliesForBackendContractVerification()
    {
        var now = DateTime.UtcNow;
        var user = new EventUser { JellyfinUserId = "11111111-1111-1111-1111-111111111111", Username = "Fixture User" };
        var media = new EventMedia { JellyfinMediaId = "22222222-2222-2222-2222-222222222222", Title = "Fixture Movie", Type = "Movie", DurationMs = 600_000 };
        var progressMedia = new PlaybackProgressMedia { JellyfinMediaId = media.JellyfinMediaId, Title = media.Title, Type = media.Type, DurationMs = media.DurationMs };
        var session = new EventSession { SessionId = "contract-session", ClientName = "Fixture", DeviceName = "Fixture Device", PlayMethod = "DirectPlay", PositionTicks = 600_000_000 };
        var events = new List<PluginEvent>
        {
            new HeartbeatEvent { Timestamp = now, PluginVersion = "2.1.0.0", JellyfinVersion = "12.2.0", Users = new() { new HeartbeatUser { JellyfinUserId = user.JellyfinUserId, Username = user.Username! } } },
            new LibraryChangedEvent { Timestamp = now, Items = new() { new LibraryItem { JellyfinMediaId = media.JellyfinMediaId, Title = media.Title, Type = media.Type, DurationMs = media.DurationMs } } },
            new PlaybackStartEvent { Timestamp = now, User = user, Media = media, Session = session },
            new PlaybackProgressEvent { Timestamp = now.AddSeconds(5), ObservedAtUtc = now.AddSeconds(5), SessionId = session.SessionId!, User = user, Media = progressMedia, Session = session, PositionTicks = 650_000_000 },
        };
        foreach (var change in new[] { "pause", "resume", "seek", "audio_change", "subtitle_change" })
        {
            events.Add(new PlaybackStateChangedEvent
            {
                Timestamp = now.AddSeconds(6), ObservedAtUtc = now.AddSeconds(6), SessionId = session.SessionId!,
                User = user, Media = progressMedia, Session = session, ChangeType = change, IsPaused = change == "pause",
                PositionTicks = 900_000_000, Metadata = new() { ["fromTicks"] = 650_000_000L, ["toTicks"] = 900_000_000L },
            });
        }
        var stop = new PlaybackStopEvent { Timestamp = now.AddSeconds(7), SessionId = session.SessionId!, User = user, Media = new() { JellyfinMediaId = media.JellyfinMediaId }, PositionTicks = 900_000_000, DurationTicks = 6_000_000_000 };
        events.Add(stop);
        events.Add(new PlaybackStartEvent { Timestamp = now.AddSeconds(8), User = user, Media = media, Session = session });
        events.Add(new SessionEndedEvent { Timestamp = now.AddSeconds(9), SessionId = session.SessionId!, User = user, Session = session });
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var payloads = events.Select(e =>
        {
            e.ServerId = "contract-jellyfin";
            e.ServerName = "Contract Jellyfin";
            return JsonSerializer.SerializeToElement(e, e.GetType(), options);
        }).ToArray();
        Assert.All(payloads, p => Assert.Equal(3, p.GetProperty("eventSchemaVersion").GetInt32()));
        Assert.Equal(7, payloads.Select(p => p.GetProperty("event").GetString()).Distinct().Count());
        if (Environment.GetEnvironmentVariable("JELLYTRACK_CONTRACT_OUTPUT") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(payloads, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
