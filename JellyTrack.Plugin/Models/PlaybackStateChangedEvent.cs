using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public sealed class PlaybackStateChangedEvent : PluginEvent
{
    [JsonPropertyName("event")]
    public override string Event => "PlaybackStateChanged";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public EventUser User { get; set; } = new();

    [JsonPropertyName("media")]
    public PlaybackProgressMedia Media { get; set; } = new();

    [JsonPropertyName("session")]
    public EventSession Session { get; set; } = new();

    [JsonPropertyName("positionTicks")]
    public long PositionTicks { get; set; }

    [JsonPropertyName("previousPositionTicks")]
    public long? PreviousPositionTicks { get; set; }

    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("audioStreamIndex")]
    public int? AudioStreamIndex { get; set; }

    [JsonPropertyName("subtitleStreamIndex")]
    public int? SubtitleStreamIndex { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}
