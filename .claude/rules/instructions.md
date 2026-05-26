#+ NOTE: Single reference file for AI agents working on the plugin.

![JellyTrack Plugin Logo](../../assets/banner.png)

# JellyTrack Plugin - AI Agent Instructions (v1.6.0.0)

IMPORTANT (for AI agents) - read this document before proposing changes.

- Do not hallucinate payload formats, event fields, or i18n keys.
  - Canonical sources: `JellyTrack.Plugin/Models/*.cs`, `JellyTrack.Plugin/Notifiers/*`, `JellyTrack.Plugin/Services/*`.
- Do not invent the server contract.
  - Canonical app source: `JellyTrack/src/app/api/plugin/events/route.ts`.
- Respect endpoint conventions, event schema, and plugin key contract from the parent project.
- Recommended install method for end users: Jellyfin catalog via `manifest.json`.
- Do not commit, push, create branches, or merge without an explicit user request.

## 1. Overview

The JellyTrack plugin (C#) emits Jellyfin events to the JellyTrack app (Next.js).

Main pipeline:

1. Capture local events (start/progress/state/stop/library + heartbeat).
2. Standard JSON serialization (`System.Text.Json`).
3. HTTP POST to the configured endpoint (default `/api/plugin/events`).
4. Local retry with a bounded in-memory queue.

Goal: reliable, idempotent, low-noise telemetry.

## 2. Canonical stack

- Language: C# (`net9.0`)
- Patterns: `IEventConsumer<T>`, `IScheduledTask`, `IHostedService`
- Serialization: `System.Text.Json`
- Networking: `HttpClient` via `IHttpClientFactory`
- Jellyfin API: `Jellyfin.Controller` + `Jellyfin.Model` (10.11.x, `JellyfinPackageVersion=10.11.10`)

## 3. Compatibility contract with JellyTrack app

Server reference: `JellyTrack/src/app/api/plugin/events/route.ts`

### 3.1 Event types emitted by the plugin

Source: `JellyTrack.Plugin/Models/*.cs`

- `Heartbeat`
- `PlaybackStart`
- `PlaybackProgress`
- `PlaybackStateChanged`
- `PlaybackStop`
- `SessionEnded`
- `LibraryChanged`

Note: the server route must accept all emitted event types. Verify the app contract before changing payloads.

### 3.2 Required schema version

- `eventSchemaVersion` must be present on all payloads.
- Current plugin schema version: `3` (see `JellyTrack.Plugin/Models/PluginEvent.cs`).
- If the server contract changes, update both app and plugin docs.

### 3.3 Plugin key auth (hash-at-rest on app side)

- The plugin sends the raw key (never a hash) only over HTTP.
- Supported and recommended headers:
  - `Authorization: Bearer <pluginKey>`
  - `X-Api-Key: <pluginKey>`
- Plugin source: `JellyTrack.Plugin/Services/JellyTrackApiClient.cs`.
- App side compares against the stored scrypt hash (`pluginApiKey`) via timing-safe compare.

Important:
- Never attempt to reproduce the scrypt hash in the plugin.
- Never store the key outside the Jellyfin plugin configuration.

### 3.4 Multi-server scoped keys

- App format: `jts3.<serverIdBase64url>.<rawKey>`.
- The plugin must pass this token as-is (no parsing).
- The server extracts the scoped part and checks it against the payload `serverId`.

## 4. Heartbeat policy (network performance)

Source: `JellyTrack.Plugin/Services/HeartbeatService.cs` + `JellyTrack.Plugin/PluginConfiguration.cs`

- First heartbeat is sent immediately on startup.
- Default interval: `600` seconds (10 minutes).
- Minimum interval: `300` seconds (5 minutes).
- Invalid or <=0 values fall back to 600 seconds.

Goal: reduce network noise while keeping a periodic health signal.

## 5. Robustness and resilience

Source: `JellyTrack.Plugin/Services/JellyTrackApiClient.cs`

- Short HTTP timeout (5s) to avoid blocking the Jellyfin host.
- Retry queue is bounded by `RetryQueueSize` (default 500, min 10).
- Flush queued events before sending a new event.
- On network/API failure: requeue and retry on subsequent sends.
- `PlaybackProgress` retries can be coalesced; see `JellyTrackApiClient`.

## 6. Working structure (useful map)

- `JellyTrack.Plugin/Plugin.cs`: plugin definition + config page
- `JellyTrack.Plugin/PluginConfiguration.cs`: persisted options
- `JellyTrack.Plugin/PluginServiceRegistrator.cs`: DI/service registration
- `JellyTrack.Plugin/Api/JellyTrackController.cs`: admin plugin endpoints
- `JellyTrack.Plugin/Configuration/configPage.html`: configuration UI
- `JellyTrack.Plugin/Models/*.cs`: event contract
- `JellyTrack.Plugin/Notifiers/*.cs`: playback notifiers
- `JellyTrack.Plugin/Services/JellyTrackApiClient.cs`: HTTP client to app
- `JellyTrack.Plugin/Services/HeartbeatService.cs`: periodic heartbeat
- `JellyTrack.Plugin/Services/LibraryChangeNotifier.cs`: debounce/batch library changes

## 7. Plugin internationalization

- Files: `Localization/*.json`.
- `configPage.html` loads translations via the plugin endpoint.
- Any new UI key must be added in all plugin locales.

## 8. Zero technical-debt quality rules

Before finalization:

1. Verify contract compatibility with `JellyTrack/src/app/api/plugin/events/route.ts`.
2. Verify `eventSchemaVersion` for any new event.
3. Verify auth headers remain compatible (`Authorization` + `X-Api-Key`).
4. Run `dotnet build` in `JellyTrack.Plugin/JellyTrack.Plugin`.
5. If the contract changes, update in parallel:
   - `JellyTrack/.claude/rules/instructions.md`
   - `JellyTrack.Plugin/.claude/rules/instructions.md`
6. Ensure no real secrets are added to the repo (manifest/config/doc).

## 9. Reference commands

- Build plugin: `dotnet build JellyTrack.Plugin/JellyTrack.Plugin/JellyTrack.Plugin.csproj`
- Build solution: `dotnet build JellyTrack.Plugin/plugin-jellytrack.sln`
- Update manifest: `python scripts/update_manifest.py`

## 10. Anti-hallucination checklist

Always verify before proposing:

- `JellyTrack.Plugin/Models/*.cs`
- `JellyTrack.Plugin/Services/JellyTrackApiClient.cs`
- `JellyTrack.Plugin/Services/HeartbeatService.cs`
- `JellyTrack.Plugin/Configuration/configPage.html`
- `JellyTrack/src/app/api/plugin/events/route.ts`
- `JellyTrack/src/lib/pluginKeyManager.ts`
- `JellyTrack/src/lib/pluginServerKey.ts`

If in doubt: read the file, do not guess.

---

This document is the reference for JellyTrack Plugin v1.6.0.0.
Any contract change (payload, auth, schema version) must update this document in the same PR.