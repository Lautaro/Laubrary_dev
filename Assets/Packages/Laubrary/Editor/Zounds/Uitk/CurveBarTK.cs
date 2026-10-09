using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The one curve toolbar (owner's request 2026-10-08): a small strip of [Trim] [Time ✎ 👁] [Pitch ✎ 👁] [Vol ✎ 👁], used
    /// unchanged by the Klip editor over its waveform and by every local track in the Zequence editor. Per curve: the
    /// name latches the curve ON (it plays), the pencil selects it for editing (one at a time; the others step back as
    /// backdrops), the eye shows or hides its drawing. Each curve's three chips wear the colour the curve is drawn in
    /// (the Settings tab's), and Trim wears the trim handles' colour, so a chip and its line are told apart at a glance.
    /// The host supplies the reads and writes; this strip only draws the state and relays clicks, so both windows behave
    /// the same by construction.
    /// </summary>
    internal sealed class CurveBarTK : VisualElement {

        public const float H = 16f, NameW = 36f, IconW = 18f, TrimW = 34f;

        public sealed class Curve {
            public string label;
            public Color colour;
            /// <summary>Whether the curve plays (its modifier is on). Null reads as "no curve yet": enabling creates it.</summary>
            public Func<bool> enabled;
            public Action<bool> setEnabled;
            /// <summary>The eye: drawn or hidden (a view setting, not saved with the sound).</summary>
            public Func<bool> shown;
            public Action<bool> setShown;
            /// <summary>A warning shown beside the name while true (the pitch curve still on its old scale), with its text.</summary>
            public Func<bool> warn;
            public string warnTip;
            /// <summary>Right-click on the name: the host's settings for this curve (the volume curve's range).</summary>
            public Action<VisualElement> onContext;
            internal ZuiToggleButton enable, edit, eye;
            internal Image pencil;
            internal Label warnMark;
        }

        readonly List<Curve> curves = new List<Curve>();
        readonly ZuiToggleButton trim;
        readonly Color trimColour;
        readonly Func<bool> trimOn;
        readonly Func<int> selected;
        readonly Action<int> select;
        readonly Func<string> trimTip;

        /// <param name="trimOn">The trim switch, or null for no Trim segment.</param>
        /// <param name="selected">Which curve is selected for editing (its index in <paramref name="curves"/>), or -1.</param>
        /// <param name="select">Select a curve for editing, or -1 to select none.</param>
        /// <param name="trimColour">The colour the Trim chip wears (the trim handles' colour), or null for the skin's own.</param>
        public CurveBarTK(List<Curve> curves, Func<bool> trimOn, Action<bool> setTrim, Func<string> trimTip, Func<int> selected, Action<int> select, Color? trimColour = null) {
            this.curves.AddRange(curves);
            this.trimOn = trimOn; this.selected = selected; this.select = select; this.trimTip = trimTip;
            this.trimColour = trimColour ?? Color.white;
            AddToClassList("zs-curvebar");
            if (trimOn != null) {
                trim = ZS.Toggle("Trim", "", trimOn(), v => setTrim(v), "RichToggle", ZUICornerMask.All, TrimW, H);
                trim.AddToClassList("zs-curvebar__toggle");
                if (trimColour.HasValue) trim.AddToClassList("zs-curvebar__toggle--curve");
                Add(trim);
                Add(Gap(4f));
            }
            for (int i = 0; i < this.curves.Count; i++) {
                var c = this.curves[i];
                int index = i;
                c.enable = ZS.Toggle(c.label, "", c.enabled(), v => c.setEnabled(v), "RichToggle", ZUICornerMask.Left, NameW, H);
                if (c.onContext != null) c.enable.RegisterCallback<PointerDownEvent>(e => { if (e.button != 1) return; e.StopPropagation(); c.onContext(c.enable); });
                c.edit = ZS.Toggle("", "", false, v => select(v ? index : -1), "RichToggle", ZUICornerMask.None, IconW, H);
                c.edit.markWhenOn = false;
                c.pencil = new Image { image = AudioSpectrumView.editIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                c.pencil.AddToClassList("zs-curvebar__pencil");
                c.edit.Add(c.pencil);
                c.eye = ZS.Eye(c.shown(), v => "", v => c.setShown(v), ZUICornerMask.Right, IconW, H, 11f);
                foreach (var t in new[] { c.enable, c.edit, c.eye }) { t.AddToClassList("zs-curvebar__toggle"); t.AddToClassList("zs-curvebar__toggle--curve"); }
                Add(c.enable); Add(c.edit); Add(c.eye);
                if (c.warn != null) {
                    c.warnMark = new Label("⚠") { tooltip = c.warnTip };
                    c.warnMark.AddToClassList("zs-lbl"); c.warnMark.AddToClassList("zs-warnmark"); c.warnMark.AddToClassList("zs-curvebar__warn");
                    Add(c.warnMark);
                }
                if (i < this.curves.Count - 1) Add(Gap(4f));
            }
            Sync();
        }

        static VisualElement Gap(float w) { var g = new VisualElement(); g.style.width = w; g.AddToClassList("zs-curvebar__gap"); return g; }

        /// <summary>
        /// One look for every chip, in its curve's colour: off = the colour as outline and text on the skin's dark face;
        /// on = a translucent fill of the colour with light text (the sheet turns the skin's orange "on" picture off).
        /// The colour is data (the Settings tab's), so it is set here rather than in the sheet.
        /// </summary>
        static void Tint(ZuiToggleButton t, Color c, bool on) {
            var border = on ? c : new Color(c.r, c.g, c.b, 0.6f);
            t.style.borderTopColor = t.style.borderRightColor = t.style.borderBottomColor = t.style.borderLeftColor = border;
            t.style.backgroundColor = on ? (StyleColor)new Color(c.r, c.g, c.b, 0.42f) : StyleKeyword.Null;
            t.style.color = on ? StyleKeyword.Null : (StyleColor)c;
            // The icon inside (the pencil image, or the eye's background picture) in the same colour.
            foreach (var child in t.Children()) {
                if (child is Image img) img.tintColor = c;
                else if (child.ClassListContains("zs-eye__icon")) child.style.unityBackgroundImageTintColor = c;
            }
        }

        /// <summary>Brings every segment up to date with the host's state (call after any change, and on the host's tick).</summary>
        public void Sync() {
            if (trim != null) {
                bool t = trimOn();
                trim.SetValueWithoutNotify(t); trim.tooltip = trimTip != null ? trimTip() : "";
                if (trim.ClassListContains("zs-curvebar__toggle--curve")) Tint(trim, trimColour, t);
            }
            int sel = selected();
            for (int i = 0; i < curves.Count; i++) {
                var c = curves[i];
                bool on = c.enabled(), editing = sel == i, shown = c.shown();
                c.enable.SetValueWithoutNotify(on);
                c.edit.SetValueWithoutNotify(editing);
                c.edit.SetEnabled(on);
                c.eye.SetEnabled(on);
                ZS.SetEye(c.eye, shown);
                Tint(c.enable, c.colour, on);
                Tint(c.edit, c.colour, on && editing);
                Tint(c.eye, c.colour, on && shown);
                string what = c.label.ToLowerInvariant() == "vol" ? "volume" : c.label.ToLowerInvariant();
                c.enable.tooltip = (on ? "The " + what + " curve is on: it shapes every play. Click to bypass it."
                                       : "The " + what + " curve is off. Click to switch it on.")
                                 + (c.onContext != null ? "\n\nRight-click: its settings (the extra time it keeps going after the audio; for the volume curve, its range)." : "");
                c.edit.tooltip = !on ? "Switch the " + what + " curve on first."
                               : editing ? "Editing the " + what + " curve: drag its points, click its line (or double-click anywhere) to add one, double-click a point to remove it, right-click a point for its random range. The other curves step back behind it. Click to stop editing."
                                         : "Edit the " + what + " curve here: shows its points and puts the other curves behind it.";
                c.eye.tooltip = !on ? "Switch the " + what + " curve on first."
                              : shown ? "The " + what + " curve is drawn. Click to hide its drawing (it still plays)."
                                      : "The " + what + " curve is hidden. Click to draw it.";
                if (c.warnMark != null) c.warnMark.style.visibility = c.warn() ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }
}
