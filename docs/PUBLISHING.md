# Publishing MachinScope

## Repository contents

Commit `.github/`, `src/`, `tests/`, `scripts/`, current `docs/`,
`THIRD-PARTY-NOTICES/`, solution/project configuration, package lock files, root
Markdown documents, LICENSE and RUNTIME-TERMS.txt. Keep the existing Core
identifiers. README.md becomes the GitHub repository homepage automatically.

Do not commit `.tools/`, bin/obj, local configuration, raw inventories/test reports,
private development archives, logs or portable/source ZIPs. `.gitignore` excludes
local/generated outputs. Keep historical development records outside the public source tree.

## Local review and commit

Use `codex/public-beta` for the publication candidate. It includes the public
source and the process-test CI fix. Origin is
https://github.com/AitLilCat/MachinScope.git. Preserve the existing history; do not
create a replacement repository or force push.

```powershell
git status --short --branch
python scripts/audit-source.py
python scripts/check-repository.py
.\scripts\build.ps1
.\scripts\test.ps1
git diff --stat origin/main
git diff --check origin/main
```

Review the source and local commit before authorizing any remote write. Confirm
icon/artwork provenance, runtime redistribution obligations and the applicable
[validation gates](VALIDATION.md). Legitimate signing remains optional; disclose
unsigned artifacts accurately. Keep reviewed screenshots free of private data.

## Only after explicit owner approval

```powershell
git fetch origin
git merge origin/main
git push -u origin codex/public-beta
gh pr create --base main --head codex/public-beta --title "Finalize MachinScope beta documentation and CI" --body-file docs/RELEASE_NOTES.md
```

Resolve/test any remote changes locally. Review/merge through the repository's
normal process. Run these commands only after the owner approves the push and pull request.

## GitHub Releases

Publish `MachinScope-v0.1.0-beta-win-x64.zip` and its `SHA256SUMS.txt`, using
[RELEASE_NOTES.md](RELEASE_NOTES.md). The ZIP includes EXE, launch instructions,
project license and separately licensed runtime notices/terms. Normal source
downloads use GitHub's generated archives; the optional local source ZIP adds
per-file integrity checks for private/offline review and is not a default asset.

Configure required reviewers for the `public-beta` environment, verify Actions
permissions and enable private vulnerability reporting where available. Dispatch
the manual release workflow at the reviewed revision with `publish=false` first.
Inspect artifacts/checksums. Only after explicit owner authorization, dispatch
with `publish=true` to create the prerelease. There is no automatic push/tag
publication trigger. Hosted workflow execution requires its own validation.

For documentation or test-only changes, use the preserved-candidate mode in
[BUILDING.md](../BUILDING.md). Keep the verified EXE and ZIP unchanged. Production
changes require a new build and artifact acceptance.
