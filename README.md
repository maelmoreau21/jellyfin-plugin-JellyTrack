<p align="center">
  <img src="assets/banner.png" alt="JellyTrack Plugin banner">
</p>

<p align="center">
  <img src="logo.svg" width="72" height="72" alt="JellyTrack Plugin logo">
</p>

<h1 align="center">JellyTrack Plugin</h1>

<p align="center">
  <strong>Jellyfin playback events, library activity and detailed telemetry for your JellyTrack server.</strong>
</p>

<p align="center">
  <a href="https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack/actions/workflows/build.yml"><img alt="Build and test" src="https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/maelmoreau21/jellyfin-plugin-JellyTrack"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue.svg"></a>
</p>

> [!IMPORTANT]
> **Requires a JellyTrack server.** This is the **Jellyfin companion plugin**, not the JellyTrack dashboard itself. It collects events inside Jellyfin and sends them to a separate JellyTrack installation. **[JellyTrack server repository](https://github.com/maelmoreau21/JellyTrack)**.

## What does the plugin do?

The plugin enriches JellyTrack analytics by reporting supported Jellyfin playback and library events.

- **Playback lifecycle:** start, progress, stop and supported end-of-session information.
- **Advanced telemetry:** pause/resume, seeking and audio/subtitle changes where available.
- **Library activity:** library change notifications forwarded to the JellyTrack server.
- **Heartbeat:** periodic server and user synchronization information.
- **Delivery resilience:** bounded event retry queue and other resource-safety protections.
- **Configuration:** JellyTrack URL, API key, enable/disable switch, connection testing and telemetry intervals.

The actual data available depends on your Jellyfin version, its clients and playback activity. The plugin is designed to run inside Jellyfin; charts, playback history and user dashboards are provided by the separate **JellyTrack server**.

## Compatibility and downloads

The current **unreleased 2.1.0.0 candidate** targets **Jellyfin 12.2+ compatible APIs**, **.NET 10** and `targetAbi: 12.2.0.0`. The project no longer builds or advertises Jellyfin 10.x/11.x packages.

| Version | Validation of this candidate |
| --- | --- |
| Jellyfin 12.0 | Candidate DLL fails to load with the 12.0 assemblies; excluded. |
| Jellyfin 12.1 | Candidate DLL fails to load with the 12.1 assemblies; excluded. |
| Jellyfin 12.2 | Restore/build/unit tests and isolated service registration pass. Real server playback has **not** been tested. |
| Future versions | Require compatible APIs and separate verification; no blanket compatibility claim. |

The manifest retains historical Jellyfin 12 packages with their original checksums and ABI requirements. Their ABI labels do not prove runtime validation. Builds before Jellyfin 12 are removed from the catalog, while [historical GitHub releases and downloads](https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack/releases) remain unchanged. The candidate is available locally only until manually published.

See [the validation report](docs/JELLYFIN12_MODERNIZATION.md) for evidence and limitations.

## Install through Jellyfin (recommended)

This project publishes a **community Jellyfin plugin repository**. It is not the official Jellyfin-maintained plugin repository.

1. Open **Jellyfin Dashboard → Plugins → Repositories**.
2. Select **Add** (`+`).
3. Enter:
   - **Name:** `JellyTrack`
   - **Repository URL:**

     ```text
     https://raw.githubusercontent.com/maelmoreau21/jellyfin-plugin-JellyTrack/main/manifest.json
     ```
4. Go to **Dashboard → Plugins → Catalog**, find **JellyTrack** and install the compatible package.
5. **Restart Jellyfin**.
6. Open **Dashboard → Plugins → JellyTrack** to configure and enable the plugin.

GitHub-hosted repository source: [maelmoreau21/jellyfin-plugin-JellyTrack](https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack).

## Connect it to JellyTrack

**First, deploy and configure the [JellyTrack server](https://github.com/maelmoreau21/JellyTrack).** It must be accessible over the network from the machine/container running Jellyfin.

In **Jellyfin Dashboard → Plugins → JellyTrack**:

1. Enter the **JellyTrack URL**, such as `https://jellytrack.example.com` or a reachable local address.
2. Generate a plugin key in the JellyTrack server's Jellyfin/plugin settings and paste it into the **API Key** field.
3. Enable the plugin with its **Enable plugin** switch. A fresh installation starts disabled until configured.
4. Leave telemetry intervals at their defaults unless you have a specific need.
5. Use **Test connection** to verify the endpoint and credentials, then save the settings.

### Default configuration

| Setting | Current default | Purpose |
| --- | --- | --- |
| Heartbeat interval | `600` seconds | Periodic heartbeat and synchronization. |
| Playback progress interval | `5` seconds | Progress updates during playback. |
| Paused progress interval | `30` seconds | Progress updates while paused. |
| Seek detection threshold | `20` seconds | Threshold used to detect seeks. |
| Retry queue size | `500` | Maximum buffered events, including the in-flight event. |
| Retry flush batch size | `50` | Events delivered before a short 100 ms batch yield. |
| Pause/resume tracking | Enabled | Report supported pause and resume events. |
| Seek tracking | Enabled | Report supported seek events. |
| Audio/subtitle changes | Enabled | Track supported track changes. |
| Plugin enabled | **Disabled** | Enable it after checking URL and API key. |

The plugin posts authenticated JSON events to:

```text
POST /api/plugin/events
```

Events use schema v3, canonical user/media GUIDs, UTC timestamps and the Jellyfin `SystemId` as `serverId`. Both `Authorization: Bearer` and `X-Api-Key` are sent; keys scoped to the matching Jellyfin server are supported. Playback callbacks enqueue events without waiting for HTTP. A single background worker retries network/5xx/authentication/rate-limit failures every 10 seconds, keeps lifecycle order, and coalesces only adjacent progress for the same media/session.

The queue stays in memory: a restart loses pending events. At capacity it drops queued progress first, then the oldest pending event. Malformed/permanently rejected 4xx payloads are discarded; 401/403/408/429 remain retryable. Delivery cannot guarantee exactly-once telemetry if the backend accepted an event but its response was lost. A queued heartbeat does not itself prove that JellyTrack received it.

The hosted heartbeat loop owns the startup and periodic schedule; the scheduled task remains available manually. Existing Jellyfin installations may retain old saved heartbeat task triggers: remove those automatic triggers to avoid a second periodic schedule.

The Go backend currently replaces a registered server URL with its fallback when `serverUrl` is absent. Review the backend follow-up in the validation report before production rollout.

Never include your plugin API key in screenshots, GitHub issues or public logs. Prefer HTTPS when the events cross untrusted networks.

## Manual installation

Use this method only if you cannot install from the Jellyfin plugin repository.

1. Download the archive for your Jellyfin version from [Releases](https://github.com/maelmoreau21/jellyfin-plugin-JellyTrack/releases).
2. Stop Jellyfin before manually changing files in its plugin directory.
3. Place the package files together in the appropriate Jellyfin `plugins` subdirectory, following Jellyfin's manual plugin installation rules. The archive includes `meta.json`, the plugin DLL and accompanying artifacts.
4. Start Jellyfin, confirm the plugin is loaded, then configure and enable it.

The new candidate requires Jellyfin 12.2 or later compatible APIs. It cannot be installed on Jellyfin 10.x, 11.x, 12.0 or 12.1.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Plugin installed but no events arrive | Confirm **Enable plugin** is checked, the JellyTrack URL is correct, the API key is configured, and actual playback occurred. |
| Test connection fails | Verify the hostname and port **from the Jellyfin server**, not just from your desktop browser. Check reverse proxy rules and certificates. |
| HTTP 401 / unauthorized | Verify the plugin key against the JellyTrack server's current plugin credentials; check whether a key was rotated. |
| Plugin missing from the catalog | Refresh Jellyfin plugin repositories, check the manifest URL and select a package compatible with your Jellyfin version. |
| Some seek or track-change events missing | Event coverage depends on Jellyfin/client reporting and player behavior. Review plugin logs before assuming a server-side failure. |
| Plugin load or runtime errors | Confirm the installed ZIP targets the correct Jellyfin ABI and .NET runtime. |

When reporting a bug, include the Jellyfin version, plugin version, reproduction steps and sanitized logs. Remove API keys, user/session identifiers and private URLs where appropriate.

## Development

The source uses **.NET 10 only** with stable `Jellyfin.Controller` and `Jellyfin.Model` **12.2.0**. The plugin GUID and assembly name remain unchanged. Jellyfin itself resolves two transitive ICU4N alpha packages for which no stable release exists; they are not shipped in the plugin ZIP. See the validation report for the remaining upstream limitation.

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
python -m pip install pyyaml
python -m unittest discover -s scripts -p 'test_*.py'
```

Prepare the single local archive and a candidate manifest:

```powershell
dotnet publish JellyTrack.Plugin/JellyTrack.Plugin.csproj -c Release -o publish
python scripts/package_release.py
```

Outputs are in `artifacts/`. The candidate manifest contains a future release URL and must be deployed only after that archive is manually uploaded. The release workflow runs **on manual dispatch only**, prepares a downloadable CI artifact and never creates, updates or publishes a GitHub release or pushes a manifest. Historical releases are preserved.

### Repository files

- [`JellyTrack.Plugin/`](JellyTrack.Plugin/) — source and configuration UI.
- [`JellyTrack.Plugin.Tests/`](JellyTrack.Plugin.Tests/) — unit tests.
- [`Localization/`](Localization/) — translations.
- [`manifest.json`](manifest.json) — Jellyfin repository metadata and compatible releases.
- [`meta.json`](meta.json) — current plugin package metadata.
- [Build and test workflow](.github/workflows/build.yml)
- [Release workflow](.github/workflows/release.yml)

The plugin project is separate from the [JellyTrack Go application](https://github.com/maelmoreau21/JellyTrack/tree/develop). Their versions are not interchangeable; check the event/API compatibility of both projects when upgrading.

## License

Released under the [MIT License](LICENSE).
