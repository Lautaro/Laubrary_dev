#!/usr/bin/env python3
"""Phase 3 parity check: a before/after window must resolve to the same styles and the same pixels.

Two independent oracles are compared for every captured window state:

  * the resolved-style dump, element by element. Element identity is its path through the tree plus its class
    list, so a migration that ADDS a class changes the identity on purpose; the comparison therefore matches
    elements by position in the tree and reports class-list changes separately from style changes.
  * the PNG, pixel for pixel.

A style difference names the element and the exact property that drifted, which is what makes "appearance is
preserved" checkable rather than asserted.
"""

import json
import os
import sys
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent / "Evidence" / "Phase3"

# Which capture pair to compare. The second pass (base2/cand2) is the authoritative one: it was taken with
# the migration stashed and restored inside one warm editor session, so a first-draw difference in shared
# chrome cannot masquerade as a migration difference.
BASE = os.environ.get("PHASE3_BASE", "base2-")
CAND = os.environ.get("PHASE3_CAND", "cand2-")


def load(label):
    path = EVIDENCE / (label + ".styles.json")
    if not path.exists():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


import re

# Unity names each window's root container with a per-instance counter (rootVisualContainer27), so the same
# window reopened later has a different root name. That is not a UI difference and must not read as one.
ROOT_COUNTER = re.compile(r"rootVisualContainer\d+")


def strip_classes(p):
    """Element path without its class lists, so a deliberately added class does not read as a moved element."""
    return ROOT_COUNTER.sub("rootVisualContainer", "/".join(part.split("[")[0] for part in p.split("/")))


def normalise(p):
    return ROOT_COUNTER.sub("rootVisualContainer", p)


def compare_styles(base, cand):
    be, ce = base["elements"], cand["elements"]
    problems, class_changes = [], []
    if len(be) != len(ce):
        problems.append("element count %d -> %d" % (len(be), len(ce)))
    for i in range(min(len(be), len(ce))):
        b, c = be[i], ce[i]
        if strip_classes(b["p"]) != strip_classes(c["p"]):
            problems.append("[%d] tree shape: %s -> %s" % (i, b["p"], c["p"]))
            continue
        if normalise(b["p"]) != normalise(c["p"]):
            class_changes.append("[%d] %s -> %s" % (i, b["p"], c["p"]))
        if b["rect"] != c["rect"]:
            problems.append("[%d] %s rect %s -> %s" % (i, strip_classes(b["p"]), b["rect"], c["rect"]))
        if b["s"] != c["s"]:
            for bf, cf in zip(b["s"].split(" "), c["s"].split(" ")):
                if bf != cf:
                    problems.append("[%d] %s style %s -> %s" % (i, strip_classes(b["p"]), bf, cf))
    return problems, class_changes


def compare_pixels(label):
    try:
        from PIL import Image, ImageChops
    except ImportError:
        return "pillow missing"
    a, b = EVIDENCE / (BASE + label + ".png"), EVIDENCE / (CAND + label + ".png")
    if not a.exists() or not b.exists():
        return "missing png"
    ia, ib = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
    if ia.size != ib.size:
        return "size %s -> %s" % (ia.size, ib.size)
    if ia.getextrema() == ib.getextrema() and all(lo == hi for lo, hi in ia.getextrema()):
        return "uniform capture, not valid evidence"
    diff = ImageChops.difference(ia, ib)
    changed = sum(1 for px in diff.getdata() if px != (0, 0, 0))
    worst = max(max(px) for px in diff.getdata())
    return "%d changed pixels (worst channel delta %d) of %d" % (changed, worst, ia.size[0] * ia.size[1])


def main():
    labels = sorted({p.name[len(BASE):-len(".styles.json")] for p in EVIDENCE.glob(BASE + "*.styles.json")})
    failures = 0
    for label in labels:
        base, cand = load(BASE + label), load(CAND + label)
        if base is None or cand is None:
            print("%-34s MISSING" % label)
            failures += 1
            continue
        problems, class_changes = compare_styles(base, cand)
        pixels = compare_pixels(label)
        state = "OK " if not problems else "DIFF"
        if problems:
            failures += 1
        print("%-34s %s  styles: %d differences, %d elements re-classed  |  pixels: %s"
              % (label, state, len(problems), len(class_changes), pixels))
        for line in problems[:12]:
            print("      ! " + line)
        if len(problems) > 12:
            print("      ! ... %d more" % (len(problems) - 12))
    print("\n%d of %d states differ in resolved style." % (failures, len(labels)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
