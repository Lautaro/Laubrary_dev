// ZUIForm.cs
// Declarative form builder for ZUI.
// Build controls and rows as objects, compose into a form, then Draw() once.
// Label column auto-sizes to the widest label. Spacing is automatic.
//
// Usage:
//   var form = ZUI.Form("my_section");
//
//   // Simple labelled rows (shorthand — one control per row)
//   form.Add("Blur Radius", () => shadow.blur = EditorGUILayout.Slider(shadow.blur, 0, 20));
//   form.Add("Passes",      () => shadow.passes = EditorGUILayout.IntSlider(shadow.passes, 1, 20));
//
//   // Conditional rows
//   if (shadow.blur > 0)
//       form.Add("Quality", () => shadow.quality = EditorGUILayout.IntSlider(...));
//
//   // Complex row — multiple controls side by side
//   var offsetRow = ZUI.Row("Offset");
//   offsetRow.Add(ZUI.Control(60f, () => shadow.x = EditorGUILayout.FloatField(shadow.x)));
//   offsetRow.Add(ZUI.Control(60f, () => shadow.y = EditorGUILayout.FloatField(shadow.y)));
//   form.Add(offsetRow);
//
//   // Draw everything
//   bool changed = form.Draw();

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ── Control ──────────────────────────────────────────────────────────────────
// An atomic draw action, optionally with a fixed width. Flex if no width set.

public class ZUIControl
{
    internal float? fixedWidth;
    internal Action draw;

    public ZUIControl(Action draw, float? width = null)
    {
        this.draw = draw;
        this.fixedWidth = width;
    }
}

// ── Row ──────────────────────────────────────────────────────────────────────
// A labelled horizontal group of one or more controls.

public class ZUIRow
{
    internal string label;
    internal bool narrowLabel;
    internal List<ZUIControl> controls = new List<ZUIControl>();

    public ZUIRow(string label = "", bool narrow = false)
    {
        this.label = label;
        this.narrowLabel = narrow;
    }

    public ZUIRow Add(ZUIControl control) { controls.Add(control); return this; }
    public ZUIRow Add(Action draw) { controls.Add(new ZUIControl(draw)); return this; }
    public ZUIRow Add(float width, Action draw) { controls.Add(new ZUIControl(draw, width)); return this; }
    public ZUIRow Add(params ZUIControl[] ctrls) { controls.AddRange(ctrls); return this; }

    // Typed control support
    public ZUIRow Add(IZUIControl ctrl) { controls.Add(new ZUIControl(() => ctrl.Draw())); return this; }
    public ZUIRow Add(float width, IZUIControl ctrl) { controls.Add(new ZUIControl(() => ctrl.Draw(), width)); return this; }
}

// ── Form ─────────────────────────────────────────────────────────────────────
// A vertical sequence of rows. Draw() measures labels and renders everything.

public class ZUIForm
{
    string _spacingScale = "V Section Rows";
    float? _labelWidthOverride;
    List<FormEntry> _entries = new List<FormEntry>();

    internal enum EntryType { Row, Header, Gap, Custom }

    internal struct FormEntry
    {
        public EntryType type;
        public ZUIRow row;           // for Row entries
        public string headerText;    // for Header entries
        public Action customDraw;    // for Custom entries
    }

    // ── Configuration ────────────────────────────────────────────────────

    public ZUIForm Spacing(string scaleName) { _spacingScale = scaleName; return this; }
    public ZUIForm LabelWidth(float width) { _labelWidthOverride = width; return this; }

    // ── Adding entries ───────────────────────────────────────────────────

    /// <summary>Add a pre-built row.</summary>
    public ZUIForm Add(ZUIRow row)
    {
        _entries.Add(new FormEntry { type = EntryType.Row, row = row });
        return this;
    }

    /// <summary>Shorthand: add a simple labelled row with one flex control.</summary>
    public ZUIForm Add(string label, Action draw)
    {
        var row = new ZUIRow(label);
        row.Add(draw);
        return Add(row);
    }

    /// <summary>Shorthand: add a simple labelled row with one fixed-width control.</summary>
    public ZUIForm Add(string label, float controlWidth, Action draw)
    {
        var row = new ZUIRow(label);
        row.Add(controlWidth, draw);
        return Add(row);
    }

    /// <summary>Shorthand: add a labelled row with a ZUIControl.</summary>
    public ZUIForm Add(string label, ZUIControl ctrl)
    {
        var row = new ZUIRow(label);
        row.Add(ctrl);
        return Add(row);
    }

    /// <summary>Shorthand: add a labelled row with a typed control.</summary>
    public ZUIForm Add(string label, IZUIControl ctrl)
    {
        var row = new ZUIRow(label);
        row.Add(ctrl);
        return Add(row);
    }

