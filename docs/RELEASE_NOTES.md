# MachinScope v0.1.0-beta

An early Windows x64 beta for inspecting processes, hardware, devices, storage,
files, networking and system configuration.

See deeper. Understand more. Take control.

## Included

- Process relationships, resource counters and properties.
- Disks, partitions, volumes, storage mappings and available provider health data.
- Present USB/PnP devices, driver properties and session observations.
- TCP/UDP endpoints, supported process associations and adapter traffic counters.
- File browsing, hashes, duplicate analysis and reviewed individual-file operations.
- Startup inventory, current-user Run recovery controls and read-only services.
- Native Windows administration tools, table filters, sorting and CSV/JSON export.

## Download and launch

After publication, download `MachinScope-v0.1.0-beta-win-x64.zip` and
`SHA256SUMS.txt` from [GitHub Releases](https://github.com/AitLilCat/MachinScope/releases).
The binary release has not been published yet.

```powershell
Get-FileHash .\MachinScope-v0.1.0-beta-win-x64.zip -Algorithm SHA256
```

Compare the hash with the manifest, extract the complete archive and run
`MachinScope.exe` as a normal user. .NET and Windows App SDK are embedded and
extract on first launch. No separate runtime installation is required.
Settings and recovery backups retain `%LOCALAPPDATA%\CORE` for compatibility.

The application targets Windows 10 build 19041 or later. Recorded validation uses
Windows 11 x64; other supported Windows versions and clean-machine launches need
acceptance testing. The executable is unsigned. Checksums verify integrity, not
publisher identity. Follow your device's security policies.

## Limitations

Protected fields and counters may be unavailable. Storage-provider health is not
decoded SMART/NVMe diagnostics. Device relationships do not prove unused physical
ports or complete non-present device inventory. Session observations can miss
changes between samples. Packet capture, reliable initiator direction,
per-process bandwidth and connection byte counts are unavailable.

Services, disks and devices are read-only in the app. Native utilities apply their
own authorization. ISO, imaging and Linux support are future work. See the
[feature reference](FEATURES.md) and [validation checklist](VALIDATION.md).

## Source and license

Build instructions are in [BUILDING.md](../BUILDING.md). Locked dependencies and
deterministic compilation support reproducible builds; byte-identical archives
across different hosts are not promised.

Project-owned code is [MIT licensed](../LICENSE). Bundled components retain their
[notices](../THIRD-PARTY-NOTICES/README.md) and [runtime terms](../RUNTIME-TERMS.txt).

Developed and maintained by AitLilCat.
