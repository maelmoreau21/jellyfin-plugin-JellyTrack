using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public sealed class HeartbeatEvent : PluginEvent
{
    [JsonPropertyName("event")]
    public override string Event => "Heartbeat";

    [JsonPropertyName("pluginVersion")]
    public string PluginVersion { get; set; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("jellyfinVersion")]
    public string JellyfinVersion { get; set; } = string.Empty;

    [JsonPropertyName("users")]
    public List<HeartbeatUser> Users { get; set; } = new();

    [JsonPropertyName("serverLanguage")]
    public string? ServerLanguage { get; set; }

    [JsonPropertyName("pluginMetrics")]
    public HeartbeatPluginMetrics? PluginMetrics { get; set; }
}

public sealed class HeartbeatPluginMetrics
{
    [JsonPropertyName("queueDepth")]
    public int QueueDepth { get; set; }

    [JsonPropertyName("retries")]
    public int Retries { get; set; }

    [JsonPropertyName("lastHttpCode")]
    public int? LastHttpCode { get; set; }

    [JsonPropertyName("coalescedProgressEvents")]
    public int CoalescedProgressEvents { get; set; }
}

public sealed class HeartbeatUser
{
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}
