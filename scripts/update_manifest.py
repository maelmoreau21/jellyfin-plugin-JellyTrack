import os
import sys
import yaml
import json
import hashlib
import datetime

def get_md5(file_path):
    hash_md5 = hashlib.md5()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(4096), b""):
            hash_md5.update(chunk)
    return hash_md5.hexdigest()

def update_manifest_entries(repo, tag, build_info, manifest_path="manifest.json"):
    version = tag.lstrip('v')
    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    changelog = build_info.get("changelog", "")

    # Target configurations to publish
    targets = [
        {
            "targetAbi": "12.0.0.0",
            "zip_name": f"Jellyfin.Plugin.JellyTrack-{version}.zip",
        },
        {
            "targetAbi": "10.11.0.0",
            "zip_name": f"Jellyfin.Plugin.JellyTrack-{version}-jellyfin11.zip",
        }
    ]

    manifest = []
    if os.path.exists(manifest_path):
        try:
            with open(manifest_path, "r", encoding="utf-8") as f:
                manifest = json.load(f)
        except Exception as e:
            print(f"Could not load existing manifest: {e}")

    if isinstance(manifest, dict):
        manifest = [manifest]

    plugin_entry = None
    for plugin in manifest:
        if plugin.get("guid") == build_info.get("guid"):
            plugin_entry = plugin
            break

    if not plugin_entry:
        plugin_entry = {
            "guid": build_info.get("guid"),
            "name": build_info.get("name"),
            "description": build_info.get("description"),
            "overview": build_info.get("overview"),
            "owner": build_info.get("owner"),
            "category": build_info.get("category"),
            "imageUrl": f"https://raw.githubusercontent.com/{repo}/main/assets/banner.png",
            "versions": []
        }
        manifest.append(plugin_entry)

    plugin_entry["imageUrl"] = f"https://raw.githubusercontent.com/{repo}/main/assets/banner.png"
    existing_versions = plugin_entry.get("versions", [])

    new_version_entries = []
    for target in targets:
        zip_path = target["zip_name"]
        if os.path.exists(zip_path):
            checksum = get_md5(zip_path)
            entry = {
                "version": version,
                "changelog": changelog,
                "targetAbi": target["targetAbi"],
                "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{target['zip_name']}",
                "checksum": checksum,
                "timestamp": timestamp
            }
            new_version_entries.append(entry)
        else:
            print(f"Warning: Zip file {zip_path} not found, skipping targetAbi {target['targetAbi']}")

    # Remove existing entries with same version and targetAbi
    filtered_versions = []
    for v in existing_versions:
        match = any(
            v.get("version") == n["version"] and v.get("targetAbi") == n["targetAbi"]
            for n in new_version_entries
        )
        if not match:
            filtered_versions.append(v)

    # Prepend new version entries (Jellyfin 12 first, then Jellyfin 10.11)
    for n in reversed(new_version_entries):
        filtered_versions.insert(0, n)

    plugin_entry["versions"] = filtered_versions

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)
        
    print(f"Manifest written to {manifest_path} with {len(new_version_entries)} entries for version {version}")

def main():
    repo = os.environ.get("REPO")
    tag = os.environ.get("RELEASE_TAG")
    
    if not repo or not tag:
        print("Missing REPO or RELEASE_TAG environment variables")
        sys.exit(1)

    build_yaml_path = "JellyTrack.Plugin/build.yaml"
    with open(build_yaml_path, "r", encoding="utf-8") as f:
        build_info = yaml.safe_load(f)

    update_manifest_entries(repo, tag, build_info)

if __name__ == "__main__":
    main()
