#!/usr/bin/env python3
"""
Coverage gate — merges all opencover reports produced by `dotnet test
--collect:"XPlat Code Coverage" ... Format=opencover` and enforces a minimum
LINE coverage over production sources (src/, excluding Migrations/bin/obj).

Merge semantics: a source line counts as covered if it is hit in ANY report
(union across the per-test-project reports), so shared assemblies exercised by
more than one test project are not double-counted.

SCOPE (measured universe): the gate covers the assemblies instrumented by
`dotnet test` over Hub.sln — API, Application, Domain and Infrastructure.
Adapted from tesouro-direto-api/scripts/coverage-gate.py; the merge/exclusion
logic is unchanged. There is no Web/Blazor project and no E2E suite here yet.

Usage: coverage-gate.py <coverage-root-dir> [--threshold N] [--verbose]
Exit code: 0 if coverage >= threshold (or no threshold given), 1 otherwise.
"""
import sys
import glob
import os
import xml.etree.ElementTree as ET
from collections import defaultdict


def is_production_source(path: str) -> bool:
    p = path.replace("\\", "/")
    if "/src/" not in p:
        return False
    if "/Migrations/" in p or "/obj/" in p or "/bin/" in p:
        return False
    return True


def collect(coverage_root: str):
    reports = glob.glob(os.path.join(coverage_root, "**", "coverage.opencover.xml"),
                        recursive=True)
    total = set()            # (path, start_line)
    covered = set()          # (path, start_line) hit in any report
    per_module_total = defaultdict(set)
    per_module_covered = defaultdict(set)

    for report in reports:
        tree = ET.parse(report)
        root = tree.getroot()
        for module in root.iter("Module"):
            mod_name_el = module.find("ModuleName")
            mod_name = mod_name_el.text if mod_name_el is not None else "?"
            # uid -> fullPath, scoped to this module
            uid_to_path = {}
            files_el = module.find("Files")
            if files_el is not None:
                for f in files_el.findall("File"):
                    uid_to_path[f.get("uid")] = f.get("fullPath", "")
            for sp in module.iter("SequencePoint"):
                fileid = sp.get("fileid")
                path = uid_to_path.get(fileid)
                if not path or not is_production_source(path):
                    continue
                key = (path.replace("\\", "/"), sp.get("sl"))
                total.add(key)
                per_module_total[mod_name].add(key)
                if int(sp.get("vc", "0")) > 0:
                    covered.add(key)
                    per_module_covered[mod_name].add(key)

    return reports, total, covered, per_module_total, per_module_covered


def main():
    args = sys.argv[1:]
    if not args:
        print("usage: coverage-gate.py <coverage-root> [--threshold N] [--verbose]")
        return 2
    root = args[0]
    threshold = None
    verbose = "--verbose" in args
    if "--threshold" in args:
        threshold = float(args[args.index("--threshold") + 1])

    reports, total, covered, pmt, pmc = collect(root)
    if not reports:
        print(f"ERROR: no coverage.opencover.xml found under {root}")
        return 2
    if not total:
        print("ERROR: no production source lines found in reports")
        return 2

    pct = 100.0 * len(covered) / len(total)

    print(f"Reports merged: {len(reports)}")
    if verbose:
        for mod in sorted(pmt):
            mt, mc = len(pmt[mod]), len(pmc[mod])
            if mt:
                print(f"  {mod:45s} {100.0*mc/mt:6.2f}%  ({mc}/{mt} lines)")
    print(f"Line coverage (src/, excl. Migrations): "
          f"{pct:.2f}%  ({len(covered)}/{len(total)} lines)")

    if threshold is not None:
        print(f"Threshold: {threshold:.2f}%")
        if pct + 1e-9 < threshold:
            print(f"FAIL: coverage {pct:.2f}% < threshold {threshold:.2f}%")
            return 1
        print(f"PASS: coverage {pct:.2f}% >= threshold {threshold:.2f}%")
    return 0


if __name__ == "__main__":
    sys.exit(main())
