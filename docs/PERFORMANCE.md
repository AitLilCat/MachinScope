# Performance measurement

Measure a named workload under consistent hardware, power, window and refresh
conditions. Keep raw reports under ignored `artifacts/`; review them for private
paths, hardware identifiers and host information before sharing.

## Managed table benchmark

```powershell
.\scripts\table-benchmark.ps1 -Algorithm legacy -Report artifacts/benchmarks/legacy.json
.\scripts\table-benchmark.ps1 -Algorithm optimized -Report artifacts/benchmarks/optimized.json
python scripts/check-performance.py artifacts/benchmarks/legacy.json artifacts/benchmarks/optimized.json
```

The managed reconciliation harness exercises synthetic row scenarios. It excludes
WinUI layout/rendering/input, native collection and complete page refresh latency.
The comparison gate checks matching scenarios with a broad 50% or 5 ms tolerance;
it is not an application-wide performance certification. Preserve correctness
checks and report fixture seed, runtime, repetition/variation and scope.

## Running-session sample

```powershell
.\scripts\benchmark.ps1 -ProcessId <MachinScope-PID> -Seconds 300 -Scenario Processes
```

This observes CPU, working-set/handle ranges and executable identity. The scenario
label is operator context, not proof of UI state. Managed heap, allocation rate
and application error counts are unavailable to this sampler. A short active
session is not an idle or leak baseline. Compare repeated equivalent workloads
before making performance claims.

## Optional native harness modes

After building with the SDK selected by global.json, run the integration assembly
with `--storage-benchmark <absolute-report>` or
`--phase75-diagnostics <absolute-report>`. The latter switch retains its internal
compatibility name; it measures catalog/cache/metadata work, not MainWindow or
native utility execution. The assembly is under
`tests/Core.IntegrationTests/bin/Release/net10.0-windows10.0.19041.0/`.

## Local instrumentation and acceptance

Optional in-app diagnostics retain bounded numeric durations/outcomes in memory;
disabling clears them. Export is explicit and local. These measurements do not
represent every UI/provider cost; operation durations can include dialog wait.

Review startup/navigation latency, active/inactive CPU, long-running working set
and handles, large-table interaction, scans/duplicates, cancellation/close and
physical hotplug on appropriate hardware. See [VALIDATION.md](VALIDATION.md).
Historical raw reports are kept privately; no historical speed ratio is presented
as a current product benchmark.
