#!/usr/bin/env python3
"""Build a Windows portable release. Python and .NET SDK are build tools only."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGING = ROOT / "packaging" / "windows"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def collect_licenses(bundle):
    assets = json.loads((ROOT / "TestBuilder/obj/project.assets.json").read_text())
    deps = json.loads((bundle / "TestBuilder.deps.json").read_text())
    folders = [Path(p) for p in assets["packageFolders"]]
    records = []
    for name, library in sorted(deps["libraries"].items()):
        if library["type"] not in ("package", "runtimepack"):
            continue
        package_id, version = name.split("/", 1)
        cache_id = package_id.removeprefix("runtimepack.")
        relative = assets.get("libraries", {}).get(name, {}).get("path", f"{cache_id}/{version}".lower())
        package = next((p / relative for p in folders if (p / relative).is_dir()), None)
        if package is None:
            raise RuntimeError(f"Package metadata is missing: {name}")
        target = bundle / "licenses" / cache_id / version
        target.mkdir(parents=True)
        nuspec = next(package.glob("*.nuspec"))
        shutil.copy2(nuspec, target / nuspec.name)
        root = ET.parse(nuspec).getroot()
        license_node = next((n for n in root.iter() if n.tag.split('}')[-1] == "license"), None)
        license_url = next((n.text for n in root.iter() if n.tag.split('}')[-1] == "licenseUrl"), None)
        notices = []
        for file in sorted(package.rglob("*")):
            if not file.is_file():
                continue
            if not any(key in file.name.lower() for key in ("license", "copying", "notice", "copyright")):
                continue
            destination = target / file.relative_to(package)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, destination)
            notices.append(destination.relative_to(bundle).as_posix())
        records.append({"package": package_id, "version": version,
                        "license": None if license_node is None else license_node.text,
                        "licenseUrl": license_url, "noticeFiles": notices})
    write_json(bundle / "licenses/nuget-packages.json", records)
    shutil.copy2(ROOT / "TestBuilder/Assets/Station/LICENSE", bundle / "licenses/Station-assets-LICENSE.txt")
    shutil.copytree(PACKAGING / "licenses", bundle / "licenses/upstream")
    return records


def firmware_references(profiles):
    references = []
    def visit(value, profile):
        if isinstance(value, dict):
            for key, child in value.items():
                if key == "firmwarePath" and child:
                    references.append({"profile": profile, "path": child, "included": False})
                visit(child, profile)
        elif isinstance(value, list):
            for child in value:
                visit(child, profile)
    for file in profiles:
        visit(json.loads(file.read_text(encoding="utf-8-sig")), file.name)
    return references


def validate_bundle(bundle, profiles):
    required = ["TestBuilder.exe", "TestBuilder.dll", "TestBuilder.deps.json", "coreclr.dll",
                "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "System.IO.Ports.dll",
                "Avalonia.Win32.dll", "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "SharpPcap.dll",
                "PacketDotNet.dll", "NModbus4.dll", "WinSCP/WinSCP.exe", "WinSCP/WinSCP.com",
                "WinSCP/license.txt", "arpd.bat", "set_mac_pro.bat", "Browser/chrome-win64/chrome.exe"]
    for path in required:
        if not (bundle / path).is_file():
            raise RuntimeError(f"Required file missing: {path}")
    config = json.loads((bundle / "TestBuilder.runtimeconfig.json").read_text())["runtimeOptions"]
    if "framework" in config or "frameworks" in config or not config.get("includedFrameworks"):
        raise RuntimeError("The publish output is not self-contained")
    actual = sorted(p.name for p in (bundle / "profiles").glob("*.json"))
    if actual != sorted(p.name for p in profiles):
        raise RuntimeError("Profile inventory does not match the repository")
    for profile in profiles:
        if sha256(profile) != sha256(bundle / "profiles" / profile.name):
            raise RuntimeError(f"Profile content changed: {profile.name}")
    # Check the apphost architecture without requiring Windows on the build host.
    binary = (bundle / "TestBuilder.exe").read_bytes()
    pe_offset = int.from_bytes(binary[60:64], "little")
    if binary[:2] != b"MZ" or binary[pe_offset:pe_offset + 6] != b"PE\0\0\x64\x86":
        raise RuntimeError("TestBuilder.exe is not a Windows AMD64 executable")
    return config["includedFrameworks"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--browser-zip", type=Path, required=True, help="Downloaded official chrome-win64.zip")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    args = parser.parse_args()
    browser = json.loads((PACKAGING / "browser.json").read_text())
    if sha256(args.browser_zip) != browser["sha256"]:
        raise RuntimeError("Browser archive SHA-256 does not match packaging/windows/browser.json")
    project = ET.parse(ROOT / "TestBuilder/TestBuilder.csproj").getroot()
    version = project.findtext("PropertyGroup/Version")
    if not version:
        raise RuntimeError("Set the release version in TestBuilder.csproj")
    name = f"TFortisTestBuilder-{version}-win-x64"
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    final_directory, archive = output / name, output / (name + ".zip")
    if final_directory.exists() or archive.exists():
        raise RuntimeError(f"Release already exists; select a new output directory: {final_directory}")
    commit = git("rev-parse", "HEAD")
    dirty = bool(git("status", "--porcelain"))
    with tempfile.TemporaryDirectory(prefix="release-build-", dir=output) as temporary:
        bundle = Path(temporary) / name
        subprocess.run(["dotnet", "publish", "TestBuilder/TestBuilder.csproj", "-c", "Release",
                        "-r", "win-x64", "--self-contained", "true", "-p:PublishTrimmed=false",
                        "-p:DebugSymbols=false", "-p:DebugType=None", "-o", str(bundle)], cwd=ROOT, check=True)
        browser_root = bundle / "Browser"
        browser_root.mkdir()
        with zipfile.ZipFile(args.browser_zip) as source:
            for item in source.infolist():
                destination = (browser_root / item.filename).resolve()
                if not destination.is_relative_to(browser_root.resolve()) or not item.filename.startswith("chrome-win64/"):
                    raise RuntimeError(f"Unexpected browser archive entry: {item.filename}")
            source.extractall(browser_root)
        for filename in ("Start-TestBuilder.cmd", "Check-Installation.cmd", "Check-Installation.ps1", "README-FIRST.txt"):
            encoding = "utf-8-sig" if filename.endswith(".ps1") else "utf-8"
            text = (PACKAGING / filename).read_text(encoding="utf-8-sig")
            (bundle / filename).write_bytes(text.replace("\r\n", "\n").replace("\n", "\r\n").encode(encoding))
        # Ship tracked documentation only, never local logs, credentials or build scratch files.
        for relative in git("ls-files", "-z", "docs").split("\0"):
            if not relative:
                continue
            source = ROOT / relative
            if source.is_file():
                destination = bundle / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
        write_json(bundle / "testbuilder.settings", {
            "graphsFolder": "", "serverBaseUrl": "https://iccid.fort-telecom.ru", "standId": "123",
            "theme": "Light", "engineerMode": False, "enableFileLogging": True,
            "logFolder": "", "dataTestAdapterIds": {}})
        profiles = sorted((ROOT / "profiles").glob("*.json"))
        frameworks = validate_bundle(bundle, profiles)
        packages = collect_licenses(bundle)
        manifest = {"version": version, "runtime": "win-x64", "configuration": "Release",
                    "selfContained": True, "frameworks": frameworks,
                    "sourceCommit": commit, "sourceDirty": dirty,
                    "builtAtUtc": datetime.now(timezone.utc).isoformat(), "browser": browser,
                    "profiles": [p.name for p in profiles], "nugetPackageCount": len(packages),
                    "externalFirmware": firmware_references(profiles),
                    "externalRequirements": ["Npcap driver", "USB/COM adapter driver if needed",
                        "Configured printer queue", "Bench network configuration", "Launcher session/server access"],
                    "windowsExecutionVerified": False,
                    "files": [{"path": p.relative_to(bundle).as_posix(), "size": p.stat().st_size,
                               "sha256": sha256(p)} for p in sorted(bundle.rglob("*")) if p.is_file()]}
        write_json(bundle / "release-manifest.json", manifest)
        temporary_zip = Path(temporary) / archive.name
        with zipfile.ZipFile(temporary_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
            for path in sorted(bundle.rglob("*")):
                if path.is_file():
                    zipped.write(path, arcname=f"{name}/{path.relative_to(bundle).as_posix()}")
        with zipfile.ZipFile(temporary_zip) as zipped:
            if zipped.testzip() is not None:
                raise RuntimeError("Archive CRC verification failed")
        shutil.move(str(bundle), str(final_directory))
        shutil.move(str(temporary_zip), str(archive))
    checksum = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(f"{checksum}  {archive.name}\n", encoding="ascii")
    print(f"Release: {archive}\nSHA-256: {checksum}\nProfiles: {len(profiles)}; packages: {len(packages)}")


if __name__ == "__main__":
    main()
