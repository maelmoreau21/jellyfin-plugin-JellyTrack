using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public sealed class PlaybackProgressEvent : PluginEvent
{
    [JsonPropertyName("event")]
    public override string Event => "PlaybackProgress";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("observedAtUtc")]
    public DateTime ObservedAtUtc { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public EventUser User { get; set; } = new();

    [JsonPropertyName("media")]
    public PlaybackProgressMedia Media { get; set; } = new();

    [JsonPropertyName("session")]
    public EventSession Session { get; set; } = new();

    [JsonPropertyName("positionTicks")]
    public long PositionTicks { get; set; }

    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("audioStreamIndex")]
    public int? AudioStreamIndex { get; set; }

    [JsonPropertyName("subtitleStreamIndex")]
    public int? SubtitleStreamIndex { get; set; }

    [JsonPropertyName("playbackRate")]
    public double? PlaybackRate { get; set; }
}

public sealed class PlaybackProgressMedia
{
    [JsonPropertyName("jellyfinMediaId")]
    public string JellyfinMediaId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("collectionType")]
    public string? CollectionType { get; set; }

    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; set; }

    [JsonPropertyName("seasonName")]
    public string? SeasonName { get; set; }

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    [JsonPropertyName("albumArtist")]
    public string? AlbumArtist { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }
}
