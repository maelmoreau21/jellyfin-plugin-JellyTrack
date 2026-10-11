"""Verify .NET-emitted fixtures using a read-only Go source overlay and temporary SQLite DBs."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    backend = Path(sys.argv[1]).resolve()
    fixture = Path(sys.argv[2]).resolve()
    root = Path(__file__).resolve().parents[1]
    if not fixture.is_file():
        raise FileNotFoundError(fixture)
    environment = dict(os.environ, JELLYTRACK_CONTRACT_OUTPUT=str(fixture))
    with tempfile.TemporaryDirectory(prefix="jellytrack-contract-") as temp:
        overlay = Path(temp) / "overlay.json"
        overlay.write_text(json.dumps({"Replace": {
            str(backend / "internal/plugin/dotnet_contract_test.go"): str(root / "scripts/go_contract_test.go.txt")
        }}), encoding="utf-8")
        result = subprocess.run(["go", "test", f"-overlay={overlay}", "./internal/plugin", "-run", "TestActualDotNetPluginContract", "-v", "-count=1"], cwd=backend, env=environment)
        sys.exit(result.returncode)


if __name__ == "__main__":
    main()
