using System.Text.Json.Serialization;

namespace JellyTrack.Plugin.Models;

public abstract class PluginEvent
{
    // Version 1 is the historical payload format without explicit schema metadata.
    // Version 2 introduces eventSchemaVersion in all plugin events.
    // Version 3 adds immediate state/session telemetry for Jellyfin 12.
    public const int CurrentSchemaVersion = 3;

    [JsonPropertyName("event")]
    public abstract string Event { get; }

    [JsonPropertyName("eventSchemaVersion")]
    public virtual int EventSchemaVersion => CurrentSchemaVersion;
}
