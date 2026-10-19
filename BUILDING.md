# Building MachinScope

## Toolchain

Windows x64, .NET SDK 10.0.401 (global.json, latest patch roll-forward), NuGet
access for initial restore and Windows SDK/WinUI build tooling. Python 3.11+ is
required for SHA-256 archive packaging. Windows 11 is the tested host; the target
minimum is Windows 10 build 19041. No Linux desktop host is implemented.
Scripts prefer the optional ignored `.tools/dotnet/dotnet.exe`, then installed dotnet.
The SDK cache is not required in source distributions. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1`
for direct commands if desired; the scripts already set it.

## Restore, compile and test

```powershell
dotnet --version
dotnet restore CORE.slnx --locked-mode
dotnet build CORE.slnx -c Release --no-restore
.\scripts\test.ps1
dotnet build CORE.slnx -c Release --no-restore -p:RunAnalyzersDuringBuild=true -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest -warnaserror
dotnet list CORE.slnx package --vulnerable --include-transitive --format json
```

`build.ps1` wraps restore/build. `test.ps1` runs both MSTest suites and checks that
both reports contain only passing tests. Fixtures use disposable processes/files
and dedicated test registry keys. Managed restart coverage requires Medium process
integrity; the script selects applicable categories and records environment
exclusions explicitly. Use `test.ps1 -StandardUserProcessLaunch Require` in a
normal user terminal for full restart coverage. Elevated CI still runs the real
launch-denial security test and other native fixtures. See
[test environments](docs/VALIDATION.md#native-process-launch-context). Native
integration checks depend on Windows providers and host hardware. Raw logs/TRX
are private local artifacts under `artifacts/`, which is ignored.

## Portable release

```powershell
.\scripts\release.ps1 -Version 0.1.0-beta
```

The script builds/tests, checks NuGet advisories, runs analyzers, publishes into a
new `artifacts/MachinScope-release-<id>/` directory, verifies product metadata,
executes the opt-in bootstrap probe from the temporary directory, gathers license
notices and creates/verifies the portable ZIP and `artifacts/SHA256SUMS.txt`.
It never publishes remotely. The probe initializes WinUI/XAML and checks native
tool availability without launching MainWindow or management utilities; it does
not replace interactive acceptance.

To publish only:

```powershell
.\scripts\publish-portable.ps1
python scripts/dependency-notices.py
python scripts/package-delivery.py
```

Single-file publication uses the existing WinUI-supported flags:

```powershell
dotnet publish src/Core.Desktop/Core.Desktop.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:NuGetLockFilePath=packages.portable.lock.json -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/MachinScope-single-exe
```

Outputs: `MachinScope-v0.1.0-beta-win-x64.zip` and `SHA256SUMS.txt` under `artifacts/`.
GitHub-generated source downloads are sufficient for normal source distribution.
For local review or offline handoff, `release.ps1 -IncludeSourceArchive` or
`package-delivery.py --include-source` adds a source ZIP with per-file integrity
checks. The release workflow publishes only the Windows ZIP and checksum manifest.
Runtime ZIP: EXE, launch README, MIT license, third-party metadata/license notices
and a per-file integrity manifest. No SDK install is needed at runtime; embedded
.NET/Windows App SDK/native SQLite resources extract on launch. Writable local
storage is required. This is self-contained publication, not Native AOT.

`publish.ps1` retains the alternative folder distribution. Keep its PRI, compiled
XAML and runtime files beside MachinScope.exe. `install.ps1` is an optional per-user
copy/shortcut helper, not a prerequisite for the portable ZIP.

## Revalidate an unchanged portable candidate

For documentation or test-only changes, retain the existing EXE, Windows ZIP and bundled
notice files. Re-run the full checks and verify the archive without rewriting it:

```powershell
.\scripts\release.ps1 -ExistingPortableExe artifacts/MachinScope-beta-final/MachinScope.exe
```

This mode runs restore/build/tests, advisories, analyzers, metadata and bootstrap
checks against the preserved candidate. It skips publishing/signing and notice
regeneration, then verifies that the existing ZIP exactly matches the candidate
and current runtime companions. It fails if any companion changed. A changed
application/dependency requires normal `release.ps1` and new artifact acceptance.
The normal build outputs are separate from the preserved distribution.

## Signing

Unsigned is the default. No private certificates are needed to build. For an
owner-approved certificate already installed securely and a trusted timestamp
service, use `release.ps1 -SigningCertificateThumbprint <thumbprint> -TimestampUrl <https-url>`.
It invokes signtool SHA-256 signing/timestamping and verifies before packaging.
No keys belong in source or artifacts. Never disable Windows security controls.
The workflow intentionally has no signing secrets; connect a secure signing runner
only after owner approval. Metadata is distinct from signing and SmartScreen trust.

## Source layout and reproducibility

`src/Core.Domain` contains models, `Core.Application` shared logic/contracts,
`Core.Windows` native providers, `Core.Persistence` local storage and `Core.Desktop`
the WinUI host. `tests` holds unit/integration tests and optional benchmarks.
`docs` contains current architecture, features, APIs, platforms, measurement,
validation and publishing guidance.
`CORE.slnx`, routes, resource keys and `%LOCALAPPDATA%\CORE` paths stay compatible.
Dependency versions are declared in projects; generated package lock files preserve
transitive resolution. Normal builds use packages.lock.json; single-file publication
uses separate packages.portable.lock.json files because the SDK adds ILLink tooling.
SDK/runtime versions and ZIP timestamps can vary between hosts, so byte-identical
packages are not promised.

CI uses Windows runners. Release workflow dispatch defaults to artifact-only;
publication requires an explicit publish input and the public-beta environment.
Configure required reviewers on that environment before enabling release writes.
See [publishing instructions](docs/PUBLISHING.md) for preserving existing history.
