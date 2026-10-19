"""Generate notices from restored NuGet packages and the self-contained runtime packs."""
import json
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "THIRD-PARTY-NOTICES"


def main():
    DEST.mkdir(exist_ok=True)
    packages = {}
    folders = set()
    for assets in list((ROOT / "src").glob("*/obj/project.assets.json")) + list((ROOT / "tests").glob("*/obj/project.assets.json")):
        data = json.loads(assets.read_text(encoding="utf-8-sig"))
        folders.update(data["packageFolders"])
        for identity, item in data["libraries"].items():
            if item["type"] == "package":
                packages[identity] = item["path"]
        # Runtime packs may be supplied through downloadDependencies, not libraries.
        for framework in data["project"]["frameworks"].values():
            for dependency in framework.get("downloadDependencies", []):
                name = dependency["name"].lower()
                version = dependency["version"].strip("[]").split(",")[0].strip()
                if ".runtime.win-x64" in name:
                    packages[f"{name}/{version}"] = f"{name}/{version}"
    records = []
    for identity, relative in sorted(packages.items()):
        package = next((Path(f) / relative for f in folders if (Path(f) / relative).is_dir()), None)
        if package is None:
            raise RuntimeError(f"Missing restored package: {identity}")
        spec = next(package.glob("*.nuspec"))
        metadata = ET.parse(spec).getroot()
        def field(name):
            return next((n.text or "" for n in metadata.iter() if n.tag.split("}")[-1] == name), "")
        license_element = next((n for n in metadata.iter() if n.tag.split("}")[-1] == "license"), None)
        license_value = field("license")
        candidates = [p for p in package.rglob("*") if p.is_file() and (p.name.lower().replace("-", "").startswith(("license", "notice", "thirdpartynotice")) or p.name.lower() == "copyright")]
        if license_element is not None and license_element.get("type") == "file":
            candidates.append(package / license_value)
        target = DEST / identity.replace("/", "-")
        target.mkdir(exist_ok=True)
        copied = []
        for source in sorted(set(candidates)):
            if not source.is_file():
                raise RuntimeError(f"Missing license: {source}")
            name = source.relative_to(package).as_posix().replace("/", "__")
            shutil.copyfile(source, target / name)
            copied.append(f"{target.name}/{name}")
        # NuGet metadata contains component authorship and expression/file license declarations.
        shutil.copyfile(spec, target / spec.name)
        records.append({"package": identity, "authors": field("authors"), "copyright": field("copyright"), "license": license_value, "licenseUrl": field("licenseUrl"), "notices": copied})
    (DEST / "packages.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
    (DEST / "README.md").write_text("# Third-party notices\n\nGenerated from restored NuGet metadata. Includes build/test components as well as\nruntime components; not every listed package is shipped in the executable.\nComponent licenses apply independently of the project's MIT license.\nNuGet specifications retain authorship, copyright and license declarations.\nBundled license/notice texts are copied without alteration. packages.json indexes them.\n\nExpression-only MIT/Apache licenses are reproduced in COMMON-LICENSES.txt.\nMicrosoft redistributable terms remain in their package license files.\n", encoding="utf-8")
    print(f"Collected metadata and available notices for {len(records)} packages")


if __name__ == "__main__":
    main()
