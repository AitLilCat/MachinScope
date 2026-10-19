# Contributing to MachinScope

Use Windows x64, SDK 10.0.401 and Windows SDK/WinUI tooling. Initial restore needs
NuGet access; Python 3.11+ is needed only for packaging. See BUILDING.md.

```powershell
dotnet restore CORE.slnx --locked-mode
dotnet build CORE.slnx -c Release --no-restore
.\scripts\test.ps1
```

Preserve the existing C#/WinUI architecture, compact UI, stable routes/settings
and explicit unavailable states. Keep changes focused. Dispose native handles,
respect cancellation and UI thread ownership, and avoid expensive inactive-page
polling. Use real OS evidence; never substitute guessed measurements.
Management changes need identity validation, least privilege, explicit target
review and realistic recovery behavior. Add focused regression coverage when
behavior changes. Do not add tests solely to increase counts.

Explain the problem, resulting behavior, tests and remaining manual checks in a
pull request. Include dependency/license/privacy effects. Do not commit generated
outputs, raw inventories, credentials or user paths. Review screenshots for private
information. Report ordinary issues with app/Windows versions and reproducible
steps. Vulnerabilities follow SECURITY.md; keep exploit details out of public issues.

Project-owned contributions are MIT licensed. Retain third-party attribution.
Publishing requires explicit owner approval.

## Development provenance

Developed and maintained by AitLilCat. AI assistance has been used for development,
review, cleanup and documentation. AitLilCat remains responsible for project
direction, testing, maintenance and releases. Preserve contributor provenance and
third-party credits.
