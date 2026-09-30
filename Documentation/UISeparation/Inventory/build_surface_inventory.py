#!/usr/bin/env python3
"""Build a reproducible, source-only Laubrary UI surface inventory.

This is deliberately a lexical inventory, not a C# parser or a visual audit.  It
records the evidence used for each classification and labels all style counts as
candidate screening metrics.  Run from any directory; output is written beside
this script by default.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_PACKAGE = SCRIPT_DIR.parents[2] / "Assets" / "Packages" / "Laubrary"
TEXT_SUFFIXES = {".cs", ".uss", ".uxml", ".asmdef"}
CLASS_RE = re.compile(r"\bclass\s+(?P<name>[A-Za-z_]\w*)\s*(?::\s*(?P<bases>[^\{\n]+))?")
CUSTOM_EDITOR_RE = re.compile(r"\[CustomEditor(?:\([^\]]*\))?\]")
STYLE_ASSIGN_RE = re.compile(r"(?:\.style\.[A-Za-z_]\w*|\bstyle\.[A-Za-z_]\w*)\s*=(?!=)")
CLASS_CALL_RE = re.compile(r"\.(?:AddToClassList|EnableInClassList|RemoveFromClassList)\s*\(")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def family_for(path: Path, root: Path) -> tuple[str, str]:
    """Return the package side and owning family without pretending every file is UI."""
    parts = path.relative_to(root).parts
    side = parts[0] if parts and parts[0] in {"Editor", "Runtime"} else "Other"
    family = parts[1] if len(parts) > 1 and side in {"Editor", "Runtime"} else (parts[0] if parts else "Other")
    return side, family


def evidence_for(text: str, path: Path, side: str) -> tuple[str, list[str], str, str]:
    """Return renderer, evidence tokens, skin assessment, and dynamic-layout warning."""
    evidence: list[str] = []
    has_uitk = any(token in text for token in ("UnityEngine.UIElements", "VisualElement", "CreateGUI(", "AddToClassList(", "StyleSheet", "UIDocument"))
    has_zui = "ZUI" in text or "Zui" in text
    has_imgui = any(token in text for token in ("OnGUI(", "EditorGUILayout", "GUILayout", "IMGUIContainer", "GUI."))
    has_ugui = "UnityEngine.UI" in text or "Canvas" in text and "Button" in text
    has_painter = any(token in text for token in ("Handles.", "GUI.DrawTexture", "GL.", "MeshGenerationContext", "Painter2D"))
    for name, present in (("UI Toolkit", has_uitk), ("ZUI", has_zui), ("IMGUI", has_imgui), ("uGUI", has_ugui), ("custom painter", has_painter)):
        if present:
            evidence.append(name)
    if has_uitk and has_imgui:
        renderer = "mixed UI Toolkit + IMGUI"
    elif has_uitk:
        renderer = "UI Toolkit"
    elif has_zui and has_imgui:
        renderer = "ZUI / IMGUI"
    elif has_imgui:
        renderer = "IMGUI"
    elif has_ugui:
        renderer = "uGUI"
    elif has_painter:
        renderer = "custom painter"
    else:
        renderer = "unknown / no renderer evidence"
    if ".uss" in text or "StyleSheet" in text or "AddToClassList(" in text:
        skin = "known: Toolkit stylesheet/class hook"
    elif has_zui:
        skin = "known: ZUI or legacy style state"
    elif renderer == "unknown / no renderer evidence":
        skin = "unknown"
    else:
        skin = "unknown: inspect visual path"
    dynamic_terms = ("GeometryChangedEvent", "resolvedStyle", "worldBound", "Measure", "GetRect", "position.", "width =", "height =", "flexGrow")
    dynamic = "yes: " + ", ".join(sorted({term for term in dynamic_terms if term in text})) if any(term in text for term in dynamic_terms) else "none detected"
    return renderer, evidence, skin, dynamic


def shared_dependencies(text: str) -> list[str]:
    markers = {
        "ZUI": ("ZUI", "Zui"),
        "asset browser": ("LauAsset", "AssetBrowser"),
        "tag picker": ("LauTag", "TagPicker"),
        "reflection/form": ("Reflect", "Form", "SerializedProperty"),
        "curve/envelope": ("CurveField", "Envelope"),
        "style factory": ("StyleSheet", "StyleDefinition", "AddToClassList"),
    }
    return [name for name, terms in markers.items() if any(term in text for term in terms)]


def surface_kind(name: str, bases: str, text: str, side: str) -> str | None:
    """Class-local type evidence prevents unrelated nested classes becoming surfaces."""
    popup_base = "PopupWindowContent" in bases or "PopupWindow" in bases
    editor_window_base = "EditorWindow" in bases or "ZUIWindow" in bases
    if popup_base or (name.endswith("Popup") and ("ShowAsDropDown" in text or "PopupWindow" in text)):
        return "popover"
    if CUSTOM_EDITOR_RE.search(text) or name.endswith("Inspector") or name.endswith("Editor") and "Editor" in bases:
        return "inspector"
    if editor_window_base or name.endswith("Window"):
        return "editor window"
    if side == "Runtime" and ("UIDocument" in text or "UnityEngine.UI" in text or "OnGUI(" in text):
        return "runtime UI"
    return None


def lifecycle(text: str) -> str:
    hits = []
    for label, token in (("legacy", "legacy"), ("deprecated", "deprecated"), ("obsolete", "obsolete"), ("held", "held"), ("retained", "retained")):
        if re.search(r"\b" + token + r"\b", text, re.I):
            hits.append(label)
    return "held/legacy candidate: " + ", ".join(hits) if hits else "not classified by source screening"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE)
    parser.add_argument("--out", type=Path, default=SCRIPT_DIR)
    args = parser.parse_args()
    root = args.package.resolve()
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() in TEXT_SUFFIXES)
    sources, surfaces = [], []
    families: dict[tuple[str, str], dict] = defaultdict(lambda: {"files": 0, "ui_files": 0, "surfaces": 0, "renderers": Counter(), "states": Counter(), "static_style_candidates": 0, "class_calls": 0})
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        side, family = family_for(path, root)
        key = (side, family)
        families[key]["files"] += 1
        style_count = len(STYLE_ASSIGN_RE.findall(text)) if path.suffix == ".cs" else 0
        class_count = len(CLASS_CALL_RE.findall(text)) if path.suffix == ".cs" else 0
        renderer, evidence, skin, dynamic = evidence_for(text, path, side)
        source = {"path": rel(path, root), "sha256": sha256(path), "bytes": path.stat().st_size, "side": side, "family": family, "renderer_screening": renderer, "style_assignment_candidates": style_count, "class_assignment_candidates": class_count}
        sources.append(source)
        families[key]["static_style_candidates"] += style_count
        families[key]["class_calls"] += class_count
        if evidence:
            families[key]["ui_files"] += 1
            families[key]["renderers"][renderer] += 1
        for match in CLASS_RE.finditer(text):
            name = match.group("name")
            bases = match.group("bases") or ""
            kind = surface_kind(name, bases, text, side)
            if not kind:
                continue
            row = {"surface": name, "kind": kind, "side": side, "owning_tool": family, "source": rel(path, root), "renderer": renderer, "renderer_evidence": "; ".join(evidence) or "no lexical renderer marker", "shared_dependencies": "; ".join(shared_dependencies(text)) or "none detected", "skin": skin, "dynamic_layout_constraints": dynamic, "lifecycle": lifecycle(text), "style_assignment_candidates": style_count, "class_assignment_candidates": class_count, "source_sha256": source["sha256"]}
            surfaces.append(row)
            families[key]["surfaces"] += 1
            families[key]["states"][row["lifecycle"]] += 1
    family_rows = []
    for (side, family), item in sorted(families.items()):
        family_rows.append({"side": side, "family": family, "files": item["files"], "ui_evidence_files": item["ui_files"], "surfaces": item["surfaces"], "renderer_evidence": "; ".join(f"{k}={v}" for k, v in item["renderers"].most_common()) or "no UI evidence", "static_style_assignment_candidates": item["static_style_candidates"], "class_assignment_candidates": item["class_calls"], "status": "surface(s) inventoried" if item["surfaces"] else ("UI evidence but no named surface" if item["ui_files"] else "no UI evidence / unknown by lexical screening")})
    payload = {"generated_utc": datetime.now(timezone.utc).isoformat(), "method": {"scope": str(root), "mode": "source-only lexical screening", "limitations": ["Not a C# semantic parser.", "Style and class counts are candidate patterns, not complete semantic classifications.", "No Unity editor, compile, runtime, or visual inspection was performed.", "Shaper real engine is external scope in D:/UNITY/Laubrary Dev - Shaper and is not included."]}, "summary": {"source_files": len(sources), "surface_records": len(surfaces), "families": len(family_rows), "static_style_assignment_candidates": sum(s["style_assignment_candidates"] for s in sources), "class_assignment_candidates": sum(s["class_assignment_candidates"] for s in sources)}, "families": family_rows, "surfaces": surfaces, "sources": sources}
    (out / "surface-inventory.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    def write_csv(name: str, rows: list[dict]) -> None:
        fields = list(rows[0]) if rows else ["empty"]
        with (out / name).open("w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=fields)
            writer.writeheader(); writer.writerows(rows)
    write_csv("surface-register.csv", surfaces)
    write_csv("family-summary.csv", family_rows)
    write_csv("source-hashes.csv", sources)
    lines = ["# Laubrary UI surface register", "", "Generated by `build_surface_inventory.py` from package source only. This is a reproducible lexical inventory, not a visual audit or a complete semantic classification.", "", "## Baseline summary", "", f"- Source files screened: {payload['summary']['source_files']}", f"- Named surface records: {payload['summary']['surface_records']}", f"- Package families recorded: {payload['summary']['families']}", f"- Static style-assignment candidates: {payload['summary']['static_style_assignment_candidates']}", f"- Semantic class/state-call candidates: {payload['summary']['class_assignment_candidates']}", "", "These two counts only screen direct lexical patterns. They do not trace helpers, resolve C# intent, or prove visual ownership. Dynamic geometry, content images, and custom painters need manual review.", "", "## Family register", "", "| Side | Family | Files | UI-evidence files | Surfaces | Renderer evidence | Static style candidates | Class/state candidates | State |", "|---|---|---:|---:|---:|---|---:|---:|---|"]
    for r in family_rows:
        lines.append(f"| {r['side']} | {r['family']} | {r['files']} | {r['ui_evidence_files']} | {r['surfaces']} | {r['renderer_evidence']} | {r['static_style_assignment_candidates']} | {r['class_assignment_candidates']} | {r['status']} |")
    lines += ["", "## Surface register", "", "| Surface | Kind | Tool | Renderer (evidence) | Skin | Shared controls/dependencies | Dynamic/layout constraints | Held state |", "|---|---|---|---|---|---|---|---|"]
    for r in sorted(surfaces, key=lambda x: (x['side'], x['owning_tool'], x['surface'])):
        lines.append(f"| {r['surface']} | {r['kind']} | {r['side']}/{r['owning_tool']} | {r['renderer']} ({r['renderer_evidence']}) | {r['skin']} | {r['shared_dependencies']} | {r['dynamic_layout_constraints']} | {r['lifecycle']} |")
    lines += ["", "## Phase 0/1 implications", "", "- Pilot controls to preserve: button/toggle, slider, envelope or curve, and a width-sensitive populated row. The source register flags curve/envelope and dynamic-layout references where present; visual measurement remains required.", "- Skin controls: UI Toolkit class/stylesheet hooks are candidates for explicit skin attachment. ZUI and legacy style-state paths must remain isolated until a reviewed adapter exists.", "- Separate-root risk: popovers and any independently-rooted editor/runtime UI must receive the selected presentation context explicitly.", "- External scope: the real Shaper engine is in `D:/UNITY/Laubrary Dev - Shaper`, a separate worktree; it is intentionally excluded to avoid double-counting shared package code.", "- Unknown/no-UI-evidence families are retained in the family register. They are not evidence that the family has no UI; they require owner review before any exclusion decision.", "", "## Reproduction", "", "```powershell", "python .\\Documentation\\UISeparation\\Inventory\\build_surface_inventory.py", "```", "", "Outputs are JSON for tooling, three CSV files for review, and this Markdown register. `source-hashes.csv` binds every screened relevant source file to the baseline."]
    (out / "SURFACE_REGISTER.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
