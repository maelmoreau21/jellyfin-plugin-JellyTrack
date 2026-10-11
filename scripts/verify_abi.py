"""Check the exact candidate DLL against stable 12.0/12.1/12.2 assemblies, not a Jellyfin server."""
import json
from pathlib import Path
import subprocess
import tempfile
import sys
from xml.sax.saxutils import escape


def main():
    root = Path(__file__).resolve().parents[1]
    dll = root / "publish/Jellyfin.Plugin.JellyTrack.dll"
    if not dll.is_file():
        raise FileNotFoundError(dll)
    results = []
    with tempfile.TemporaryDirectory(prefix="jellytrack-abi-") as directory:
        for version in ("12.0.0", "12.1.0", "12.2.0"):
            case = Path(directory) / version
            case.mkdir()
            (case / "Check.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
<ItemGroup><Reference Include="Jellyfin.Plugin.JellyTrack"><HintPath>{escape(str(dll))}</HintPath></Reference>
<PackageReference Include="Jellyfin.Controller" Version="{version}" />
<PackageReference Include="Jellyfin.Model" Version="{version}" /></ItemGroup></Project>""", encoding="utf-8")
            (case / "Check.cs").write_text("""using System;
using JellyTrack.Plugin;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
public static class Check {
    public static int Main() {
        try { Register(); return 0; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }
    private static void Register() {
        Console.WriteLine(typeof(IServerApplicationHost).Assembly.FullName);
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null);
        Console.WriteLine("Registered " + services.Count + " services using exact candidate DLL.");
    }
}""", encoding="utf-8")
            run = subprocess.run(["dotnet", "run", "--project", str(case / "Check.csproj"), "-c", "Release"], capture_output=True, text=True)
            result = {"version": version, "exitCode": run.returncode, "output": run.stdout + run.stderr}
            results.append(result)
            print(json.dumps(result, ensure_ascii=False))
    output = root / "artifacts/abi-checks.json"
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
    expected = results[0]["exitCode"] != 0 and results[1]["exitCode"] != 0 and results[2]["exitCode"] == 0
    expected = expected and all("MediaBrowser.Controller, Version=12.2.0.0" in r["output"] for r in results[:2])
    sys.exit(0 if expected else 1)


if __name__ == "__main__":
    main()
