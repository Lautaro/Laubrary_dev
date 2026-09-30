#!/usr/bin/env python3
"""Bind the source inventory to a frozen Git baseline without changing package files.

Git blobs are LF-normalized while a Windows checkout may be CRLF.  This report
therefore records both raw SHA-256 hashes and a CRLF-to-LF-only comparison.  The
normalized comparison is not a general formatter: any non-line-ending content
difference remains a current-source deviation.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path


HERE = Path(__file__).resolve().parent
SUFFIXES = {".cs", ".uss", ".uxml", ".asmdef"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def normalize_eol(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n")


def git(root: Path, *args: str) -> bytes:
    result = subprocess.run(["git", *args], cwd=root, capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode("utf-8", "replace").strip())
    return result.stdout


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", required=True, help="Commit-ish holding the immutable source baseline")
    parser.add_argument("--inventory", type=Path, default=HERE / "surface-inventory.json")
    parser.add_argument("--out", type=Path, default=HERE)
    args = parser.parse_args()
    inv = json.loads(args.inventory.read_text(encoding="utf-8"))
    package = Path(inv["method"]["scope"])
    root = package.parents[2]
    prefix = "Assets/Packages/Laubrary/"
    baseline_paths = {
        line[len(prefix):]
        for line in git(root, "ls-tree", "-r", "--name-only", args.baseline, "--", "Assets/Packages/Laubrary").decode("utf-8").splitlines()
        if line.startswith(prefix) and Path(line).suffix.lower() in SUFFIXES
    }
    snapshot = {s["path"]: s for s in inv["sources"]}
    rows = []
    for path in sorted(baseline_paths | snapshot.keys()):
        before = git(root, "show", f"{args.baseline}:{prefix}{path}") if path in baseline_paths else None
        after_path = package / path
        after = after_path.read_bytes() if path in snapshot and after_path.exists() else None
        if before is None:
            state = "snapshot-only"
        elif after is None:
            state = "baseline-only"
        elif digest(before) == digest(after):
            state = "raw match"
        elif normalize_eol(before) == normalize_eol(after):
            state = "EOL-only difference"
        else:
            state = "content difference"
        rows.append({
            "path": path,
            "baseline_raw_sha256": digest(before) if before is not None else "",
            "snapshot_raw_sha256": digest(after) if after is not None else "",
            "baseline_lf_sha256": digest(normalize_eol(before)) if before is not None else "",
            "snapshot_lf_sha256": digest(normalize_eol(after)) if after is not None else "",
            "comparison": state,
        })
    counts = {name: sum(row["comparison"] == name for row in rows) for name in ("raw match", "EOL-only difference", "content difference", "baseline-only", "snapshot-only")}
    identity = {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "baseline_commit": git(root, "rev-parse", args.baseline).decode().strip(),
        "comparison_method": "raw SHA-256 plus CRLF-to-LF-only normalized SHA-256",
        "baseline_detected_relevant_files": len(baseline_paths),
        "snapshot_detected_relevant_files": len(snapshot),
        "paths_compared": len(rows),
        "counts": counts,
        "baseline_identity": "immutable Git blob baseline; snapshot differences classified above",
    }
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    (out / "baseline-identity.json").write_text(json.dumps(identity, indent=2) + "\n", encoding="utf-8")
    with (out / "baseline-source-hashes.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0]))
        writer.writeheader(); writer.writerows(rows)
    lines = ["# Immutable Phase 0 baseline identity", "", f"- Frozen Git baseline: `{identity['baseline_commit']}`.", f"- Baseline relevant files detected: {identity['baseline_detected_relevant_files']}.", f"- Captured snapshot relevant files detected: {identity['snapshot_detected_relevant_files']}.", f"- Paths compared: {identity['paths_compared']}.", f"- Raw byte matches: {counts['raw match']}.", f"- EOL-only differences: {counts['EOL-only difference']}.", f"- Actual content differences after CRLF-to-LF normalization: {counts['content difference']}.", f"- Baseline-only paths: {counts['baseline-only']}.", f"- Snapshot-only paths: {counts['snapshot-only']}.", "", "`baseline-source-hashes.csv` contains raw and normalized SHA-256 values for every detected path. The normalized comparison changes only CRLF line endings; it does not hide formatting or source changes. The baseline and snapshot denominators above make added or omitted relevant files explicit."]
    (out / "BASELINE_IDENTITY.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
