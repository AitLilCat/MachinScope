# Platform support

| Platform | Backend/frontend | Distribution and validation |
| --- | --- | --- |
| Windows 11 x64 | Windows native providers and WinUI 3 | Self-contained portable beta; automated and interactive checks documented for build 26200 |
| Windows 10 x64 | Project target starts at build 19041 | Broader runtime/clean-machine acceptance remains required; target compatibility is not certification |
| Windows ARM64 | No validated distribution | Not supported by the supplied x64 release |
| Linux | No native backend or desktop frontend | No desktop release |

The maintainer reports testing the release candidate on multiple Windows computers.
The recorded automated results remain specific to their execution host; this does
not establish compatibility with every edition, device or security policy.

Domain, Application and Persistence are shared net10.0/AnyCPU projects. Windows
collectors and WinUI remain platform-specific; shared assemblies alone do not make
the application cross-platform. Future Linux work requires genuine native providers,
UI integration and regression tests, not placeholder availability claims.

Core inspection runs locally. Protected processes, provider counters, hardware
properties and administrative tools can be inaccessible. Windows edition, installed
components and policy affect native utility availability. The app runs asInvoker;
external Windows utilities handle their own UAC/authorization.

The portable distribution embeds .NET and Windows App SDK runtimes, including
native resources. First launch extracts them to a writable runtime cache; no
separate .NET installation is required. Settings/logs/recovery remain per-user.
See [BUILDING.md](../BUILDING.md) and [validation checklist](VALIDATION.md).
