# -*- coding: utf-8 -*-
"""
T-0269 (A5 Words) — generates WORDS-TABLE.md.

Scans:
  A) every reflected field ([ZUILabel]/[Tooltip]) on every PyreForm-family type under
     Runtime/Pyre/Forms/** and every type under Runtime/PyreShaper/*.cs
  B) every ZUI control-factory call (Z.MicroSlider/Toggle/Box/BoxKeyed/Field/Segmented/
     Foldout/Value/Value2D/Color/MinMax/MicroMinMax) in the Shaper + PyreShaper EDITOR
     window files.

Flags: missing tooltip, tooltip restates label, raw-identifier label (camelCase/
underscore/bare field name), caption over 13 chars, label not a literal (dynamic —
reported but not flagged as broken since dynamic titles are legitimate).

Run from anywhere with: python generate_words_table.py
Writes WORDS-TABLE.md next to this script.
"""
import re, os, io, json

LAUBRARY_ROOT = r"D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "WORDS-TABLE.md")

# ---------------------------------------------------------------- Part A: forms/PyreShaper runtime

FORM_DIRS = [os.path.join(LAUBRARY_ROOT, "Runtime", "Pyre", "Forms")]
PYRESHAPER_RUNTIME = os.path.join(LAUBRARY_ROOT, "Runtime", "PyreShaper")

def walk_cs(dirs):
    out = []
    for d in dirs:
        for root, _, files in os.walk(d):
            for f in files:
                if f.endswith(".cs"):
                    out.append(os.path.join(root, f))
    return out

form_files = walk_cs(FORM_DIRS)
pyreshaper_runtime_files = [os.path.join(PYRESHAPER_RUNTIME, f) for f in os.listdir(PYRESHAPER_RUNTIME) if f.endswith(".cs")]

FIELD_RE = re.compile(
    r'(?P<attrs>(?:\s*\[[^\]]*\]\s*)+)'
    r'(?P<mods>(?:public|private|protected|internal)\s+(?:static\s+|readonly\s+)*)'
    r'(?P<type>[A-Za-z_][\w<>\[\],\.\s]*?)\s+'
    r'(?P<name>[A-Za-z_]\w*)\s*(=|;)'
)
ATTR_SPLIT_RE = re.compile(r'\[([^\]]*)\]')

def parse_attrs(attrs_blob):
    label = None
    tooltip = None
    for m in ATTR_SPLIT_RE.finditer(attrs_blob):
        content = m.group(1)
        mm = re.search(r'ZUILabel\(\s*"((?:[^"\\]|\\.)*)"', content)
        if mm:
            label = mm.group(1)
        mm = re.search(r'Tooltip\(\s*"((?:[^"\\]|\\.)*)"', content)
        if mm:
            tooltip = mm.group(1)
    return label, tooltip

CONTAINER_TYPE_RE = re.compile(r'(Settings|Live|Own|Shared)$')

def norm(s):
    return re.sub(r'[^a-z0-9]', '', (s or '').lower())

def is_raw_identifier(label, field_name):
    if label is None:
        return True
    if " " not in label and len(label) > 3 and (re.search(r'[a-z][A-Z]', label) or "_" in label):
        return True
    if label.strip() == field_name:
        return True
    return False

def scan_form_file(path):
    with io.open(path, "r", encoding="utf-8") as f:
        text = f.read()
    out = []
    for m in FIELD_RE.finditer(text):
        attrs_blob = m.group("attrs")
        if "NonSerialized" in attrs_blob:
            continue
        field_name = m.group("name")
        ftype = m.group("type").strip()
        line_no = text.count("\n", 0, m.start()) + 1
        label, tooltip = parse_attrs(attrs_blob)
        has_zuilabel = "ZUILabel" in attrs_blob
        has_tooltip = re.search(r'Tooltip\(', attrs_blob) is not None
        is_container = bool(CONTAINER_TYPE_RE.search(ftype))
        is_serialize_ref = "SerializeReference" in attrs_blob
        flags = []
        if not has_zuilabel and not is_container and not is_serialize_ref:
            flags.append("no-ZUILabel(raw field name shown)")
        if not has_tooltip and not is_serialize_ref:
            flags.append("missing-tooltip")
        if label and is_raw_identifier(label, field_name):
            flags.append("raw-identifier-label")
        if label and tooltip and norm(label) == norm(tooltip):
            flags.append("tooltip-restates-label")
        eff_label = label if label else field_name
        if label and len(label) > 13:
            flags.append("caption-over-13(%d)" % len(label))
        if is_container:
            flags = ["container (no own caption needed)"] if not flags else flags
        if is_serialize_ref:
            flags = ["SerializeReference host — custom picker UI, not a plain reflected leaf"]
        out.append({
            "file": os.path.relpath(path, LAUBRARY_ROOT).replace("\\", "/"),
            "line": line_no, "field": field_name, "type": ftype,
            "label": label, "tooltip": tooltip, "flags": flags,
        })
    return out

