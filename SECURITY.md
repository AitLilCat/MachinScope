# Security policy

The 0.1.0-beta line is the current security review target. There are no supported
stable releases yet and no guaranteed response time. Report vulnerabilities
privately using the repository's GitHub **Security → Report a vulnerability**
facility if enabled. If it is unavailable, open an issue asking for a private
reporting channel without publishing exploit details, secrets or inventories.

Include affected version, Windows build, privileges, impact and minimal steps.
Redact usernames, paths, addresses, process command lines and device identifiers.
Never attach credentials or private signing keys. Ordinary bugs belong in issues.

MachinScope runs asInvoker and never silently elevates. Process mutations use
creation identities and native handle checks; destructive actions require review.
Startup controls cover current-user Run entries with recovery backups. Services,
disks and devices are read-only. Native administration tools apply their own UAC.
File actions refuse overwrites and protected directories, but mutable path checks
are not a sandbox against adversarial concurrent filesystem changes.

No telemetry or automatic diagnostic uploads are implemented. Explicit network
diagnostics resolve/contact the requested host; DNS resolution can use configured
resolvers. Native Windows tools may have their own network behavior. Local logs
and exports can contain private information. Review before sharing.

Unsigned builds do not establish trusted publisher identity or SmartScreen
reputation. Never weaken Windows protections to run them.

## Implementation boundaries

Process control validates creation time, critical status and executable location
on the handle used for mutation. System PIDs, critical processes, the app itself
and Windows executables are refused. Restart is limited to recorded standard-user
launches with executable hash/context rechecks; failed replacement is reported.

Startup controls preserve the exact current-user Run command and registry kind
before disabling, verify results, retain recovery data on failure and refuse
conflicting restores. Windows registry values do not provide an atomic
compare-and-delete operation against another startup manager. RunOnce, machine
entries, services, folders and StartupApproved policy stay read-only.

File changes require review, refuse overwrites/protected directories and reject
reparse ancestors. Path-based operations are not an adversarial sandbox against
concurrent replacement. Scans skip reparse entries and never decide deletion.
Explicit hashes revalidate file identity/size/timestamps and discard changed
results. Restart Manager is queried without closing applications.

Device inspection is read-only; observed session identity does not authorize
future controls. Disk properties revalidate identity; every disk-mutation decision
is denied. Connection observations do not establish initiator direction, reliable
per-process bytes or atomic permission to close a connection.

CSV exports quote fields and guard formula-leading values; JSON preserves strings.
Save/export is explicit. Local logs, process command lines, network addresses,
serial identifiers and startup backups may contain private information.

## Dependencies and distribution

Scan the exact source and artifacts being released. Pattern scans may miss
credentials or private data, and NuGet advisories may not cover every native
component. Neither replaces review.
Application source has no automatic telemetry upload; bundled Microsoft runtime
components and external native tools retain independent behavior and terms.

Project MIT does not relicense bundled components. Preserve
[third-party notices](THIRD-PARTY-NOTICES/README.md) and
[runtime terms](RUNTIME-TERMS.txt). Confirm icon/artwork ownership before
publication. See the [validation checklist](docs/VALIDATION.md).
