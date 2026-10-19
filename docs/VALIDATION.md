# Validation and release acceptance

## Automated checks

Use the toolchain in [BUILDING.md](../BUILDING.md):

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
python scripts/audit-source.py
python scripts/check-repository.py
```

Run both MSTest suites for each candidate. `test.ps1` requires complete passing
reports for every selected suite. Keep raw TRX files and logs in ignored
`artifacts/` storage.
Fixtures use disposable processes/files and dedicated test registry keys, not real
disk mutations. Native-provider tests depend on the host.

### Native process-launch context

Managed launch/restart intentionally permits only a real Medium-integrity session.
GitHub-hosted Windows runners run as administrators with UAC disabled
([runner privileges](https://docs.github.com/en/actions/reference/runners/github-hosted-runners#administrative-privileges));
they do not satisfy this application safety requirement. Do not inject a fake
Medium token, weaken the launch guard or elevate/demote the machine to pass tests.

`test.ps1` probes the real native provider using the inherited process token and
records only integrity and suite selection in `test-context.json` beside TRX files.
The baseline suite always executes terminated-process inspection and
`ProcessLaunchSecurity`, which checks the actual app guard before path validation:
non-Medium contexts must receive the safety rejection, while Medium contexts reach
ordinary invalid-path validation without launching a child.

`ManagedRestartCreatesNewIdentityAndRejectsUnknownContext` belongs to
`StandardUserProcessLaunch`. Auto selection runs this separate category only under
Medium integrity. Otherwise it is explicitly excluded and never counted as passing.
Unknown/unreadable probe context fails suite selection. A direct `dotnet test` run
in an unsuitable context reports the restart test as inconclusive, not successful.
The terminated-process test uses its own readiness-checked disposable fixture and
does not depend on managed launch/restart. No interactive desktop is needed for
these fixtures, but real Medium integrity is required for the restart feature.

```powershell
# Normal complete run: applicable categories selected from real native context
.\scripts\test.ps1
# Fail if the full restart category cannot actually execute (normal user terminal)
.\scripts\test.ps1 -StandardUserProcessLaunch Require
# Exercise baseline selection without claiming restart coverage
.\scripts\test.ps1 -StandardUserProcessLaunch Exclude
```

CI uploads context JSON and TRX and records category execution in the job summary.
Retain real Medium-session coverage before releases as well as elevated-runner
guard coverage; exclusion is an environment boundary, not feature acceptance.

For a new executable, `release.ps1` also runs advisory/analyzer checks, publishes,
verifies metadata, executes the opt-in WinUI bootstrap probe and checks archive
CRC/per-file SHA-256 and distribution checksums. The probe runs outside the source
working directory. It does not open MainWindow or certify interactive behavior.

For documentation or test-only changes, preserve the verified portable EXE
and ZIP. Compare their hashes before/after, verify ZIP entries and checksum
manifest, and build/test the cleaned source. A rebuilt artifact needs fresh acceptance. GitHub-hosted workflow execution and the
optional signing branch need their own validation.

## Interactive acceptance

- Startup and graceful close; navigation, Back/Forward, refresh and cancellation.
- Process trees/identities, properties, sampled rates and inaccessible/exited targets.
- Table filtering, sorting, selection, keyboard/clipboard and export/save round trip.
- Storage layout/properties and honest missing health/counter/provider states.
- Device inventory, relationships, physical hotplug/removal and stale observations.
- Network endpoints/owners, listening versus established state, adapter traffic and
  opt-in diagnostics; no invented direction, process bandwidth or byte counts.
- Startup recovery using disposable fixtures; no overwrite of conflicting entries.
- Themes, high contrast, scaling, resizing and permission/error reporting.
- Active/inactive/long-running performance on representative hardware.
- Clean-machine launch without installed runtimes, under normal Windows security
  policy, and acceptance on supported Windows versions/editions.

Never weaken Windows protections to pass a test. Do not manipulate real protected
processes, disks or devices as a convenience check. Inspect exports/screenshots
for private information before publication.

## Publication gates

Review icon/artwork provenance and third-party redistribution obligations, retain
required terms/notices, confirm unsigned/signing status and complete applicable
manual acceptance. Maintainer-reported testing on other computers complements,
but does not replace, recorded reproducible checks. Publish only with explicit
owner approval; see [PUBLISHING.md](PUBLISHING.md).