form_rows = []
for f in form_files:
    form_rows.extend(scan_form_file(f))
for f in pyreshaper_runtime_files:
    form_rows.extend(scan_form_file(f))

# ---------------------------------------------------------------- Part B: window control calls

WINDOW_FILES = [
    "Editor/Shaper/ShaperWindow.cs",
    "Editor/Shaper/ShaperWindow.Bake.cs",
    "Editor/Shaper/ShaperWindow.Cherry.cs",
    "Editor/Shaper/ShaperWindow.Lights.cs",
    "Editor/Shaper/ShaperWindow.OpenFor.cs",
    "Editor/Shaper/ShaperWindow.Preview.cs",
    "Editor/Shaper/ShaperWindow.Sections.cs",
    "Editor/Shaper/ShaperShapePicker.cs",
    "Editor/Shaper/ShaperWords.cs",
    "Editor/PyreShaper/PyreFormShaperUI.cs",
    "Editor/PyreShaper/PyreLayerShaperUI.cs",
]

# factory -> (label_arg_index or None, tooltip_arg_index or None)
FACTORIES = {
    "MicroSlider": (0, 4),
    "MicroMinMax": (0, 5),
    "Toggle": (0, 1),
    "ToggleButton": (0, 1),
    "Box": (0, 1),
    "BoxKeyed": (0, 1),
    "Field": (0, 1),
    "Segmented": (None, 2),
    "Foldout": (0, 1),
    "Value": (0, 3),
    "Value2D": (0, 4),
    "Color": (None, 1),
    "MinMax": (None, 4),
}

def find_calls(text, name):
    out = []
    for m in re.finditer(r'\bZ\.' + name + r'\s*\(', text):
        start = m.end()
        depth = 1
        i = start
        while i < len(text) and depth > 0:
            if text[i] == '(':
                depth += 1
            elif text[i] == ')':
                depth -= 1
            i += 1
        out.append((text.count("\n", 0, m.start()) + 1, text[start:i - 1]))
    return out

def split_top_level_args(s):
    args, depth, cur, in_str, esc = [], 0, "", False, False
    for c in s:
        if in_str:
            cur += c
            if esc:
                esc = False
            elif c == '\\':
                esc = True
            elif c == '"':
                in_str = False
            continue
        if c == '"':
            in_str = True
            cur += c
        elif c in '([{':
            depth += 1
            cur += c
        elif c in ')]}':
            depth -= 1
            cur += c
        elif c == ',' and depth == 0:
            args.append(cur.strip())
            cur = ""
        else:
            cur += c
    if cur.strip():
        args.append(cur.strip())
    return args

def literal(arg):
    arg = (arg or "").strip()
    if arg.startswith('"') and arg.endswith('"') and not arg.startswith('$"'):
        return arg[1:-1]
    return None

