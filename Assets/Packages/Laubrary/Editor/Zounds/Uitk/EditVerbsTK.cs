using System;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>The edit and playback verbs the Zounds editors offer on a waveform or a timeline.</summary>
    internal enum EditVerb {
        FitView, ZoomSelection, ZoomTrack,
        Follow, Ripple, Loop,
        PlayFromMarker, PlaySelection,
        Trim, Untrim, Split, Delete,
        Cut, Copy, Paste, Insert, DuplicateInsert, DuplicatePaste,
        SelectAll,
    }

    /// <summary>
    /// The one place an edit verb's face is defined (owner, 2026-10-10): its icon, its short label for a bar, its longer
    /// name for a menu, and its keyboard shortcut. The Zequence editor's timeline bar, the Klip editor's edit bar and the
    /// waveform's right-click menu all build their buttons and items here, so the same verb looks and reads the same
    /// everywhere. What a verb DOES stays with the window that offers it.
    /// </summary>
    internal static class EditVerbsTK {

        internal readonly struct Face {
            public readonly string icon, bar, menu, shortcut;
            /// <summary>Whether a bar shows the label beside the icon (false: icon only, the tooltip names it).</summary>
            public readonly bool labelled;
            public Face(string icon, string bar, string menu, string shortcut, bool labelled = true) {
                this.icon = icon; this.bar = bar; this.menu = menu; this.shortcut = shortcut; this.labelled = labelled;
            }
        }

        internal static Face Of(EditVerb v) {
            switch (v) {
                case EditVerb.FitView:         return new Face("arrows-out-line-horizontal", "Fit", "Fit", null, false);
                case EditVerb.ZoomSelection:   return new Face("magnifying-glass-plus", "Sel", "Zoom to selection", null, false);
                case EditVerb.ZoomTrack:       return new Face("corners-out", "Track", "Zoom to track", null, false);
                case EditVerb.Follow:          return new Face("arrow-fat-lines-right", "Follow", "Follow", null);
                case EditVerb.Ripple:          return new Face("arrows-in-line-horizontal", "Ripple", "Ripple", null);
                case EditVerb.Loop:            return new Face("repeat", "Loop", "Loop", null);
                case EditVerb.PlayFromMarker:  return new Face("play", "Here", "Play from marker", null);
                case EditVerb.PlaySelection:   return new Face("play-circle", "Audition", "Play selection", "Space");
                case EditVerb.Trim:            return new Face("crop", "Trim", "Trim to selection", "T");
                case EditVerb.Untrim:          return new Face("frame-corners", "Untrim", "Untrim", null);
                case EditVerb.Split:           return new Face("square-split-horizontal", "Split", "Split", "S");
                case EditVerb.Delete:          return new Face("trash", "Delete", "Delete", "Delete");
                case EditVerb.Cut:             return new Face("scissors", "Cut", "Cut", "Ctrl+X");
                case EditVerb.Copy:            return new Face("copy", "Copy", "Copy", "Ctrl+C");
                case EditVerb.Paste:           return new Face("clipboard-text", "Paste", "Paste", "Ctrl+V");
                case EditVerb.Insert:          return new Face("text-indent", "Insert", "Insert", "Ctrl+Shift+V");
                case EditVerb.DuplicateInsert: return new Face("stack-plus", "Dup insert", "Duplicate insert", "Ctrl+D");
                case EditVerb.DuplicatePaste:  return new Face("rows-plus-bottom", "Dup paste", "Duplicate paste", "Ctrl+Shift+D");
                case EditVerb.SelectAll:       return new Face("selection-all", "All", "Select all", "Ctrl+A");
                default: return new Face(null, v.ToString(), v.ToString(), null);
            }
        }

        const float IconSize = 12f, Pad = 5f, IconGap = 3f;

        /// <summary>A verb's button for a bar: its icon, then (when the verb is labelled) its short label; a fixed width
        /// measured from the label once it has its font, so nothing stretches and the row never wraps.</summary>
        internal static Button Button(EditVerb v, string tooltip, Action onClick, ZUICornerMask corners, float h) {
            var f = Of(v);
            var b = ZS.Button("", tooltip, "RichButton", onClick, corners, -1f, h);
            Dress(b, f, h);
            b.name = "verb-" + v;
            b.userData = v;
            return b;
        }

        /// <summary>A verb that is a switch (Follow, Ripple, Loop): the same face on a latching toggle.</summary>
        internal static ZuiToggleButton Toggle(EditVerb v, string tooltip, bool value, Action<bool> onChanged, ZUICornerMask corners, float h, Color onColour) {
            var f = Of(v);
            var t = ZS.Toggle("", tooltip, value, onChanged, "ZoundBtnFlatToggle", corners, -1f, h, onColour);
            Dress(t, f, h);
            t.name = "verb-" + v;
            t.userData = v;
            return t;
        }

        static void Dress(TextElement b, Face f, float h) {
            b.AddToClassList("zs-verb");
            var icon = Z.Icon(f.icon, IconSize);
            if (icon != null) { icon.AddToClassList("zs-verb__icon"); b.Add(icon); }
            if (!f.labelled) { b.AddToClassList("zs-verb--icon-only"); b.style.width = Mathf.Max(h + 4f, IconSize + 2f * Pad); return; }
            var l = new Label(f.bar) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zs-verb__label");
            b.Add(l);
            // A first width from the label's length, then the measured one once the label has its font.
            b.style.width = 2f * Pad + IconSize + IconGap + f.bar.Length * 6.2f;
            void Fit() {
                var sz = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (sz.x > 0f) b.style.width = Mathf.Ceil(2f * Pad + IconSize + IconGap + sz.x + 1f);
            }
            b.RegisterCallback<AttachToPanelEvent>(_ => b.schedule.Execute(Fit));
        }

        /// <summary>The verb as a menu item: its icon, its menu name and its shortcut; a greyed item still says why on hover.</summary>
        internal static ZuiMenu Item(this ZuiMenu menu, EditVerb v, bool enabled, string tooltip, Action onClick) {
            var f = Of(v);
            return menu.Item(f.menu, tooltip, onClick, false, enabled, f.icon, f.shortcut);
        }
    }
}
