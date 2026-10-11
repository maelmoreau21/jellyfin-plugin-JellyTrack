import json
import os
from pathlib import Path
import tempfile
import unittest
from update_manifest import update_manifest_entries, get_md5


class ManifestTests(unittest.TestCase):
    def test_only_one_net10_package_and_old_abis_removed(self):
        before = os.getcwd()
        with tempfile.TemporaryDirectory() as directory:
            try:
                os.chdir(directory)
                Path("Jellyfin.Plugin.JellyTrack-2.1.0.0.zip").write_bytes(b"candidate")
                Path("Jellyfin.Plugin.JellyTrack-2.1.0.0-jellyfin11.zip").write_bytes(b"legacy")
                Path("manifest.json").write_text(json.dumps([{"guid": "plugin", "versions": [
                    {"version": "old", "targetAbi": "10.11.0.0"},
                    {"version": "old12", "targetAbi": "12.0.0.0"}]}]))
                build = {"guid": "plugin", "framework": "net10.0", "targetAbi": "12.2.0.0"}
                update_manifest_entries("owner/repo", "v2.1.0.0", build)
                update_manifest_entries("owner/repo", "v2.1.0.0", build)
                versions = json.loads(Path("manifest.json").read_text())[0]["versions"]
                self.assertEqual(len(versions), 2)
                self.assertEqual(versions[0]["targetAbi"], "12.2.0.0")
                self.assertEqual(versions[0]["checksum"], get_md5("Jellyfin.Plugin.JellyTrack-2.1.0.0.zip"))
                self.assertNotIn("jellyfin11", versions[0]["sourceUrl"])
            finally:
                os.chdir(before)

    def test_rejects_legacy_framework_and_abi(self):
        for framework, abi in [("net9.0", "12.0.0.0"), ("net10.0", "10.11.0.0")]:
            with self.assertRaises(ValueError):
                update_manifest_entries("owner/repo", "v2.1.0.0", {"framework": framework, "targetAbi": abi})

    def test_missing_archive_does_not_change_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            before = os.getcwd()
            try:
                os.chdir(directory)
                Path("manifest.json").write_text("[]")
                with self.assertRaises(FileNotFoundError):
                    update_manifest_entries("owner/repo", "v2.1.0.0", {"framework": "net10.0", "targetAbi": "12.2.0.0"})
                self.assertEqual(Path("manifest.json").read_text(), "[]")
            finally:
                os.chdir(before)


if __name__ == "__main__":
    unittest.main()