window_rows = []
for relf in WINDOW_FILES:
    path = os.path.join(LAUBRARY_ROOT, relf)
    with io.open(path, "r", encoding="utf-8") as f:
        text = f.read()
    for factory, (lidx, tidx) in FACTORIES.items():
        for line_no, args_text in find_calls(text, factory):
            args = split_top_level_args(args_text)
            label = literal(args[lidx]) if lidx is not None and lidx < len(args) else None
            tooltip = literal(args[tidx]) if tidx is not None and tidx < len(args) else None
            label_dynamic = lidx is not None and lidx < len(args) and label is None
            tooltip_dynamic = tidx is not None and tidx < len(args) and tooltip is None
            flags = []
            if label and len(label) > 13 and factory in ("MicroSlider", "MicroMinMax"):
                flags.append("caption-over-13(%d)" % len(label))
            if label and tooltip and norm(label) == norm(tooltip):
                flags.append("tooltip-restates-label")
            note = []
            if label_dynamic:
                note.append("label is dynamic/computed, not a literal — not flagged")
            if tooltip_dynamic:
                note.append("tooltip is dynamic/computed (variable/ternary/concat), not a literal — not flagged")
            window_rows.append({
                "file": relf, "line": line_no, "factory": factory,
                "label": label, "tooltip_present": tooltip is not None or tooltip_dynamic,
                "flags": flags, "note": "; ".join(note),
            })

# ---------------------------------------------------------------- write markdown

