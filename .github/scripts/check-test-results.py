"""Reject missing, empty, failed, or skipped VSTest TRX runs in CI."""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main():
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check-test-results.py path/to/results.trx")
    path = Path(sys.argv[1])
    if not path.is_file():
        raise SystemExit(f"Missing test results: {path}")
    root = ET.parse(path).getroot()
    counters = root.find("{*}ResultSummary/{*}Counters")
    if counters is None:
        raise SystemExit(f"Missing test counters: {path}")
    total, executed, passed, failed, not_executed = (
        int(counters.get(name, "0"))
        for name in ("total", "executed", "passed", "failed", "notExecuted")
    )
    results = root.findall("{*}Results/{*}UnitTestResult")
    # xUnit/VSTest can report notExecuted=0 even when rows are NotExecuted.
    skipped = max(not_executed, sum(r.get("outcome") == "NotExecuted" for r in results))
    print(
        f"{path}: total={total}, executed={executed}, passed={passed}, "
        f"failed={failed}, skipped={skipped}",
        flush=True,
    )
    if (
        total <= 0
        or executed != total
        or passed != total
        or failed != 0
        or skipped != 0
        or len(results) != total
        or any(result.get("outcome") != "Passed" for result in results)
    ):
        raise SystemExit("Expected a nonempty test run with every case executed and passed.")


if __name__ == "__main__":
    main()
