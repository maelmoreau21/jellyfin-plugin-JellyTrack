<p align="center">
  <img src="assets/banner.png" alt="JellyTrack Plugin Banner">
</p>

<p align="center">
  <img src="logo.svg" width="64" height="64" alt="JellyTrack Logo">
</p>

<h1 align="center">JellyTrack Plugin</h1>

<p align="center">
  <img src="https://img.shields.io/github/v/release/maelmoreau21/Jellyfin.Plugin.JellyTrack" alt="GitHub Release">
  <img src="https://img.shields.io/github/license/maelmoreau21/Jellyfin.Plugin.JellyTrack" alt="License">
</p>

<p align="center">
  <strong>Jellyfin plugin for JellyTrack: captures and sends playback events and metadata in real time to your analytics server.</strong>
</p>

---

> [!IMPORTANT]
> ### JellyTrack server required
> This plugin only works when connected to an active **JellyTrack** instance. Without a server, it has no visible effect.
>
> [Deploy the JellyTrack server](https://github.com/maelmoreau21/JellyTrack)

---

## Install via Jellyfin repository

Prefer installing via the official repository to get automatic updates in Jellyfin.

### 1. Add the repository

1. In Jellyfin: **Dashboard** > **Plugins** > **Repositories**.
2. Click the `+` button (Add).
3. Fill in:
   - **Name**: `JellyTrack`
   - **URL**: `https://raw.githubusercontent.com/maelmoreau21/Jellyfin.Plugin.JellyTrack/main/manifest.json`

### 2. Install

1. Go to the **Catalog** tab.
2. Search for **JellyTrack** and install it.
3. **Restart Jellyfin** to enable the plugin.

---

## Configuration

Once installed, go to **Dashboard** > **Plugins** > **JellyTrack** to configure the connection:

- **JellyTrack URL**: your server address, for example `http://192.168.1.100:3000`.
- **API key**: the key generated in JellyTrack settings, format `jt_xxxxxxxxxxxx`.
- **Heartbeat interval**: health check cadence, default `600s`.
- **Telemetry**: default progress intervals are `5s` during playback and `30s` when paused.

> [!TIP]
> Use the **Test connection** button to confirm the plugin can reach your server before saving.

---

## Build Jellyfin 10.11 public

Version `1.6.0.0` targets Jellyfin `10.11.x` with `JellyfinPackageVersion=10.11.10` and `targetAbi=10.11.0.0`.

```powershell
dotnet restore .\plugin-jellytrack.sln `
  -p:JellyfinPackageVersion=10.11.10

dotnet build .\plugin-jellytrack.sln -c Release --no-restore `
  -p:JellyfinPackageVersion=10.11.10 `
  -warnaserror:CS0618
```

To publish `1.6.0.0`, create `Jellyfin.Plugin.JellyTrack-1.6.0.0.zip` from the Release build, then run `scripts/update_manifest.py` with `REPO=maelmoreau21/Jellyfin.Plugin.JellyTrack` and `RELEASE_TAG=v1.6.0.0` to compute the checksum of the real zip.

---

## Manual installation

If you cannot use the repository:

1. Download `Jellyfin.Plugin.JellyTrack-1.6.0.0.zip` from [Releases](https://github.com/maelmoreau21/Jellyfin.Plugin.JellyTrack/releases).
2. Create a `JellyTrack` folder inside your Jellyfin `plugins` directory.
3. Extract `meta.json`, `Jellyfin.Plugin.JellyTrack.deps.json`, `Jellyfin.Plugin.JellyTrack.dll`, `Jellyfin.Plugin.JellyTrack.pdb`, and `Jellyfin.Plugin.JellyTrack.png`, then restart Jellyfin.

---

## License

Distributed under the **MIT** license.