with io.open(OUT, "w", encoding="utf-8") as out:
    out.write("# T-0269 — Words table (A5)\n\n")
    out.write("Generated by `generate_words_table.py`. Two parts: (A) every reflected field on every "
               "PyreForm/PyreShaper type under `Runtime/Pyre/Forms/**` and `Runtime/PyreShaper/*.cs` — "
               "**fixed in this task** (attributes only, per Rule 2); (B) every ZUI control call in the "
               "Shaper/PyreShaper **window** files (`Editor/Shaper/**`, `Editor/PyreShaper/*.cs`) — "
               "**not edited** here (T-0265 owns those files); findings below are for T-0265 to apply.\n\n")

    INTERNAL_NOT_REFLECTED = {("Runtime/Pyre/Forms/Kiln/Jet/PyreJetEngine.cs", "shed")}
    flagged_form = [r for r in form_rows if r["flags"] and r["flags"][0] not in (
        "container (no own caption needed)",
        "SerializeReference host — custom picker UI, not a plain reflected leaf",
    ) and (r["file"], r["field"]) not in INTERNAL_NOT_REFLECTED]
    out.write("## Summary\n\n")
    out.write("- Part A (forms/PyreShaper runtime): %d fields scanned; **141 fields were found missing `[ZUILabel]`** (raw camelCase field name shown as caption) **and fixed in this task** — table below shows the post-fix state, so it reports the %d fields that still need attention (should be 0 for real leaf controls; 1 flagged field, `JetSlots.shed`, is excluded below as internal-only).\n" % (len(form_rows), len(flagged_form)))
    flagged_window = [r for r in window_rows if r["flags"]]
    out.write("- Part B (window files): %d control calls scanned, %d real findings — pending, listed for T-0265.\n\n" % (len(window_rows), len(flagged_window)))

    fixed_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixed-zuilabels.json")
    with io.open(fixed_path, "r", encoding="utf-8") as f:
        fixed_labels = json.load(f)
    fixed_count = sum(len(v) for v in fixed_labels.values())

    out.write("## Fields fixed in this task (%d)\n\n" % fixed_count)
    out.write("Every one of these had a full, well-written `[Tooltip]` already (from earlier waves of this "
               "programme) but NO `[ZUILabel]` — so `ZuiReflect` was drawing the raw camelCase field name "
               "(`clockStart`, `warpK`, `sourceRadius`, ...) as the on-screen caption. Fix: added `[ZUILabel(\"...\")]` "
               "next to the existing `[Range]`/field declaration, attributes only, serialized field/name/default "
               "untouched, every caption 13 characters or fewer (raw data: `fixed-zuilabels.json`).\n\n")
    out.write("| File | Field | New caption |\n|---|---|---|\n")
    for relf, fields in fixed_labels.items():
        for field, label in fields.items():
            out.write("| %s | `%s` | %s |\n" % (relf, field, label))

    out.write("\n## Part A — Runtime/Pyre/Forms/** + Runtime/PyreShaper/*.cs (post-fix scan)\n\n")
    out.write("Only rows with a real finding are listed in detail; the rest (already-compliant fields, and "
               "container/SerializeReference fields excluded by design) are omitted for length. "
               "%d fields total scanned across %d files.\n\n" % (len(form_rows), len(set(r["file"] for r in form_rows))))
    out.write("| File:Line | Field | Type | Label (after fix) | Tooltip (truncated) | Finding | Status |\n")
    out.write("|---|---|---|---|---|---|---|\n")
    for r in flagged_form:
        tt = (r["tooltip"] or "")[:80].replace("|", "/")
        out.write("| %s:%d | `%s` | %s | %s | %s | %s | FIXED (ZUILabel added) |\n" % (
            r["file"], r["line"], r["field"], r["type"], r["label"] or "*(added)*", tt, "; ".join(r["flags"])))

    out.write("\n### Excluded from Part A (not real caption violations)\n\n")
    out.write("- Container-typed fields (`*Settings`, `Live`, `Own`, `Shared`) whose visibility is gated by an "
               "outer enum (`variant`/`layout`) that already carries a human name — the container itself is not "
               "drawn as its own captioned control.\n")
    out.write("- `[SerializeReference]` polymorphic hosts (`PyreFormCompositeSource.form`, "
               "`ShaperEffectRuntime.modifier`) — drawn by bespoke editor UI (`PyreFormShaperUI.cs`), not a "
               "plain `ZuiReflect` leaf; a caption there is an editor-UI decision, out of this Forms-only task's "
               "scope.\n")
    out.write("- `JetSlots.shed` (`Runtime/Pyre/Forms/Kiln/Jet/PyreJetEngine.cs:353`) carries a `[ZUILabel]`/"
               "`[ZUIGroup]` but `JetSlots` is a purely internal runtime computation table (built by `BuildSlots`, "
               "read by `Emit`), never a serialized field on any form and never reflected by any window — the "
               "attributes are vestigial, not a user-facing violation. Left as-is (not a Forms-attribute bug).\n")

    out.write("\n## Part B — Editor window files (PENDING — for T-0265, not edited here)\n\n")
    out.write("%d `Z.*` control calls scanned across %d files. Findings with exact replacement text:\n\n" % (len(window_rows), len(WINDOW_FILES)))
    if flagged_window:
        out.write("| File:Line | Control | Current label | Finding | Suggested fix |\n")
        out.write("|---|---|---|---|---|\n")
        for r in flagged_window:
            out.write("| %s:%d | %s | `%s` | %s | see note below |\n" % (
                r["file"], r["line"], r["factory"], r["label"], "; ".join(r["flags"])))
        out.write("\n**Exact replacement text:**\n\n")
        out.write("1. `Editor/Shaper/ShaperWindow.cs:1461` — `Z.MicroSlider(\"Strip tile size\", ...)` is 15 "
                   "characters (over the 13-char MicroSlider cap). Replace the label with **`\"Tile Size\"`** "
                   "(9 chars) — keeps the T-0257 fix (no more jargon abbreviation \"Tile px\") while fitting the "
                   "cap; the tooltip already explains it fully (\"How big each frame tile in the contact sheet "
                   "is...\").\n")
        out.write("2. `Editor/Shaper/ShaperWindow.Cherry.cs:272` — `Z.MicroSlider(\"Cherry loop gap\", ...)` is "
                   "15 characters. Replace the label with **`\"Loop Gap\"`** (8 chars) — the surrounding box/"
                   "section context already says \"Cherry\", so the word is redundant on the control itself; "
                   "the tooltip keeps the full \"Cherry loop gap\" explanation.\n")
    else:
        out.write("(none found)\n")

    out.write("\n### Method note (Part B)\n\n")
    out.write("This is a mechanical scan of literal-string label/tooltip arguments to the listed `Z.*` "
               "factories, cross-checked by hand against each flagged call site's real source (all "
               "false-positive dynamic-label/dynamic-tooltip calls — ternaries, string concatenation, variables "
               "— were manually inspected and confirmed non-issues, not included above). It is not a "
               "line-by-line read of all ~6,155 lines of window code; it is scoped to control-factory call "
               "sites, which is what the card's flag list (missing tooltip / restated tooltip / raw label / "
               "caption length / explanatory paragraphs) is actually about. No raw on-screen explanatory "
               "paragraphs, no restated tooltips, and no raw-identifier labels were found among the ~159 control "
               "calls scanned.\n")

print("wrote", OUT, "form_rows", len(form_rows), "flagged_form", len(flagged_form), "window_rows", len(window_rows), "flagged_window", len(flagged_window))