    /// <summary>Shorthand: add a labelled row with a fixed-width typed control.</summary>
    public ZUIForm Add(string label, float controlWidth, IZUIControl ctrl)
    {
        var row = new ZUIRow(label);
        row.Add(controlWidth, ctrl);
        return Add(row);
    }

    /// <summary>Insert a bold header label (full-width).</summary>
    public ZUIForm Header(string title)
    {
        _entries.Add(new FormEntry { type = EntryType.Header, headerText = title });
        return this;
    }

    /// <summary>Insert an extra vertical gap.</summary>
    public ZUIForm Gap()
    {
        _entries.Add(new FormEntry { type = EntryType.Gap });
        return this;
    }

    /// <summary>Insert a full-width custom draw action (no label column).</summary>
    public ZUIForm Custom(Action draw)
    {
        _entries.Add(new FormEntry { type = EntryType.Custom, customDraw = draw });
        return this;
    }

    // ── Draw ─────────────────────────────────────────────────────────────

    /// <summary>Measures labels, draws all entries with auto-spacing. Returns true if anything changed.</summary>
    public bool Draw()
    {
        if (_entries.Count == 0) return false;

        // Measure widest non-narrow label
        float labelW;
        if (_labelWidthOverride.HasValue)
        {
            labelW = _labelWidthOverride.Value;
        }
        else
        {
            labelW = 0f;
            var measureStyle = EditorStyles.label;
            foreach (var e in _entries)
            {
                if (e.type != EntryType.Row || e.row == null) continue;
                if (e.row.narrowLabel || string.IsNullOrEmpty(e.row.label)) continue;
                float w = measureStyle.CalcSize(new GUIContent(e.row.label)).x + 6f;
                if (w > labelW) labelW = w;
            }
            labelW = Mathf.Clamp(labelW, ZUI.LabelWidthNarrow, 200f);
        }

        EditorGUI.BeginChangeCheck();

        bool first = true;
        foreach (var e in _entries)
        {
            switch (e.type)
            {
                case EntryType.Row:
                    if (!first) ZUI.VerticalSpace(_spacingScale);
                    DrawRow(e.row, labelW);
                    first = false;
                    break;

                case EntryType.Header:
                    if (!first) ZUI.VerticalSpace(_spacingScale);
                    EditorGUILayout.LabelField(e.headerText, EditorStyles.boldLabel);
                    first = false;
                    break;

                case EntryType.Gap:
                    ZUI.VerticalSpace(_spacingScale);
                    break;

                case EntryType.Custom:
                    if (!first) ZUI.VerticalSpace(_spacingScale);
                    e.customDraw?.Invoke();
                    first = false;
                    break;
            }
        }

        return EditorGUI.EndChangeCheck();
    }

    void DrawRow(ZUIRow row, float autoLabelW)
    {
        if (row == null || row.controls.Count == 0) return;

        GUILayout.BeginHorizontal();

        // Label — MiddleLeft aligned so it centers with taller controls
        if (!string.IsNullOrEmpty(row.label))
        {
            float w = row.narrowLabel ? ZUI.LabelWidthNarrow : autoLabelW;
            EditorGUILayout.LabelField(row.label, ZUI.RowLabelStyle, GUILayout.Width(w));
        }

        // Controls with auto horizontal spacing
        for (int i = 0; i < row.controls.Count; i++)
        {
            if (i > 0) ZUI.HorizontalSpace("H Control Gap");

            var ctrl = row.controls[i];
            if (ctrl.fixedWidth.HasValue)
            {
                GUILayout.BeginVertical(GUILayout.Width(ctrl.fixedWidth.Value));
                ctrl.draw?.Invoke();
                GUILayout.EndVertical();
            }
            else
            {
                ctrl.draw?.Invoke();
            }
        }

        GUILayout.EndHorizontal();
    }
}

// ── ZUI entry points ─────────────────────────────────────────────────────────

public static partial class ZUI
{
    /// <summary>Create a new form builder.</summary>
    public static ZUIForm Form(string id = null) => new ZUIForm();

    /// <summary>Create a new row with a label.</summary>
    public static ZUIRow Row(string label = "") => new ZUIRow(label);

    /// <summary>Create a new row with a narrow label.</summary>
    public static ZUIRow RowNarrow(string label) => new ZUIRow(label, narrow: true);

    /// <summary>Create a flex-width control.</summary>
    public static ZUIControl Control(Action draw) => new ZUIControl(draw);

    /// <summary>Create a fixed-width control.</summary>
    public static ZUIControl Control(float width, Action draw) => new ZUIControl(draw, width);
}
