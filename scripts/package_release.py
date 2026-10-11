"""Prepare one local candidate archive and manifest; never publish or edit releases."""
import json
import os
from pathlib import Path
import shutil
import zipfile
import yaml
from update_manifest import update_manifest_entries


def main():
    root = Path(__file__).resolve().parents[1]
    os.chdir(root)
    meta = json.loads(Path("meta.json").read_text(encoding="utf-8"))
    build = yaml.safe_load(Path("JellyTrack.Plugin/build.yaml").read_text(encoding="utf-8"))
    if meta["version"] != build["version"] or meta["targetAbi"] != build["targetAbi"]:
        raise ValueError("Package metadata and build.yaml disagree")
    version = meta["version"]
    output = root / "artifacts"
    output.mkdir(exist_ok=True)
    files = {
        "meta.json": root / "meta.json",
        "Jellyfin.Plugin.JellyTrack.png": root / "assets/banner.png",
    }
    for extension in ("dll", "deps.json", "pdb"):
        name = f"Jellyfin.Plugin.JellyTrack.{extension}"
        files[name] = root / "publish" / name
    for source in files.values():
        if not source.is_file():
            raise FileNotFoundError(source)
    with zipfile.ZipFile(output / f"Jellyfin.Plugin.JellyTrack-{version}.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for name, source in files.items():
            archive.write(source, name)
    shutil.copyfile(root / "manifest.json", output / "manifest.json")
    os.chdir(output)
    update_manifest_entries(os.environ.get("REPO", "maelmoreau21/jellyfin-plugin-JellyTrack"), f"v{version}", build)
    print("Candidate archive and manifest prepared in artifacts/. Publication requires a separate manual action.")


if __name__ == "__main__":
    main()
