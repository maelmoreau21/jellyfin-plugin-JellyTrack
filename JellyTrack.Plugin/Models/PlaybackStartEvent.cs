using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public sealed class PlaybackStartEvent : PluginEvent
{
    [JsonPropertyName("event")]
    public override string Event => "PlaybackStart";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("user")]
    public EventUser User { get; set; } = new();

    [JsonPropertyName("media")]
    public EventMedia Media { get; set; } = new();

    [JsonPropertyName("session")]
    public EventSession Session { get; set; } = new();
}

public sealed class EventUser
{
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

public sealed class EventMedia
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

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("libraryName")]
    public string? LibraryName { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }
}

public sealed class EventSession
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("clientName")]
    public string? ClientName { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("playMethod")]
    public string? PlayMethod { get; set; }

    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("videoCodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("audioCodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("audioLanguage")]
    public string? AudioLanguage { get; set; }

    [JsonPropertyName("subtitleLanguage")]
    public string? SubtitleLanguage { get; set; }

    [JsonPropertyName("subtitleCodec")]
    public string? SubtitleCodec { get; set; }

    [JsonPropertyName("transcodeFps")]
    public float? TranscodeFps { get; set; }

    [JsonPropertyName("bitrate")]
    public int? Bitrate { get; set; }

    [JsonPropertyName("positionTicks")]
    public long PositionTicks { get; set; }

    [JsonPropertyName("isPaused")]
    public bool? IsPaused { get; set; }

    [JsonPropertyName("playbackRate")]
    public double? PlaybackRate { get; set; }
}
