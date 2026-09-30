#!/usr/bin/env python3
"""Check reviewed inline presentation exceptions in migrated ZUI sources.

This is deliberately a small, syntax-level guard.  It records source-line
occurrences rather than trying to infer whether an assignment is truly visual.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from collections import Counter
from pathlib import Path
from typing import Iterable


ALLOWED_CATEGORIES = {"dynamicgeometry", "content", "compatibility", "state", "legacydebt"}

# These are the presentation-bearing inline fields for this migration.  Other
# layout fields (for example left/top/flex/display) remain outside this guard.
STYLE_PROPERTY = re.compile(
    r"\.style\."
    r"(?:background(?:Color|Image)|color|font(?:Size|Style|Family)?|unityFontStyleAndWeight|"
    r"opacity|border(?:Top|Right|Bottom|Left)?(?:Color|Width|Radius)?|borderRadius|"
    r"margin(?:Top|Right|Bottom|Left)?|padding(?:Top|Right|Bottom|Left)?|"
    r"(?:min|max)?(?:width|height))\s*=",
    re.IGNORECASE,
)

# Painter APIs are too varied for a safe automatic semantic classification.
# Surface literal Unity colours for a reviewer instead of treating them as safe.
HARD_CODED_COLOR = re.compile(
    r"\b(?:new\s+Color(?:32)?\s*\(|Color\.(?:white|black|gray|clear|red|green|blue|yellow|cyan|magenta))",
    re.IGNORECASE,
)


def normalized_line(line: str) -> str:
    """Stable comparison form for one physical source line."""
    return " ".join(line.strip().split())


def occurrences(path: Path, root: Path) -> Counter[tuple[str, str, str]]:
    """Return (relative path, kind, normalized physical source line) counts."""
    found: Counter[tuple[str, str, str]] = Counter()
    relative = path.relative_to(root).as_posix()
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        normalized = normalized_line(line)
        if not normalized:
            continue
        if STYLE_PROPERTY.search(line):
            found[(relative, "inline-style", normalized)] += 1
        if HARD_CODED_COLOR.search(line):
            found[(relative, "hardcoded-color", normalized)] += 1
    return found


def load_manifest(path: Path) -> dict:
    with path.open(encoding="utf-8") as stream:
        manifest = json.load(stream)
    if manifest.get("version") != 1:
        raise ValueError("manifest version must be 1")
    if not isinstance(manifest.get("targets"), list) or not isinstance(manifest.get("exceptions"), list):
        raise ValueError("manifest must contain targets and exceptions arrays")
    return manifest


def expected_occurrences(manifest: dict) -> tuple[Counter[tuple[str, str, str]], list[str]]:
    expected: Counter[tuple[str, str, str]] = Counter()
    errors: list[str] = []
    targets = set(manifest["targets"])
    for index, item in enumerate(manifest["exceptions"]):
        label = f"exceptions[{index}]"
        valid = True
        path = item.get("path")
        kind = item.get("kind")
        line = item.get("line")
        count = item.get("count")
        category = item.get("category")
        reason = item.get("reason")
        if path not in targets:
            errors.append(f"{label}: path must be an explicit target")
            valid = False
        if kind not in {"inline-style", "hardcoded-color"}:
            errors.append(f"{label}: unsupported kind {kind!r}")
            valid = False
        if not isinstance(line, str) or line != normalized_line(line):
            errors.append(f"{label}: line must be a normalized non-empty source line")
            valid = False
        if not isinstance(count, int) or count < 1:
            errors.append(f"{label}: count must be a positive integer")
            valid = False
        if category not in ALLOWED_CATEGORIES:
            errors.append(f"{label}: category must be one of {sorted(ALLOWED_CATEGORIES)}")
            valid = False
        if not isinstance(reason, str) or not reason.strip():
            errors.append(f"{label}: reason is required")
            valid = False
        if valid:
            expected[(path, kind, line)] += count
    return expected, errors


def check(root: Path, manifest: dict) -> list[str]:
    expected, errors = expected_occurrences(manifest)
    actual: Counter[tuple[str, str, str]] = Counter()
    for relative in manifest["targets"]:
        source = root / relative
        if not source.is_file():
            errors.append(f"missing target: {relative}")
            continue
        actual.update(occurrences(source, root))

    for occurrence, count in sorted((actual - expected).items()):
        path, kind, line = occurrence
        errors.append(f"NEW OR MODIFIED {kind} x{count}: {path}: {line}")
    for occurrence, count in sorted((expected - actual).items()):
        path, kind, line = occurrence
        errors.append(f"EXPIRED exception {kind} x{count}: {path}: {line}")
    return errors


def self_test() -> int:
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        source = root / "Assets/ZuiExample.cs"
        source.parent.mkdir(parents=True)
        source.write_text("x.style.width = Measure();\n", encoding="utf-8")
        manifest = {
            "version": 1,
            "targets": ["Assets/ZuiExample.cs"],
            "exceptions": [{
                "path": "Assets/ZuiExample.cs", "kind": "inline-style", "line": "x.style.width = Measure();",
                "count": 1, "category": "dynamicgeometry", "reason": "Measured live layout width."
            }],
        }
        if check(root, manifest):
            print("self-test failed: recorded source did not pass", file=sys.stderr)
            return 1
        source.write_text("x.style.width = Measure();\nx.style.backgroundColor = Color.red;\n", encoding="utf-8")
        if not check(root, manifest):
            print("self-test failed: added cosmetic style did not fail", file=sys.stderr)
            return 1
    print("self-test passed: recorded source passed; synthetic cosmetic assignment failed")
    return 0


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2], help="project root")
    parser.add_argument("--manifest", type=Path, default=Path(__file__).with_name("presentation-exceptions.json"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    try:
        errors = check(args.root.resolve(), load_manifest(args.manifest))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"presentation guard configuration error: {error}", file=sys.stderr)
        return 2
    if errors:
        print("Presentation guard failed:", file=sys.stderr)
        print(*errors, sep="\n", file=sys.stderr)
        return 1
    print("Presentation guard passed: all detected occurrences have current reviewed exceptions.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
