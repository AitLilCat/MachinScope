"""Package the portable EXE and an explicit source allowlist; verify every byte."""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts"
VERSION = "0.1.0-beta"
EXCLUDED = {"bin", "obj", ".vs", "__pycache__", "TestResults"}
SOURCE_SUFFIXES = {".cs", ".xaml", ".csproj", ".props", ".targets", ".manifest", ".ico", ".md", ".json", ".ps1", ".py", ".yml", ".yaml", ".txt", ".nuspec"}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def package(name, files):
    files = sorted(files, key=lambda item: item[0])
    if len({name for name, _ in files}) != len(files):
        raise RuntimeError("Duplicate archive paths")
    manifest = {relative: digest(path) for relative, path in files}
    destination = ARTIFACTS / name
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for relative, path in files:
            archive.write(path, relative)
        archive.writestr("PACKAGE-MANIFEST.json", json.dumps({"package": name, "algorithm": "SHA-256", "files": manifest}, indent=2) + "\n")
    with zipfile.ZipFile(destination) as archive:
        if archive.testzip() is not None or len(archive.namelist()) != len(files) + 1:
            raise RuntimeError(f"Archive integrity failed: {destination}")
        for relative, expected in manifest.items():
            with archive.open(relative) as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
                    raise RuntimeError(f"Archived content mismatch: {relative}")
    print(f"{destination.name}: {len(files)} files, {destination.stat().st_size:,} bytes; CRC and SHA-256 verified")
    return destination


def verify_existing(destination, files):
    """Keep a previously verified distribution unchanged; reject stale companions."""
    expected = {name: digest(path) for name, path in files}
    with zipfile.ZipFile(destination) as archive:
        manifest = json.loads(archive.read("PACKAGE-MANIFEST.json"))
        if archive.testzip() is not None or manifest["files"] != expected:
            raise RuntimeError("Existing portable archive differs from the requested executable or companions")
        if set(archive.namelist()) != set(expected) | {"PACKAGE-MANIFEST.json"} or len(archive.namelist()) != len(expected) + 1:
            raise RuntimeError("Unexpected portable archive entries")
        for name, checksum in expected.items():
            with archive.open(name) as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest() != checksum:
                    raise RuntimeError(f"Existing archive content mismatch: {name}")
    print(f"{destination.name}: existing archive verified and preserved byte-for-byte")
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable-exe", type=Path, default=ARTIFACTS / "MachinScope-single-exe/MachinScope.exe")
    parser.add_argument("--include-source", action="store_true", help="Optional local source ZIP with per-file integrity manifest; GitHub source downloads normally suffice")
    parser.add_argument("--preserve-portable-archive", action="store_true", help="Verify the existing Windows ZIP without rewriting it")
    args = parser.parse_args()
    portable = args.portable_exe.resolve()
    if not portable.is_file() or portable.name != "MachinScope.exe":
        raise RuntimeError("Run scripts/publish-portable.ps1 first; MachinScope.exe is required")
    notices = ROOT / "THIRD-PARTY-NOTICES"
    if not (notices / "packages.json").is_file():
        raise RuntimeError("Generate dependency notices first")
    runtime_files = [("MachinScope.exe", portable), ("LICENSE", ROOT / "LICENSE"), ("RUNTIME-TERMS.txt", ROOT / "RUNTIME-TERMS.txt"), ("README.md", ROOT / "README.release.md")]
    runtime_files += [(p.relative_to(ROOT).as_posix(), p) for p in notices.rglob("*") if p.is_file()]
    ARTIFACTS.mkdir(exist_ok=True)
    binary_name = f"MachinScope-v{VERSION}-win-x64.zip"
    binary = verify_existing(ARTIFACTS / binary_name, runtime_files) if args.preserve_portable_archive else package(binary_name, runtime_files)
    outputs = [binary]
    if args.include_source:
        source_files = [(name, ROOT / name) for name in [".gitignore", "CORE.slnx", "Directory.Build.props", "global.json", "LICENSE", "RUNTIME-TERMS.txt"]]
        source_files += [(p.name, p) for p in ROOT.glob("*.md")]
        for directory in ("src", "tests", "docs", "scripts", ".github", "THIRD-PARTY-NOTICES"):
            for path in (ROOT / directory).rglob("*"):
                relative = path.relative_to(ROOT)
                if path.is_file() and not EXCLUDED.intersection(relative.parts) and (path.suffix.lower() in SOURCE_SUFFIXES or directory == "THIRD-PARTY-NOTICES"):
                    source_files.append((relative.as_posix(), path))
        outputs.append(package(f"MachinScope-v{VERSION}-source.zip", source_files))
    manifest = ARTIFACTS / "SHA256SUMS.txt"
    manifest.write_text("".join(f"{digest(p)}  {p.name}\n" for p in outputs), encoding="ascii")
    for line in manifest.read_text().splitlines():
        expected, name = line.split("  ", 1)
        if digest(ARTIFACTS / name) != expected:
            raise RuntimeError(f"Distribution checksum mismatch: {name}")
    print("SHA256SUMS.txt verified")


if __name__ == "__main__":
    main()
