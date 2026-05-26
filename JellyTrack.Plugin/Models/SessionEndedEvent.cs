using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public sealed class SessionEndedEvent : PluginEvent
{
    [JsonPropertyName("event")]
    public override string Event => "SessionEnded";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public EventUser? User { get; set; }

    [JsonPropertyName("session")]
    public EventSession Session { get; set; } = new();
}
