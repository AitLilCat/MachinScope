# MachinScope

**Deep System Inspection & Control**

*See deeper. Understand more. Take control.*

MachinScope is a free, open-source Windows system inspection and administration utility. It brings processes, hardware, devices, storage, networking and system tools together in one application.

Use compact technical tables to inspect system activity, investigate problems and open native Windows tools.

**No accounts. No subscriptions. No mandatory internet connection.**

Core inspection works offline. Optional network diagnostics contact the host you request and use your configured DNS resolvers.

## Current beta

**v0.1.0-beta** is the first Windows x64 release candidate. The source is public; the binary release is awaiting publication. Available information depends on Windows APIs, hardware, providers and permissions.

## What can MachinScope do?

### Processes & Performance

Explore running processes, their relationships, resource usage, executable information, and available properties. Monitor CPU, memory and supported performance counters, view bounded history and investigate system activity.

### Hardware & Storage

Inspect physical drives, partitions, volumes, storage relationships, and available hardware information. Understand how your system's storage is organized.

### USB & Connected Devices

Explore USB controllers, connected USB and PnP devices, device relationships, driver information, and available hardware properties. Observe supported connection and disconnection events during the current session.

### Network Inspection

Inspect network adapters, active TCP connections, UDP endpoints, connection states, associated processes where available, and adapter traffic statistics.

### Files & Startup

Browse files, inspect properties, calculate hashes, analyze duplicates, and perform supported file operations. Review startup entries and read-only Windows service information. Current-user Run startup controls retain recovery backups.

### Windows Administration

Access supported native Windows configuration, diagnostics, and administrative tools from one organized interface. Native tools apply their own Windows permissions.

Inspection tables support filtering, sorting, properties, column layouts, clipboard operations and CSV/JSON export. See the [feature reference](docs/FEATURES.md) for capabilities and boundaries.

## Screenshots

Screenshots will be added after review for private system information.

## Download

The portable Windows x64 build will be available from [GitHub Releases](https://github.com/AitLilCat/MachinScope/releases) after publication.

The current beta is unsigned. Windows may display a publisher or reputation warning. Verify releases using the supplied SHA-256 checksums and follow your device's security policies.

## Installation and first launch

1. Download `MachinScope-v0.1.0-beta-win-x64.zip` and `SHA256SUMS.txt` from the same release.
2. Run `Get-FileHash .\MachinScope-v0.1.0-beta-win-x64.zip -Algorithm SHA256` and compare the result with the manifest.
3. Extract the complete archive and run `MachinScope.exe` as a normal user. Embedded runtime files extract on first launch.
4. Review unavailable fields and permission messages. Confirm exact targets before using supported management actions.

No installer or account is required. Settings, logs and startup recovery backups retain the compatible `%LOCALAPPDATA%\CORE` location. Read the bundled runtime terms before use or redistribution.

## System requirements

- Windows x64. The application targets Windows 10 build 19041 or later; documented automated and interactive validation uses Windows 11 x64.
- Writable temporary and local application-data storage for runtime extraction and settings.
- No separate .NET or Windows App SDK installation is needed for the self-contained portable build.

Linux and ARM64 desktop releases are not available. See [platform support](docs/PLATFORMS.md).

## Known limitations

- Protected processes, hardware fields and performance counters may be inaccessible.
- Storage-provider health is not decoded SMART/NVMe diagnostics.
- Present-device inventory and session observations do not cover every previously installed device or prove unused physical USB ports.
- Traffic rates are adapter-level. Packet capture, reliable initiator direction, per-process bandwidth and connection byte counts are unavailable.
- Services, disks and devices are read-only within the application. Advanced imaging, ISO creation and bootable-media features are future work.
- Polling can miss intermediate events. File and registry source checks do not eliminate every concurrent-change race.

## Our Vision

**One application. Deeper visibility. More control.**

The goal is one free, open-source environment for understanding and managing Windows systems. The current beta focuses on inspection. Longer-term work may add disk diagnostics, storage management, ISO and bootable-media tools, virtualization utilities and deeper Windows administration.

### Development Roadmap

| Version | Focus |
| --- | --- |
| v0.1.0-beta | Initial Windows inspection and discovery |
| v0.2.0-beta | Stability, compatibility and additional diagnostics |
| v0.3.0-beta | Expanded functionality and community-requested improvements |
| v1.0.0 | Stable release and broader validation |

Advanced ISO, bootable media, virtualization and system management capabilities are longer-term goals, not promises for a particular release. See [ROADMAP.md](ROADMAP.md).

## Building from source

Use Windows x64 with .NET SDK 10.0.401 and Windows SDK/WinUI build tooling:

```powershell
dotnet restore CORE.slnx --locked-mode
dotnet build CORE.slnx -c Release --no-restore
.\scripts\test.ps1
```

Python 3.11+ is required for release packaging. See [BUILDING.md](BUILDING.md) for the complete toolchain and reproducible release commands. Internal `Core.*` names and the solution filename are retained for compatibility.

## Security and privacy

MachinScope has no application telemetry endpoint, mandatory cloud service or automatic diagnostic upload. Explicit network diagnostics contact the requested destination and configured DNS resolvers. Bundled runtime components retain their own terms and behavior.

Exports, screenshots, logs and recovery backups can contain private information. Review them before sharing. See [SECURITY.md](SECURITY.md) for safeguards and vulnerability reporting.

## Open Source

Developed and maintained by **AitLilCat**.

The project welcomes bug reports, feature proposals and contributions. See [CONTRIBUTING.md](CONTRIBUTING.md) for development guidance and provenance information.

Project-owned source is distributed under the [MIT license](LICENSE). Bundled components retain their respective [third-party licenses and notices](THIRD-PARTY-NOTICES/README.md) and [runtime redistribution terms](RUNTIME-TERMS.txt).
