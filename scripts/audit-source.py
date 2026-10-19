"""Conservative source-distribution secret/path checks, without echoing matched values."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
EXCLUDE = {".git", ".tools", "artifacts", "bin", "obj", "TestResults", "__pycache__", "THIRD-PARTY-NOTICES"}
PATTERNS = {
    "private key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "GitHub token": re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,})\b"),
    "AWS access key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "personal Windows path": re.compile(r"[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s%<>]+", re.I),
    "assigned credential": re.compile(r'''(?:password|api[_-]?key|client[_-]?secret)\s*[:=]\s*["'][^"'\s]{8,}["']''', re.I),
}


def main():
    findings = []
    count = 0
    for path in ROOT.rglob("*"):
        relative = path.relative_to(ROOT)
        if not path.is_file() or EXCLUDE.intersection(relative.parts):
            continue
        if path.suffix.lower() in {".pfx", ".p12", ".key", ".pem", ".dmp"} or path.name.startswith(".env"):
            findings.append((relative, "sensitive file type"))
        if path.suffix.lower() == ".ico":
            continue
        try:
            content = path.read_text(encoding="utf-8-sig")
        except UnicodeError:
            findings.append((relative, "unexpected binary"))
            continue
        count += 1
        for name, pattern in PATTERNS.items():
            if pattern.search(content):
                findings.append((relative, name))
    for path, kind in findings:
        print(f"{path.as_posix()}: {kind}")
    print(f"Scanned {count} text source/configuration/documentation files; {len(findings)} findings")
    print("Pattern scan only; not proof of absence of secrets or an exhaustive security audit.")
    return bool(findings)


if __name__ == "__main__":
    sys.exit(main())
