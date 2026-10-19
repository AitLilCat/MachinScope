"""Broad managed-pipeline regression gate; does not certify desktop performance."""
import json
import sys
from pathlib import Path

before = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
after = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8-sig"))
baseline = {case["name"]: case for case in before["cases"]}
current = {case["name"]: case for case in after["cases"]}
if baseline.keys() != current.keys():
    raise SystemExit("Benchmark scenarios differ")
failures = []
for name, case in baseline.items():
    limit = max(case["medianMs"] * 1.5, case["medianMs"] + 5)
    if current[name]["medianMs"] > limit:
        failures.append(f"{name}: {current[name]['medianMs']:.3f} ms exceeds {limit:.3f} ms")
if failures:
    raise SystemExit("\n".join(failures))
print(f"Passed {len(baseline)} managed pipeline checks (50% or 5 ms tolerance). UI/native/idle performance is outside this gate.")
