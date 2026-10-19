"""Check maintained Markdown links, required source inputs and license references."""
from pathlib import Path
from urllib.parse import unquote, urlsplit
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def main():
    failures = []
    required = ["README.md", "BUILDING.md", "CONTRIBUTING.md", "SECURITY.md", "ROADMAP.md", "CHANGELOG.md", "LICENSE", "RUNTIME-TERMS.txt", "README.release.md", "CORE.slnx", "Directory.Build.props", "global.json", ".gitignore", ".github/workflows/ci.yml", ".github/workflows/release.yml"]
    required += [f"docs/{name}.md" for name in ("ARCHITECTURE", "FEATURES", "WINDOWS_APIS", "PLATFORMS", "PERFORMANCE", "VALIDATION", "PUBLISHING", "RELEASE_NOTES")]
    for name in required:
        if not (ROOT / name).is_file():
            failures.append(f"Missing required file: {name}")
    solution = ET.parse(ROOT / "CORE.slnx")
    for element in solution.iter("Project"):
        project = ROOT / element.attrib["Path"]
        if not project.is_file() or not (project.parent / "packages.lock.json").is_file():
            failures.append(f"Missing project or normal dependency lock: {element.attrib['Path']}")
    for project in (ROOT / "src").glob("*/*.csproj"):
        if not (project.parent / "packages.portable.lock.json").is_file():
            failures.append(f"Missing portable lock: {project.parent.name}")
    documents = list(ROOT.glob("*.md")) + list((ROOT / "docs").glob("*.md")) + list((ROOT / ".github").rglob("*.md"))
    links = 0
    for document in documents:
        content = document.read_text(encoding="utf-8-sig")
        content = re.sub(r"```.*?```", "", content, flags=re.S)
        for destination in re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", content):
            destination = destination.strip().strip("<>")
            parsed = urlsplit(destination)
            if parsed.scheme or destination.startswith("//"):
                continue
            target = (document.parent / unquote(parsed.path)).resolve() if parsed.path else document
            links += 1
            if not target.is_relative_to(ROOT) or not target.exists():
                failures.append(f"{document.relative_to(ROOT)}: broken local link {destination}")
            elif parsed.fragment and target.suffix.lower() == ".md":
                headings = []
                for heading in re.findall(r"^#{1,6}\s+(.+)$", target.read_text(encoding="utf-8-sig"), flags=re.M):
                    headings.append(re.sub(r"[^\w\- ]", "", heading.lower()).replace(" ", "-"))
                if unquote(parsed.fragment) not in headings:
                    failures.append(f"{document.relative_to(ROOT)}: unknown heading {destination}")
    notices = ROOT / "THIRD-PARTY-NOTICES"
    for record in json.loads((notices / "packages.json").read_text(encoding="utf-8")):
        for relative in record["notices"]:
            path = (notices / relative).resolve()
            if not path.is_relative_to(notices) or not path.is_file():
                failures.append(f"Missing upstream notice: {relative}")
    for name in ("README.md", "COMMON-LICENSES.txt", "MIT.txt", "Apache-2.0.txt"):
        if not (notices / name).is_file():
            failures.append(f"Missing common notice: {name}")
    for failure in failures:
        print(failure)
    print(f"Checked {len(documents)} Markdown files, {links} local links, solution/locks and license references: {len(failures)} failures")
    return bool(failures)


if __name__ == "__main__":
    sys.exit(main())
