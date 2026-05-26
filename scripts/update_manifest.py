import os
import sys
import yaml
import json
import hashlib
import urllib.request
import urllib.error

def get_md5(file_path):
    hash_md5 = hashlib.md5()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(4096), b""):
            hash_md5.update(chunk)
    return hash_md5.hexdigest()

def main():
    repo = os.environ.get("REPO")
    tag = os.environ.get("RELEASE_TAG")
    
    if not repo or not tag:
        print("Missing REPO or RELEASE_TAG environment variables")
        sys.exit(1)

    build_yaml_path = "JellyTrack.Plugin/build.yaml"
    zip_name = f"Jellyfin.Plugin.JellyTrack-{tag.lstrip('v')}.zip"
    zip_path = zip_name
    
    manifest_path = "manifest.json"

    with open(build_yaml_path, "r", encoding="utf-8") as f:
        build_info = yaml.safe_load(f)

    checksum = get_md5(zip_path)
    target_abi = build_info.get("targetAbi", "10.10.0.0")
    
    version_info = {
        "version": tag.lstrip('v'),
        "changelog": build_info.get("changelog", ""),
        "targetAbi": target_abi,
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zip_name}",
        "checksum": checksum,
        "timestamp": ""
    }

    import datetime
    version_info["timestamp"] = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')

    manifest = []
    
    # Try to load existing manifest from the local file
    print(f"Attempting to load existing manifest from {manifest_path}...")
    try:
        if os.path.exists(manifest_path):
            with open(manifest_path, "r", encoding="utf-8") as f:
                manifest = json.load(f)
            print("Successfully loaded existing manifest.")
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

    versions = plugin_entry.get("versions", [])
    version_exists = False
    for i, v in enumerate(versions):
        if v.get("version") == version_info["version"]:
            versions[i] = version_info
            version_exists = True
            break
    
    if not version_exists:
        versions.insert(0, version_info)
    
    plugin_entry["versions"] = versions

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)
        
    print(f"Manifest written to {manifest_path} with version {version_info['version']}")

if __name__ == "__main__":
    main()
