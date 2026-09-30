#!/usr/bin/env python3
"""Create the Phase 0 migration classification from the source inventory.

Routes are triage routes, not implementation approval.  This script intentionally
keeps no-UI and unknown families visible and never infers an exclusion from words
such as legacy, held, or retained.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import subprocess
from collections import defaultdict
from pathlib import Path


HERE = Path(__file__).resolve().parent
COMMIT = "9eb3d9c"


def route(side: str, evidence: str, status: str) -> str:
    if status != "surface(s) inventoried":
        return "no UI / unknown: needs owner confirmation"
    if side == "Runtime":
        return "runtime strategy: player-safe presentation adapter and consumer review"
    if side == "Editor":
        if "UI Toolkit" in evidence and ("IMGUI" in evidence or "ZUI" in evidence):
            return "Toolkit separation with legacy adapter boundary"
        if "UI Toolkit" in evidence:
            return "Toolkit separation"
        if "IMGUI" in evidence or "ZUI" in evidence:
            return "legacy editor port / adapter decision"
        return "editor renderer route needs confirmation"
    return "external/sample route needs confirmation"


def baseline_state(root: Path, sources: list[dict]) -> tuple[str, list[str], list[str]]:
    mismatches, absent = [], []
    for source in sources:
        repo_path = "Assets/Packages/Laubrary/" + source["path"]
        shown = subprocess.run(["git", "show", f"{COMMIT}:{repo_path}"], cwd=root, capture_output=True)
        if shown.returncode:
            absent.append(source["path"])
        elif hashlib.sha256(shown.stdout).hexdigest() != source["sha256"]:
            mismatches.append(source["path"])
    if not mismatches and not absent:
        return f"frozen git commit {COMMIT}", mismatches, absent
    return f"concurrent source snapshot; differs from frozen git {COMMIT}", mismatches, absent


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", type=Path, default=HERE / "surface-inventory.json")
    parser.add_argument("--out", type=Path, default=HERE)
    args = parser.parse_args()
    inv = json.loads(args.inventory.read_text(encoding="utf-8"))
    root = Path(inv["method"]["scope"]).parents[2]
    identity_path = args.out / "baseline-identity.json"
    identity = json.loads(identity_path.read_text(encoding="utf-8")) if identity_path.exists() else None
    if identity:
        baseline = f"immutable Git blob baseline {identity['baseline_commit']}"
        mismatches, absent = [], []
    else:
        baseline, mismatches, absent = baseline_state(root, inv["sources"])
    surfaces = defaultdict(list)
    for surface in inv["surfaces"]:
        surfaces[(surface["side"], surface["owning_tool"])].append(surface)
    rows = []
    for family in inv["families"]:
        key = (family["side"], family["family"])
        members = surfaces[key]
        skins = sorted(set(s["skin"] for s in members))
        popovers = sum(s["kind"] == "popover" for s in members)
        unknown_renderer = sum(s["renderer"] == "unknown / no renderer evidence" for s in members)
        holes = []
        if popovers:
            holes.append(f"{popovers} named popover(s): separate root/skin attachment must be checked")
        if unknown_renderer:
            holes.append(f"{unknown_renderer} named surface(s) lack lexical renderer evidence")
        if family["status"] != "surface(s) inventoried":
            holes.append("no named surface from lexical scan; menus, overlays, inspectors and runtime consumers still need reconciliation")
        if family["side"] == "Runtime":
            holes.append("runtime use needs player, input, scaling and persistence acceptance")
        rows.append({
            "family_owner": f"{family['side']}/{family['family']}",
            "current_evidence": family["renderer_evidence"],
            "named_surface_records": family["surfaces"],
            "migration_route": route(family["side"], family["renderer_evidence"], family["status"]),
            "baseline_skin": "; ".join(skins) if skins else "unknown: no named surface classification",
            "known_holes": "; ".join(holes) or "menus/context menus/overlays still require explicit reconciliation",
            "held_deprecated_status": "source text may mention legacy/held/retained; no exclusion inferred",
            "later_acceptance_needed": "whole-task functional and visual baseline comparison; populated/empty/disabled/selected/focused/hovered/dragging/error states; menu/popover/context-root coverage",
            "baseline_identity": baseline,
        })
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    fields = list(rows[0])
    with (out / "migration-classification.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader(); writer.writerows(rows)
    baseline_lines = ([f"- Frozen Git baseline: `{identity['baseline_commit']}`.", f"- Baseline/current relevant-file denominators: {identity['baseline_detected_relevant_files']} / {identity['snapshot_detected_relevant_files']}.", f"- Raw matches: {identity['counts']['raw match']}; EOL-only differences: {identity['counts']['EOL-only difference']}; actual content differences after CRLF-to-LF normalization: {identity['counts']['content difference']}; baseline-only: {identity['counts']['baseline-only']}; snapshot-only: {identity['counts']['snapshot-only']}.", "- `BASELINE_IDENTITY.md` and `baseline-source-hashes.csv` bind this register to Git blobs and distinguish checkout line endings from source changes."] if identity else [f"- Inventory source baseline: **{baseline}**.", f"- Files whose current raw bytes differ from `{COMMIT}`: {len(mismatches)}.", f"- Screened files absent from `{COMMIT}`: {len(absent)}.", "- Run `build_baseline_identity.py --baseline 9eb3d9c` before treating this as an immutable baseline."])
    lines = ["# Phase 0 migration classification", "", "This is a source-inventory triage register. It assigns tool-family ownership, not people, and identifies an implementation route without approving a migration or inferring exclusions.", "", "## Baseline identity", "", *baseline_lines, "", "## Known inventory holes", "", "- Named popovers are registered, but context menus and `GenericMenu` call sites are not class-shaped surfaces and require an explicit reconciliation pass.", "- Menus, overlays, preview canvases and nested/floating roots can be missed by lexical class detection.", "- The inventory's named-surface count is a source-screening result, not a claim that this number was confirmed live in Unity.", "- A source mention of legacy, held, retained or deprecated is evidence to review, never an inferred owner-approved exclusion.", "- Real Shaper is external in `D:/UNITY/Laubrary Dev - Shaper`; it is not represented here.", "", "## Family routes", "", "| Family owner | Current source evidence | Named surface records | Route | Baseline skin | Known holes | Later acceptance |", "|---|---|---:|---|---|---|---|"]
    for row in rows:
        lines.append(f"| {row['family_owner']} | {row['current_evidence']} | {row['named_surface_records']} | {row['migration_route']} | {row['baseline_skin']} | {row['known_holes']} | {row['later_acceptance_needed']} |")
    lines += ["", "## Route meanings", "", "- **Toolkit separation:** preserve the current editor renderer while moving static presentation into an explicit skin/layout contract; verify dynamic geometry and custom painters separately.", "- **Toolkit separation with legacy adapter boundary:** retain the existing immediate-mode/ZUI path behind a reviewed adapter until the comparable Toolkit surface meets its functional and visual acceptance evidence.", "- **Legacy editor port / adapter decision:** no renderer migration is presumed. Establish the supported replacement or approved retained route before changing presentation ownership.", "- **Runtime strategy:** separate player-safe interaction/presentation from editor services, then validate real player input, focus, scaling and persistence in a consumer.", "- **No UI / unknown:** this is an explicit confirmation queue, not zero scope.", "", "## Reproduction", "", "```powershell", "python .\\Documentation\\UISeparation\\Inventory\\build_baseline_identity.py --baseline 9eb3d9c", "python .\\Documentation\\UISeparation\\Inventory\\build_migration_classification.py", "```"]
    (out / "MIGRATION_CLASSIFICATION.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
