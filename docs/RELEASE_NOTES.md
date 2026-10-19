# MachinScope v0.1.0-beta

An early public beta candidate of a free, open-source Windows system inspector.
See deeper. Understand more. Take control.

Inspect processes and resource counters; hardware, disks/volumes and provider
health; files/duplicates; present USB/PnP devices and session history; TCP/UDP
endpoints and adapter traffic; startup, services, Windows configuration and native
administration entry points. Filter/sort tables, inspect properties and export
selected data. Existing guarded process/file/current-user startup actions remain.

Windows x64 only. Targets Windows 10 build 19041+; validation uses Windows 11.
The portable ZIP embeds .NET and Windows App SDK; first launch extracts runtimes.
Extract all files and run MachinScope.exe. Run as a normal user; unavailable
metadata remains unavailable. Native utilities may request their own elevation.

After publication, obtain MachinScope-v0.1.0-beta-win-x64.zip and SHA256SUMS.txt
from this repository's Releases. No public release is claimed to exist yet.
Use `Get-FileHash .\MachinScope-v0.1.0-beta-win-x64.zip -Algorithm SHA256`
and compare its hash to the manifest before extraction. The manifest is an
integrity check, not a digital signature.

Known limits: provider health is not decoded SMART; device relationships do not
prove physical ports or complete remembered-device inventory; session events can
miss intermediate changes; packet capture, initiator direction, per-process
bandwidth and connection byte counts are unavailable. Service/disk/device mutation,
ISO/imaging and Linux are future work. See [the validation checklist](VALIDATION.md) for UI and clean-machine acceptance. Settings/recovery remain at %LOCALAPPDATA%\CORE.

The executable is unsigned. Publisher metadata and SmartScreen trust are distinct.
Do not bypass Windows security controls. Build commands and pinned toolchain are
in BUILDING.md; deterministic compilation does not promise byte-identical signed,
timestamped or ZIP outputs across machines. MIT applies to project-owned code;
bundled components retain their licenses in THIRD-PARTY-NOTICES.

Developed and maintained by AitLilCat, with AI-assisted development, review,
cleanup and documentation.
