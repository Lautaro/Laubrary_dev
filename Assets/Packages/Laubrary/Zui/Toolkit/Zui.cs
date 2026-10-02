// Zui — the UI Toolkit half of ZUI (retained-mode VisualElement controls; the IMGUI half is the
// global-namespace ZUI class, kept fully working while tools migrate one by one).
//
// Design rules carried over from the IMGUI era (see the laubrary skill's ui-layout-rules.md):
//  • every control factory REQUIRES a tooltip parameter — no bare labels, by construction;
//  • no control is left to stretch: BaseFields get flex-grow:0 from ZuiToolkit.uss, and factories
//    set an explicit default width the caller can override with .W(px);
//  • labels pair with controls via Zui.Field (a FieldWrap), sidestepping BaseField's inconsistent
//    built-in label widths;
//  • drag-computed numeric values are rounded to 5 decimals at the source so float noise never
//    reaches a display.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public enum ZuiText { Body, Section, Small, Subtle }

    // NOTE the class is `Z`, not `Zui` — a class named the same as its containing namespace's last
    // segment (Laubrary.Zui.Zui) gets shadowed by the namespace inside every sibling Laubrary.*
    // namespace, making `Zui.Button(...)` unresolvable exactly where it's most used.
    public static class Z
    {
        // ── stylesheet ──────────────────────────────────────────────────────────────
        static StyleSheet _sheet;
        static readonly string[] PresentationSheetNames = { "ZuiPilotStandard", "ZuiPilotBands", "ZuiPilotEnvelope", "ZuiSharedEnvelope", "ZuiPresentation", "ZuiFoundationContainers", "ZuiFoundationFields", "ZuiFoundationLayout", "ZuiFoundationFlow", "ZuiFoundationAssetBrowser", "ZuiFoundationToolShell", "ZuiFieldPresentation", "ZuiSharedTheme", "ZuiButtonSurface" };
        static readonly System.Collections.Generic.Dictionary<string, StyleSheet> PresentationSheets = new System.Collections.Generic.Dictionary<string, StyleSheet>();

        /// The shared ZuiToolkit.uss, located by search so the path works both in this dev host
        /// (Assets/Packages/Laubrary/...) and in consumers (Packages/com.lautaro.arino.laubrary/...).
        public static StyleSheet Sheet
        {
            get
            {
                if (_sheet != null) return _sheet;
                foreach (string guid in AssetDatabase.FindAssets("ZuiToolkit t:StyleSheet"))
                {
                    var s = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (s != null) { _sheet = s; break; }
                }
                if (_sheet == null) Debug.LogError("[Zui] ZuiToolkit.uss not found in the project.");
                return _sheet;
            }
        }

        /// Attach the shared stylesheet + root class to a window/popup root. ZuiWindow does this
        /// automatically; call it yourself only for roots Zui doesn't own (popups, inspectors).
        public static void Attach(VisualElement root)
        {
            var s = Sheet;
            if (s != null && !root.styleSheets.Contains(s)) root.styleSheets.Add(s);
            root.AddToClassList("zui-root");
            // Resolve beside the shared sheet so both development and installed-package paths work.
            if (s != null)
            {
                string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(s)).Replace('\\', '/');
                foreach (string name in PresentationSheetNames)
                {
                    if (!PresentationSheets.TryGetValue(name, out var presentation) || presentation == null)
                    {
                        presentation = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "/" + name + ".uss");
                        if (presentation != null) PresentationSheets[name] = presentation;
                    }
                    if (presentation != null && !root.styleSheets.Contains(presentation)) root.styleSheets.Add(presentation);
                }
            }
            // Host-owned design overrides ship independently of the reusable package.
            var overrides = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/LaubraryUI.uss");
            if (overrides != null && !root.styleSheets.Contains(overrides)) root.styleSheets.Add(overrides);
        }

        /// <summary>
        /// Tags a tool window's root with its own class, so a later sheet can restyle that one tool's controls
        /// through ordinary selector specificity (`.lau-tool-lathe .zui-slider { … }`) without the controls
        /// themselves ever knowing which tool they are in. Safe to call on every rebuild.
        /// </summary>
        /// <param name="root">The window's root element.</param>
        /// <param name="tool">The tool's own name, lower case, without the `lau-tool-` prefix.</param>
        public static void AttachTool(VisualElement root, string tool)
        {
            if (root == null || string.IsNullOrEmpty(tool)) return;
            string cls = "lau-tool-" + tool;
            if (!root.ClassListContains(cls)) root.AddToClassList(cls);
        }

        // ── containers ──────────────────────────────────────────────────────────────

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            foreach (var c in children) if (c != null) row.Add(c);
            return row;
        }

        public static VisualElement Column(params VisualElement[] children)
        {
            var col = new VisualElement();
            col.AddToClassList("zui-column");
            foreach (var c in children) if (c != null) col.Add(c);
            return col;
        }

        /// The standard tool-window shape: controls on the LEFT, workspace/preview on the RIGHT, with a
        /// user-draggable divider whose position persists per `stateKey` (EditorPrefs, machine-local view
        /// state). Wraps UITK's TwoPaneSplitView; the left pane is the fixed one.
        ///
        /// DOUBLE-CLICK THE DIVIDER TO RESET the left pane to `initialLeftWidth` and forget the saved
        /// position. That route is not a nicety: because the width persists, a COLD OPEN is not a first run —
        /// a window reopened from its menu item with every prior instance closed still comes up at whatever
        /// extreme the divider was last dragged to, and without this there is no way back to the width the
        /// tool was designed at.
        public static TwoPaneSplitView Split(string stateKey, float initialLeftWidth,
            VisualElement left, VisualElement right)
        {
            string prefKey = "ZUI.Split." + stateKey;
            float width = EditorPrefs.GetFloat(prefKey, initialLeftWidth);
            float preferredWidth = width;

            var split = new TwoPaneSplitView(0, width, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("zui-split");
            if (left != null) { left.AddToClassList("zui-split__left"); split.Add(left); }
            if (right != null) { right.AddToClassList("zui-split__right"); split.Add(right); }

            // Keep the preferred divider width separate from its narrow-window fit. A small window temporarily
            // gives the preview/workspace a useful minimum width without overwriting the user's saved position.
            bool restoring = false;
            const float minLeftWidth = 180f;
            const float minRightWidth = 300f;
            const float dividerWidth = 6f;
            const float compactBreakpoint = 800f;
            float lastAvailableWidth = -1f;
            void FitToAvailableWidth(bool force = false)
            {
                float available = split.resolvedStyle.width;
                if (available <= 1f) return;
                split.EnableInClassList("zui-split--compact", available < compactBreakpoint);
                if (!force && Mathf.Approximately(available, lastAvailableWidth)) return;
                lastAvailableWidth = available;
                // Child min-width rules otherwise override fixedPaneInitialDimension (for example, Chunks' 460px
                // recipe preference). Give the two panes explicit responsive floors before fitting the divider.
                float maxLeft = Mathf.Max(minLeftWidth, available - minRightWidth - dividerWidth);
                float fitted = Mathf.Clamp(preferredWidth, minLeftWidth, maxLeft);
                if (Mathf.Approximately(fitted, split.fixedPaneInitialDimension)) return;
                restoring = true;
                split.fixedPaneInitialDimension = fitted;
                if (left != null) left.style.width = fitted;
                split.schedule.Execute(() => { restoring = false; FitToAvailableWidth(true); TipDivider(split); }).ExecuteLater(120);
            }
            split.RegisterCallback<GeometryChangedEvent>(_ => FitToAvailableWidth());

            // Persist only a completed divider drag. Window resizes call FitToAvailableWidth but never change
            // the preference; reopening wide restores the last width the user actually chose.
            split.RegisterCallback<PointerUpEvent>(e =>
            {
                if (restoring || !(e.target is VisualElement t) ||
                    (t.name != "unity-dragline-anchor" && t.name != "unity-dragline")) return;
                split.schedule.Execute(() =>
                {
                    if (restoring || left == null) return;
                    float chosen = left.resolvedStyle.width;
                    if (chosen <= 1f) return;
                    preferredWidth = chosen;
                    EditorPrefs.SetFloat(prefKey, preferredWidth);
                }).ExecuteLater(20);
            }, TrickleDown.TrickleDown);

            // The divider only exists once TwoPaneSplitView has built its own children, so the tooltip is
            // applied on attach rather than queried for here.
            split.RegisterCallback<AttachToPanelEvent>(_ => TipDivider(split));

            // Registered on the SPLIT in the TRICKLE-DOWN phase, not on the divider itself, for two reasons
            // that are both about the divider not being ours: TwoPaneSplitView re-creates it whenever it
            // re-inits (so a handler bound to the element would go missing after the first reset), and its
            // own resizer stops propagation on pointer-down (so a bubble-phase handler would never run).
            split.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.clickCount < 2) return;
                if (!(e.target is VisualElement t)) return;
                if (t.name != "unity-dragline-anchor" && t.name != "unity-dragline") return;

                // Forget the saved width FIRST, and hold the geometry writer off while the pane settles:
                // resizing fires the geometry callback, which would otherwise save the width straight back
                // and leave the key it was asked to clear in place.
                restoring = true;
                EditorPrefs.DeleteKey(prefKey);
                preferredWidth = initialLeftWidth;
                if (!Mathf.Approximately(split.fixedPaneInitialDimension, initialLeftWidth))
                    split.fixedPaneInitialDimension = initialLeftWidth;
                if (left != null) left.style.width = initialLeftWidth;   // what the drag itself writes
                split.schedule.Execute(() => { restoring = false; FitToAvailableWidth(true); TipDivider(split); }).ExecuteLater(120);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            return split;
        }

        /// The divider is TwoPaneSplitView's own element and carries no tooltip of its own; this says what it
        /// does, including the reset, which is otherwise undiscoverable.
        static void TipDivider(VisualElement split)
        {
            var handle = split.Q("unity-dragline-anchor") ?? split.Q("unity-dragline");
            if (handle != null)
                handle.tooltip = "Drag to resize this pane. Double-click to put it back to the width the tool ships with.";
        }

        /// A masonry column layout: items are dealt round-robin into `n` equal-width columns, each an
        /// INDEPENDENT vertical stack. This is the retained-mode answer to pairing controls in a Row —
        /// a Row couples heights (a tall expanded envelope stretches its neighbour and shoves the next
        /// row down), whereas each column here flows on its own, so a control never grows or moves
        /// because the thing beside it expanded. Two consecutive items land in adjacent columns (left,
        /// right, left, …), preserving the side-by-side pairing when heights match while decoupling it
        /// when they don't. Each item is stretched to its column's full width and keeps its natural
        /// height (any horizontal flexGrow it carried for Row-mode is cleared).
        public static VisualElement Columns(int n, params VisualElement[] items)
        {
            if (n < 1) n = 1;
            var root = new VisualElement();
            root.AddToClassList("zui-columns");
            var cols = new VisualElement[n];
            for (int i = 0; i < n; i++)
            {
                var col = new VisualElement();
                col.AddToClassList("zui-columns__column");
                col.EnableInClassList("zui-columns__column--following", i > 0);
                cols[i] = col;
                root.Add(col);
            }
            int placed = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                // Columns has always taken ownership of these four layout properties.
                // Release caller inline values so the layout role (and tool USS overrides) can own them.
                it.style.flexGrow = StyleKeyword.Null;
                it.style.flexShrink = StyleKeyword.Null;
                it.style.alignSelf = StyleKeyword.Null;
                it.style.marginBottom = StyleKeyword.Null;
                it.AddToClassList("zui-columns__item");
                cols[placed % n].Add(it);
                placed++;
            }
            return root;
        }

        /// A width-driven column flow: children stack vertically at `columnWidth`, and once the container
        /// is `columnWidth × 2` (× 3, …) wide it splits the SINGLE stack into that many contiguous columns,
        /// moving the tail of the stack into the next column (capped at 4). Unlike Z.Columns (round-robin
        /// masonry), this keeps one ordered stack and cuts it by width, height-balancing the split. Children
        /// added become flow units in order; use Z.HGroup to make a horizontal row travel as one unit.
        public static ZuiColumnFlow ColumnFlow(float columnWidth) => new ZuiColumnFlow(columnWidth);

        /// A horizontal row that a ColumnFlow treats as ONE flow unit — children lay out left-to-right (and
        /// wrap if the column is narrow), and the whole group moves between columns together.
        public static ZuiHGroup HGroup(params VisualElement[] kids) => new ZuiHGroup(kids);

        /// A titled, COLLAPSIBLE section — the header itself is the toggle. Children added to the
        /// returned element go inside the section body. Prefer this over a bare
        /// `Z.Text(.., ZuiText.Section, ..)` heading whenever the heading names a block of controls:
        /// a label can only sit beside its controls, a section owns them and can fold them away.
        /// `stateKey` defaults to the title; pass one explicitly if two sections share a title.
        /// `icon` (optional) is a ZUI icon name (e.g. "stack", "shapes") drawn as a tinted glyph before the
        /// title, so sections read apart at a glance — off by default, so a section that names no icon is
        /// unchanged.
        public static ZuiSection Section(string title, string tooltip, string stateKey = null, string icon = null)
            => new ZuiSection(title, tooltip, stateKey, icon);

        /// A framed section. The tooltip (if any) renders as a "?" hover icon on the title's own
        /// row — never below the content it explains (ui-layout-rules: help sits on the header).
        /// A titled box folds when its title row is clicked (see ZuiBox).
        public static ZuiBox Box(string title, string tooltip, params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip);
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        /// Box with a leading header ICON (a ZUI icon name, drawn as a tinted glyph before the title). The
        /// `icon` argument distinguishes this overload from the plain one purely by type (string, not a
        /// VisualElement child), so existing `Z.Box(title, tooltip, child…)` calls are untouched.
        public static ZuiBox Box(string title, string tooltip, string icon, params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip, null, icon);
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        /// Box with an explicit fold-state key — for repeated boxes that share a title (one per list row),
        /// which would otherwise all fold together.
        public static ZuiBox BoxKeyed(string title, string tooltip, string stateKey,
            params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip, stateKey);
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        /// Keyed box with a leading header ICON. The `icon` string sits after `stateKey`, so
        /// `Z.BoxKeyed(title, tooltip, key, child…)` (a VisualElement 4th arg) still binds the plain overload
        /// and only a string 4th arg selects this one.
        public static ZuiBox BoxKeyed(string title, string tooltip, string stateKey, string icon,
            params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip, stateKey, icon);
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        /// A titled, bordered container that does NOT fold — the plain "titled frame" primitive (ZuiBox folds
        /// from its title, ZuiSection collapses; this always shows its border + header). For wrapping ONE
        /// self-contained control (a 3D-orientation gizmo, a mini editor) so it reads as a single labelled unit.
        /// The tooltip renders as a "?" on the title row.
        public static ZuiFrame Frame(string title, string tooltip, params VisualElement[] children)
        {
            var frame = new ZuiFrame(title, tooltip);
            foreach (var c in children) if (c != null) frame.Add(c);
            return frame;
        }

        /// Frame with a leading header ICON. Same by-type overload split as Z.Box: a string 3rd arg selects
        /// this, a VisualElement 3rd arg stays the plain overload.
        public static ZuiFrame Frame(string title, string tooltip, string icon, params VisualElement[] children)
        {
            var frame = new ZuiFrame(title, tooltip, icon);
            foreach (var c in children) if (c != null) frame.Add(c);
            return frame;
        }

        public static VisualElement HSpace(float px = -1f)
        {
            var v = new VisualElement();
            v.AddToClassList("zui-foundation-hspace");
            if (px >= 0f) v.style.width = px; // Explicit spacing remains a caller contract.
            return v;
        }

        public static VisualElement VSpace(float px = -1f)
        {
            var v = new VisualElement();
            v.AddToClassList("zui-foundation-vspace");
            if (px >= 0f) v.style.height = px; // Explicit spacing remains a caller contract.
            return v;
        }

        public static VisualElement Flexible()
        {
            var v = new VisualElement();
            v.AddToClassList("zui-foundation-flexible");
            return v;
        }

        /// A thin horizontal rule that divides one run of controls from the next INSIDE a section or box —
        /// the lightweight grouping between a full sub-box (too heavy for a couple of rows) and nothing at
        /// all (a long flat column with no visual structure). An optional label sits on the line, so a
        /// divider can also name the group it introduces.
        public static VisualElement Divider(string label = null, string tooltip = null)
        {
            var d = new VisualElement();
            d.AddToClassList("zui-divider");
            if (!string.IsNullOrEmpty(label))
            {
                d.AddToClassList("zui-divider--labelled");
                var l = new Label(label) { tooltip = tooltip };
                l.AddToClassList("zui-divider__label");
                d.Add(l);
            }
            return d;
        }

        // ── text ────────────────────────────────────────────────────────────────────

        public static Label Text(string text, ZuiText style = ZuiText.Body, string tooltip = null)
        {
            // A Section heading is a ZuiSectionLabel: still a Label to every call site, but clicking it
            // folds the block it names (see ZuiSectionLabel). Every heading already written across the
            // tools became collapsible through this one line.
            if (style == ZuiText.Section) return new ZuiSectionLabel(text, tooltip);

            var l = new Label(text);
            switch (style)
            {
                case ZuiText.Small: l.AddToClassList("zui-text--small"); break;
                case ZuiText.Subtle: l.AddToClassList("zui-text--subtle"); break;
            }
            if (!string.IsNullOrEmpty(tooltip)) l.tooltip = tooltip;
            return l;
        }

        /// A small "?" glyph whose hover shows the full explanation.
        public static Label HelpIcon(string tooltip)
        {
            var l = new Label("?");
            l.AddToClassList("zui-help");
            l.tooltip = tooltip;
            return l;
        }

        /// A small monochrome ZUI icon: a Phosphor glyph resolved BY NAME via ZUIAssetLibrary.FindIcon
        /// (pass a bare name like "palette", "eye", "cube", "stack"), drawn as a TINTABLE background image so
        /// a USS class colours it from the sheet palette — a header icon takes the heading colour, a button
        /// icon a neutral tint — rather than reading as a raw photo. Sized square to `size`. Returns NULL when
        /// the name doesn't resolve, so a caller can `if (icon != null)` and never draw an empty box for a
        /// typo'd name (the header/button factories all treat null as "no icon"). PickingMode is Ignore: an
        /// icon is decoration on a clickable header/button, never its own hit target.
        public static VisualElement Icon(string name, float size = -1f)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var tex = ZUIAssetLibrary.FindIcon(name);
            if (tex == null) return null;
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.AddToClassList("zui-icon");
            v.AddToClassList("zui-foundation-icon");
            v.style.backgroundImage = Background.FromTexture2D(tex);
            if (size >= 0f)
            {
                v.style.width = size;
                v.style.height = size;
            }
            return v;
        }

        /// Label + control pair with the label sized to its own text (the FieldWrap pattern).
        ///
        /// When the wrapped control is a bare Int/Float field, the LABEL becomes a drag zone that scrubs it —
        /// Unity's native "drag the label" idiom, and the fix for the in-field drag being eaten by the text
        /// input's pointer capture (see ZuiScrub). Additive: keyboard entry, the field's own left-edge grip,
        /// and non-numeric controls (a slider merely CONTAINING a numeric input is skipped) are all untouched.
        public static VisualElement Field(string label, string tooltip, VisualElement control)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("zui-field");
            var l = new Label(label);
            l.AddToClassList("zui-field__label");
            l.tooltip = tooltip;
            wrap.Add(l);
            if (string.IsNullOrEmpty(control.tooltip)) control.tooltip = tooltip;
            wrap.Add(control);
            ZuiScrub.AttachToLabel(l, control);
            return wrap;
        }

        // ── controls (tooltip is always a required parameter) ───────────────────────

        /// Which look the DEFAULT-variant factories draw. `Vanilla` = Unity's own controls skinned with USS
        /// (the migration default); `Custom` = the Painter2D-drawn old-ZUI look (MicroSlider fill-track,
        /// button-toggles). Flip this once to roll every tool over; the explicit Z.MicroSlider / Z.ToggleButton
        /// factories always draw the custom look regardless, and a plain Z.Slider always draws vanilla — this
        /// only steers the wrappers that offer both (currently none but the door is open, per the roll-out plan).
        public enum Variant { Vanilla, Custom }
        public static Variant DefaultVariant = Variant.Vanilla;

        public static Button Button(string label, string tooltip, Action onClick)
        {
            var b = new Button(onClick) { text = label, tooltip = tooltip };
            b.AddToClassList("zui-button");
            ZuiButtonSurface.Attach(b);
            return b;
        }

        /// A standard action button with a leading ZUI glyph; an empty label makes it icon-only.
        public static Button Button(string label, string tooltip, Action onClick, string icon)
        {
            var b = Button(string.Empty, tooltip, onClick);
            FillButton(b, label, icon);
            if (string.IsNullOrEmpty(label)) b.AddToClassList("zui-btn--icononly");
            return b;
        }

        /// A compact ICON-ONLY button: one ZUI glyph in a square, for an action that must be visible on a row
        /// too tight for a word (the "open this reference in its editor" pencil beside a ZuiChip). It is the
        /// exception, not the default — a button with room for a label gets the label, because a glyph alone
        /// is only readable once you have hovered it. The tooltip is therefore REQUIRED, and carries the verb.
        /// A name that does not resolve falls back to "…" rather than an empty square.
        public static Button IconButton(string icon, string tooltip, Action onClick, float size = -1f)
        {
            var b = new Button(onClick) { tooltip = tooltip };
            b.AddToClassList("zui-iconbtn");
            b.AddToClassList("zui-foundation-icon-button");
            FillButton(b, null, icon);
            if (b.childCount == 0) b.text = "…";
            if (size >= 0f)
            {
                b.style.width = size;
                b.style.height = size;
            }
            return b;
        }

        /// The latching twin of <see cref="IconButton"/> — a ZuiToggleButton showing one glyph and no label,
        /// for a boolean whose name does not fit the row. Same rule: the tooltip carries the meaning, and it
        /// must be composed per STATE so it never describes the opposite of what clicking will do.
        public static ZuiToggleButton IconToggle(string icon, string tooltip, bool value, Action<bool> onChanged,
            float size = -1f)
        {
            var t = new ZuiToggleButton(null, tooltip, value, onChanged, icon);
            t.AddToClassList("zui-iconbtn");
            t.AddToClassList("zui-foundation-icon-button");
            if (size >= 0f)
            {
                t.style.width = size;
                t.style.height = size;
            }
            return t;
        }

        /// The old-ZUI MicroSlider: a filled track whose fill is the value, label+value inside, no thumb.
        /// Half the height of a vanilla Slider and needs no separate value field.
        public static ZuiMicroSlider MicroSlider(string label, float value, float min, float max,
            string tooltip, Action<float> onChanged, float width = -1f, bool showValue = true,
            float? defaultValue = null, int decimals = -1, string prefsKey = null, Action onBeforeMutate = null)
        {
            var s = new ZuiMicroSlider(label, value, min, max, tooltip, onChanged, showValue, defaultValue,
                onBeforeMutate, decimals: decimals, prefsKey: prefsKey);
            if (width >= 0f) s.style.width = width; // Explicit legacy override; omitted width belongs to USS.
            return s;
        }

        /// A button that latches on/off (the old-ZUI button toggle), instead of a checkbox with a tick.
        /// `icon` (optional, a ZUI icon name) draws a leading glyph before the label — off by default.
        public static ZuiToggleButton ToggleButton(string label, string tooltip, bool value, Action<bool> onChanged,
            string icon = null)
            => new ZuiToggleButton(label, tooltip, value, onChanged, icon);

        /// A subdued, borderless view toggle for secondary chrome. Tooltip follows the current value.
        public static ZuiQuietToggle QuietToggle(string label, Func<bool, string> tooltip, bool value,
            Action<bool> onChanged)
            => new ZuiQuietToggle(label, tooltip, value, onChanged);

        /// A joined row of buttons, single-select (radio look, custom-drawn) — the themed twin of MiniRadio.
        /// `icons` (optional, one name per segment) draws a leading glyph in each — null entries stay text-only.
        public static ZuiSegmented Segmented(int selected, string[] labels, string tooltip, Action<int> onChanged,
            string[] icons = null)
            => ZuiSegmented.Radio(selected, labels, tooltip, onChanged, icons);

        /// A joined row of independently-latching segments — the right control for a flag set.
        /// `icons` (optional, one name per segment) draws a leading glyph in each.
        public static ZuiSegmented SegmentedMulti(Func<int, bool> isOn, string[] labels, string tooltip,
            Action<int, bool> onToggled, string[] icons = null)
            => ZuiSegmented.Multi(isOn, labels, tooltip, onToggled, icons);

        /// The nine-point anchor picker: a 3×3 grid whose cell IS the point, plus the chosen anchor's name
        /// beside it. `selected` is 0..8 row-major from the top-left (see ZuiAnchorGrid.Names). Use this
        /// wherever a box attaches by one of its corners/edges/centre — never a nine-entry radio strip or
        /// dropdown, which makes the author translate a word back into a position on every visit.
        public static ZuiAnchorGrid AnchorGrid(int selected, string tooltip, Action<int> onChanged,
            Action onBeforeMutate = null)
            => new ZuiAnchorGrid(selected, tooltip, onChanged, onBeforeMutate);

        /// ROUTED to the ZUI button-toggle (2026-08-02): the wrapper's whole point is that call sites keep
        /// their shape while the resolved control obeys the rulebook — a bool never renders as an OS
        /// checkbox on any surface. Every existing Z.Toggle call site healed at once by this one line.
        public static ZuiToggleButton Toggle(string label, string tooltip, bool value, Action<bool> onChanged)
            => new ZuiToggleButton(label, tooltip, value, onChanged);

        /// Slider with an inline numeric input (pass showInput:false for a bare track — for values
        /// whose exact number doesn't matter, e.g. a preview-only opacity). Values are rounded to 5
        /// decimals at the source so float noise from drag interpolation never reaches the display
        /// or the stored value.
        public static Slider Slider(float value, float min, float max, string tooltip,
Action<float> onChanged, float width = -1f, bool showInput = true)
        {
            var s = new Slider(min, max) { value = value, tooltip = tooltip, showInputField = showInput };
            s.AddToClassList("zui-slider-field");
            if (width >= 0f) s.style.width = width;
            s.RegisterValueChangedCallback(e =>
            {
                float r = (float)Math.Round(e.newValue, 5);
                s.SetValueWithoutNotify(r);
                onChanged?.Invoke(r);
            });
            return s;
        }

        public static SliderInt SliderInt(int value, int min, int max, string tooltip,
Action<int> onChanged, float width = -1f)
        {
            var s = new SliderInt(min, max) { value = value, tooltip = tooltip, showInputField = true };
            s.AddToClassList("zui-slider-field");
            if (width >= 0f) s.style.width = width;
            s.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return s;
        }

public static FloatField Float(float value, string tooltip, Action<float> onChanged, float width = -1f)
        {
            var f = new FloatField { value = value, tooltip = tooltip };
            f.AddToClassList("zui-float-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            ZuiScrub.Attach(f);   // scrub-draggable, keyboard entry untouched
            return f;
        }

public static TextField TextInput(string value, string tooltip, Action<string> onChanged, float width = -1f)
        {
            var f = new TextField { value = value, tooltip = tooltip };
            f.AddToClassList("zui-text-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        public static ObjectField Object<T>(T value, string tooltip, Action<T> onChanged,
float width = -1f, bool allowSceneObjects = false) where T : UnityEngine.Object
        {
            var f = new ObjectField
            {
                objectType = typeof(T),
                value = value,
                tooltip = tooltip,
                allowSceneObjects = allowSceneObjects
            };
            f.AddToClassList("zui-object-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue as T));
            return f;
        }

        /// The name at index `i` of an optional icon array, or null (shorter-than-options arrays are fine).
        internal static string IconAt(string[] icons, int i)
            => icons != null && i >= 0 && i < icons.Length ? icons[i] : null;

        /// Fill a button-family element (radio segment, cycle button, segmented, toggle button) with an
        /// optional leading icon glyph + a label. With NO icon it just sets `.text` — byte-identical to the
        /// old text-only path, so every text-only caller is unchanged. With an icon it clears the text and
        /// lays out an icon child + a label child in a centred row (a Button's own `.text` and child elements
        /// don't co-operate cleanly, so an icon button draws both as children). A null-resolving icon name
        /// falls back to the plain text path, so a typo never leaves a blank button.
        internal static void FillButton(Button b, string label, string icon)
        {
            ZuiButtonSurface.Attach(b);
            b.Clear();
            var ic = string.IsNullOrEmpty(icon) ? null : Icon(icon, 13f);
            if (ic == null) { b.text = label ?? string.Empty; return; }
            b.text = string.Empty;
            b.AddToClassList("zui-btn--iconrow");
            ic.AddToClassList("zui-btn__icon");
            b.Add(ic);
            if (!string.IsNullOrEmpty(label))
            {
                var l = new Label(label) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("zui-btn__label");
                b.Add(l);
            }
        }

        /// A row of mutually-exclusive mini buttons (the MiniRadio pattern). Returns the row;
        /// selection state is kept in the buttons' classes.
        /// `wrap` lets a long option set fold onto a second line instead of running off the side of a narrow
        /// pane. Off by default, because a wrapped radio loses its single-pill look and most sets are short;
        /// turn it on for the ones that genuinely grow over time (Pyre's shape picker gains a shape now and
        /// then, and each one pushed the row further past the edge).
        /// `icons` (optional, one ZUI icon name per option) draws a leading glyph in each button — null
        /// entries (or a null array) stay text-only, so existing calls are unchanged.
        public static VisualElement MiniRadio(int index, string[] options, string tooltip, Action<int> onChanged,
            bool wrap = false, string[] icons = null)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("zui-radio");
            if (wrap) row.AddToClassList("zui-radio--wrap");
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = new Button(() =>
                {
                    for (int b = 0; b < buttons.Length; b++)
                        buttons[b].EnableInClassList("zui-radio__on", b == idx);
                    onChanged?.Invoke(idx);
                })
                { tooltip = tooltip };
                FillButton(buttons[i], options[i], IconAt(icons, i));
                buttons[i].EnableInClassList("zui-radio__on", i == index);
                if (i == 0) buttons[i].AddToClassList("zui-radio__first");
                if (i == options.Length - 1) buttons[i].AddToClassList("zui-radio__last");
                row.Add(buttons[i]);
            }
            return row;
        }

        /// Vertical variant of MiniRadio — a stacked column of mutually-exclusive buttons, for when
        /// the radio sits beside a square control (a pad, a picker) and should match its height.
        public static VisualElement MiniRadioVertical(int index, string[] options, string tooltip,
            Action<int> onChanged, float width = 70f, float totalHeight = 0f, string[] icons = null)
        {
            var col = new VisualElement { tooltip = tooltip };
            col.AddToClassList("zui-radio");
            col.style.flexDirection = FlexDirection.Column;
            col.style.width = width;
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = new Button(() =>
                {
                    for (int b = 0; b < buttons.Length; b++)
                        buttons[b].EnableInClassList("zui-radio__on", b == idx);
                    onChanged?.Invoke(idx);
                })
                { tooltip = tooltip };
                FillButton(buttons[i], options[i], IconAt(icons, i));
                buttons[i].EnableInClassList("zui-radio__on", i == index);
                if (totalHeight > 0f) buttons[i].style.height = totalHeight / options.Length;
                buttons[i].style.marginTop = 0f;
                buttons[i].style.marginBottom = 0f;
                col.Add(buttons[i]);
            }
            return col;
        }

        /// Label stacked ABOVE a compact slider — the SliderStacked pattern for tightly packed rows
        /// where side-by-side label+slider would be too wide.
        public static VisualElement Stacked(string label, string tooltip, float value, float min, float max,
            Action<float> onChanged, float width, bool isInt = false)
        {
            var col = new VisualElement();
            col.style.width = width;
            col.style.flexShrink = 0f;
            col.Add(Text(label, ZuiText.Small, tooltip));
            if (isInt)
            {
                var s = new SliderInt(Mathf.RoundToInt(min), Mathf.RoundToInt(max))
                { value = Mathf.RoundToInt(value), tooltip = tooltip };
                s.style.width = width;
                s.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
                col.Add(s);
            }
            else
            {
                var s = new Slider(min, max) { value = value, tooltip = tooltip };
                s.style.width = width;
                s.RegisterValueChangedCallback(e =>
                {
                    float r = (float)Math.Round(e.newValue, 5);
                    s.SetValueWithoutNotify(r);
                    onChanged?.Invoke(r);
                });
                col.Add(s);
            }
            return col;
        }

        /// One compact button that cycles through the options on each click — the CycleButton
        /// pattern, for when a MiniRadio row would be too wide for the space.
        /// `icons` (optional, one per option) draws the current option's glyph before its label — swapped as
        /// the button cycles; a null array stays text-only (byte-identical to the old behaviour).
        public static Button CycleButton(int index, string[] options, string tooltip, Action<int> onChanged,
            string[] icons = null)
        {
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            Button b = null;
            b = new Button(() =>
            {
                current = (current + 1) % options.Length;
                FillButton(b, options[current], IconAt(icons, current));
                onChanged?.Invoke(current);
            })
            { tooltip = tooltip };
            FillButton(b, options[current], IconAt(icons, current));
            return b;
        }

        public static DropdownField Dropdown(int index, List<string> choices, string tooltip,
            Action<int> onChanged, float width = -1f)
        {
            var d = new DropdownField(choices, Mathf.Clamp(index, 0, choices.Count - 1)) { tooltip = tooltip };
            d.AddToClassList("zui-foundation-dropdown");
            if (width >= 0f) d.style.width = width;
            d.RegisterValueChangedCallback(e => onChanged?.Invoke(choices.IndexOf(e.newValue)));
            return d;
        }

        public static EnumField EnumDropdown<T>(T value, string tooltip, Action<T> onChanged,
            float width = -1f) where T : Enum
        {
            var d = new EnumField(value) { tooltip = tooltip };
            d.AddToClassList("zui-foundation-enum-dropdown");
            if (width >= 0f) d.style.width = width;
            d.RegisterValueChangedCallback(e => onChanged?.Invoke((T)e.newValue));
            return d;
        }

        public static IntegerField Int(int value, string tooltip, Action<int> onChanged, float width = -1f)
        {
            var f = new IntegerField { value = value, tooltip = tooltip };
            f.AddToClassList("zui-foundation-int-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            ZuiScrub.Attach(f);   // scrub-draggable, keyboard entry untouched
            return f;
        }

        /// The control type IMGUI's tooltip audits kept catching bare — here the tooltip is required
        /// like everywhere else.
        public static ColorField Color(Color value, string tooltip, Action<Color> onChanged,
            float width = -1f, bool showAlpha = true, bool hdr = false)
        {
            var f = new ColorField { value = value, tooltip = tooltip, showAlpha = showAlpha, hdr = hdr };
            f.AddToClassList("zui-foundation-color-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A plain AnimationCurve field. Distinct from Z.Envelope: an envelope is ZUI's own draggable
        /// multi-point editor over a List&lt;ZUIEnvelopePoint&gt;, whereas this edits a real UnityEngine
        /// AnimationCurve through Unity's own curve editor — which is what a runtime type already storing an
        /// AnimationCurve (ChunkSpec's size/alpha-over-life) actually needs. Width and height are paired:
        /// widening one without the other just makes a curve clumsier to read, not more useful.
        public static CurveField Curve(AnimationCurve value, string tooltip, Action<AnimationCurve> onChanged,
            float width = -1f, float height = -1f)
        {
            var f = new CurveField { value = value, tooltip = tooltip };
            f.AddToClassList("zui-curve-field");
            if (width >= 0f) f.style.width = width;
            if (height >= 0f) f.style.height = height;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A plain Gradient field (Unity's own gradient editor), sized rather than left to stretch.
        public static GradientField Gradient(Gradient value, string tooltip, Action<Gradient> onChanged,
            float width = -1f, float height = -1f)
        {
            var f = new GradientField { value = value, tooltip = tooltip };
            f.AddToClassList("zui-gradient-field");
            if (width >= 0f) f.style.width = width;
            if (height >= 0f) f.style.height = height;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A tooltipped GradientField over a get/set pair — the form a control (ZuiFill) that swaps the
        /// underlying Gradient instance behind the field wants, since `get` re-reads the live gradient on
        /// each rebuild. Alpha is always shown (GradientField's alpha strip); HDR stays off (its default),
        /// so this edits a plain 0..1 colour gradient.
        public static GradientField Gradient(string tooltip, Func<Gradient> get, Action<Gradient> set,
            float width = -1f)
        {
            var f = new GradientField { value = get != null ? get() : null, tooltip = tooltip };
            f.AddToClassList("zui-gradient-bound-field");
            if (width >= 0f) f.style.width = width;
            f.RegisterValueChangedCallback(e => set?.Invoke(e.newValue));
            return f;
        }

        /// The richer ZuiGradient editor — a base Gradient plus non-destructive transform knobs (reverse /
        /// hue / saturation / brightness / contrast / quantise / cycle), topped by a LIVE preview strip that
        /// shows the TRUE evaluated ramp (painted from ZuiGradient.ToLut — the exact LUT the runtime/shader
        /// uses, so the preview is never a lie). Mutates the passed ZuiGradient in place; onChanged fires after edits.
        public static ZuiGradientControl Gradient(ZuiGradient value, string tooltip, Action onChanged)
        {
            var c = new ZuiGradientControl(value, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            return c;
        }

        /// A colour-ramp editor over anything that speaks IZuiRamp (Pyre's PyreRamp) — a strip painted from the
        /// ramp's OWN Eval over a checker, with a draggable marker per stop, double-click to insert, right-click a
        /// marker to remove, and the ramp's blend modes as a segmented row. Mutates the ramp IN PLACE, so
        /// <paramref name="onBeforeMutate"/> is the Undo.RecordObject hook (fired once per gesture) and
        /// <paramref name="onChanged"/> fires after every edit. ZuiReflect reaches for this automatically for a
        /// reflected IZuiRamp field; this factory is for a hand-built window that wants one directly.
        public static ZuiRampControl Ramp(IZuiRamp value, string tooltip, Action onBeforeMutate = null,
            Action onChanged = null)
        {
            var c = new ZuiRampControl(value, tooltip);
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            if (onChanged != null) c.OnChanged += onChanged;
            return c;
        }

        /// A ZuiSwatchRef picker — an inline colour OR a named swatch in a SwatchPalette (palette ObjectField +
        /// swatch-name dropdown + inline colour + a live RESOLVED preview). Since ZuiSwatchRef is a struct, onChanged
        /// delivers the NEW struct (the caller writes it back to its field).
        public static ZuiSwatchControl Swatch(ZuiSwatchRef value, string tooltip, Action<ZuiSwatchRef> onChanged,
            float width = 220f)
        {
            var c = new ZuiSwatchControl(value, tooltip, width);
            if (onChanged != null) c.OnChanged += onChanged;
            return c;
        }

        /// A min/max range: numeric low field + MinMaxSlider + numeric high field, kept in sync
        /// (the SliderRange pattern). Rounded to 5 decimals like every slider. Double-clicking the track
        /// resets BOTH handles to <paramref name="lowDefault"/>/<paramref name="highDefault"/> when given,
        /// else to the full <paramref name="min"/>/<paramref name="max"/> range.
        public static VisualElement MinMax(float low, float high, float min, float max, string tooltip,
            Action<float, float> onChanged, float sliderWidth = -1f, bool isInt = false,
            float? lowDefault = null, float? highDefault = null)
        {
            var slider = new MinMaxSlider(low, high, min, max)
            { tooltip = tooltip + "  ·  Double-click to reset." };
            slider.AddToClassList("zui-minmax-field__slider");
            if (sliderWidth >= 0f) slider.style.width = sliderWidth;
            // isInt: the flanking numeric fields are IntegerFields and both handle+field snap to whole
            // numbers — for a discrete range (a frame window) that can never be fractional.
            BaseField<int> lowFieldI = isInt ? new IntegerField { value = Mathf.RoundToInt(low), tooltip = tooltip } : null;
            BaseField<int> highFieldI = isInt ? new IntegerField { value = Mathf.RoundToInt(high), tooltip = tooltip } : null;
            var lowField = isInt ? null : new FloatField { value = low, tooltip = tooltip };
            var highField = isInt ? null : new FloatField { value = high, tooltip = tooltip };
            (isInt ? (VisualElement)lowFieldI : lowField).AddToClassList("zui-minmax-field__number");
            (isInt ? (VisualElement)highFieldI : highField).AddToClassList("zui-minmax-field__number");

            void Commit(float lo, float hi, bool fromSlider)
            {
                lo = isInt ? Mathf.Round(lo) : (float)Math.Round(Mathf.Clamp(lo, min, max), 5);
                hi = isInt ? Mathf.Round(hi) : (float)Math.Round(Mathf.Clamp(hi, lo, max), 5);
                lo = Mathf.Clamp(lo, min, max); hi = Mathf.Clamp(hi, lo, max);
                if (fromSlider)
                {
                    if (isInt) { lowFieldI.SetValueWithoutNotify(Mathf.RoundToInt(lo)); highFieldI.SetValueWithoutNotify(Mathf.RoundToInt(hi)); }
                    else { lowField.SetValueWithoutNotify(lo); highField.SetValueWithoutNotify(hi); }
                }
                else slider.SetValueWithoutNotify(new Vector2(lo, hi));
                onChanged?.Invoke(lo, hi);
            }
            slider.RegisterValueChangedCallback(e => Commit(e.newValue.x, e.newValue.y, true));

            // Double-click the track resets both handles — to the given defaults, or the full range if none.
            // Updates the slider AND both flanking fields, then fires onChanged (so the owner records Undo).
            void ResetToDefault()
            {
                float rlo = lowDefault ?? min;
                float rhi = highDefault ?? max;
                rlo = isInt ? Mathf.Round(rlo) : (float)Math.Round(Mathf.Clamp(rlo, min, max), 5);
                rhi = isInt ? Mathf.Round(rhi) : (float)Math.Round(Mathf.Clamp(rhi, rlo, max), 5);
                rlo = Mathf.Clamp(rlo, min, max); rhi = Mathf.Clamp(rhi, rlo, max);
                slider.SetValueWithoutNotify(new Vector2(rlo, rhi));
                if (isInt) { lowFieldI.SetValueWithoutNotify(Mathf.RoundToInt(rlo)); highFieldI.SetValueWithoutNotify(Mathf.RoundToInt(rhi)); }
                else { lowField.SetValueWithoutNotify(rlo); highField.SetValueWithoutNotify(rhi); }
                onChanged?.Invoke(rlo, rhi);
            }
            // TrickleDown so this beats the slider's own drag manipulator; StopImmediatePropagation so the
            // double-click doesn't also begin a drag.
            slider.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0 && e.clickCount == 2) { ResetToDefault(); e.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);

            if (isInt)
            {
                // Scrub-draggable flanking fields (the range factory knows its bounds, so clamp to them —
                // Commit still enforces lo≤hi on top).
                ZuiScrub.Attach((IntegerField)lowFieldI, Mathf.RoundToInt(min), Mathf.RoundToInt(max));
                ZuiScrub.Attach((IntegerField)highFieldI, Mathf.RoundToInt(min), Mathf.RoundToInt(max));
                lowFieldI.RegisterValueChangedCallback(e => Commit(e.newValue, slider.maxValue, false));
                highFieldI.RegisterValueChangedCallback(e => Commit(slider.minValue, e.newValue, false));
                return Row(lowFieldI, slider, highFieldI);
            }
            ZuiScrub.Attach(lowField, min, max);
            ZuiScrub.Attach(highField, min, max);
            lowField.RegisterValueChangedCallback(e => Commit(e.newValue, slider.maxValue, false));
            highField.RegisterValueChangedCallback(e => Commit(slider.minValue, e.newValue, false));
            return Row(lowField, slider, highField);
        }

        /// The embedded-style range control the "MicroMinMax not built yet" note asked for: one two-handle
        /// slider drawn in the MicroSlider idiom (label + "low – high" readout INSIDE the track, no flanking
        /// numeric fields, no native `MinMaxSlider`). Use in place of Z.MinMax wherever a min/max pair should
        /// read as compact as a single MicroSlider — `decimals: 0` + an int-rounding setter for a discrete
        /// range (e.g. a frame window), same convention as Z.MicroSlider.
        public static ZuiMicroMinMax MicroMinMax(string label, float low, float high, float min, float max,
            string tooltip, Action<float, float> onChanged, float width = -1f, bool showValue = true,
            float? lowDefault = null, float? highDefault = null, int decimals = -1, Action onBeforeMutate = null)
        {
            var s = new ZuiMicroMinMax(label, low, high, min, max, tooltip, onChanged, showValue,
                lowDefault, highDefault, onBeforeMutate, decimals);
            if (width >= 0f) s.style.width = width; // Explicit legacy override; omitted width belongs to USS.
            return s;
        }

        /// A collapsible framed section — the FoldoutBox pattern. The tooltip lands on the Foldout
        /// AND its internal disclosure Toggle (which does not inherit it — a known UI Toolkit trap).
        public static Foldout Foldout(string title, string tooltip, bool open, params VisualElement[] children)
        {
            var f = new Foldout { text = title, value = open, tooltip = tooltip };
            f.AddToClassList("zui-box");
            var disclosure = f.Q<Toggle>();
            if (disclosure != null) disclosure.tooltip = tooltip;
            foreach (var c in children) if (c != null) f.Add(c);
            return f;
        }

        public static HelpBox Help(string text, HelpBoxMessageType type = HelpBoxMessageType.Info)
            => new HelpBox(text, type);

        /// Square drag-pad for a plain Vector2 (the PositionPad pattern — NOT for animatable values).
        public static ZuiPad Pad(Vector2 value, Rect range, string tooltip, Action<Vector2> onChanged,
            float size = 56f, bool flipY = true)
        {
            var pad = new ZuiPad(value, range, tooltip, size, flipY);
            pad.OnChanged += v => onChanged?.Invoke(v);
            return pad;
        }

        /// The SAME pad (see Z.Pad above), WITH a value readout: two labelled scrub/type X/Y fields beside
        /// it, each tooltip stating the range it accepts. A bare Z.Pad has no readout at all and its own
        /// drag resolution is far too coarse to land an exact number (ui-rules §1 "a numeric input must be
        /// drag-scrubbable, never keyboard-only"; §3 legible) — this is the fix, generalised from the
        /// pad+fields row BackSplashWindow.BuildPositionRow worked out by hand before this existed. Reach
        /// for this wherever a PLAIN (non-animatable) spatial X/Y pair needs a control — Z.Value2D is the
        /// animatable-value analog (Static/Curve modes, a ZUIValue pair) and is not a fit for a plain
        /// Vector2 like a placement offset.
        /// `sync` is handed back so a driver OTHER than this row — a value dragged directly on a preview
        /// stage, say — can push its own change into the dot AND both fields without re-firing `onChanged`,
        /// the same job BackSplashWindow.PushPosition already does by hand for its own pad.
        public static VisualElement PadRow(out Action<Vector2> sync, Vector2 value, Rect range, string tooltip,
            Action<Vector2> onChanged, float padSize = 56f, float fieldWidth = 60f, bool flipY = true)
        {
            FloatField xField = null, yField = null;
            var pad = new ZuiPad(value, range, tooltip, padSize, flipY);
            pad.OnChanged += v =>
            {
                onChanged?.Invoke(v);
                xField?.SetValueWithoutNotify(v.x);
                yField?.SetValueWithoutNotify(v.y);
            };

            string xTip = $"{tooltip} Ranges {range.xMin:0.##} to {range.xMax:0.##}.";
            string yTip = $"{tooltip} Ranges {range.yMin:0.##} to {range.yMax:0.##}.";
            // `pad.Value`'s setter clamps to `range` — read it straight back after the assignment (rather
            // than trusting the typed/scrubbed `v`) so a field that ran past the range shows the SAME clamped
            // number the pad and the asset now hold, on both axes, instead of a stale typed value the pad
            // silently disagrees with (T-0358/1, T-0367).
            xField = Float(value.x, xTip, v =>
            {
                pad.Value = new Vector2(v, pad.Value.y);
                var clamped = pad.Value;
                xField.SetValueWithoutNotify(clamped.x);
                yField?.SetValueWithoutNotify(clamped.y);
                onChanged?.Invoke(clamped);
            }, fieldWidth);
            yField = Float(value.y, yTip, v =>
            {
                pad.Value = new Vector2(pad.Value.x, v);
                var clamped = pad.Value;
                xField?.SetValueWithoutNotify(clamped.x);
                yField.SetValueWithoutNotify(clamped.y);
                onChanged?.Invoke(clamped);
            }, fieldWidth);

            sync = v =>
            {
                pad.Value = v;
                xField.SetValueWithoutNotify(v.x);
                yField.SetValueWithoutNotify(v.y);
            };

            return Row(pad, Field("X", xTip, xField), Field("Y", yTip, yField));
        }

        /// A "3D direction / orientation" control: a small draggable LIT SPHERE that sets a direction as yaw
        /// (azimuth) + pitch (elevation), optionally with a distance, plus numeric fallback fields and a larger
        /// 3D preview that opens on hover (a non-modal Z.Popover) or pins open. The lit hotspot on the sphere IS
        /// the readout, so a direction reads as a real point on a ball, not two disconnected sliders. Reach for
        /// this wherever a light/aim direction is authored (Pyre's Gem/solid key light is the reference use).
        /// `onChanged` fires (yaw, pitch, distance) once per edit — wrap it in your Undo/Dirty helper. Distance
        /// is passed even when Options.showDistance is false (ignore it there). Wrap it in Z.Frame(title, ..) for
        /// a titled bordered unit.
        public static ZuiDirection3D Direction3D(float yaw, float pitch, float distance, string tooltip,
            Action<float, float, float> onChanged, ZuiDirection3D.Options options = null)
        {
            var c = new ZuiDirection3D(yaw, pitch, distance, options ?? new ZuiDirection3D.Options(), tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            return c;
        }

        /// Labelled ZUIValue editor (Static / MinMax / Curve modes with the ⋯ config menu) — the
        /// ValRow workhorse. Pass `onBeforeMutate` to record Undo on the owning asset.
        public static ZuiValueControl Value(string label, ZUIValue v, ZuiValueControl.Options options,
            string tooltip, Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValueControl(label, v, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// A colour-or-fill editor (ZuiFill): a plain colour picker in Solid mode (alpha shown), or a
        /// gradient-driven over-life / linear / radial / noise fill, switched via the ⋯ menu — the colour
        /// analog of Z.Value. `onBeforeMutate` records Undo on the owning asset (every edit is
        /// onBeforeMutate → apply → onChanged).
        public static ZuiFillControl Fill(string label, ZuiFill fill, string tooltip, Action onChanged,
            Action onBeforeMutate, ZuiFillControl.Options options = null)
        {
            var c = new ZuiFillControl(label, fill, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// Synchronized XY pair of ZUIValues as ONE 2D control (drag a dot / trace a path) — the
        /// 2D analog of Z.Value. Pass `onBeforeMutate` to record Undo on the owning asset.
        public static ZuiValue2DControl Value2D(string label, ZUIValue x, ZUIValue y,
            ZuiValue2DControl.Options options, string tooltip, Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValue2DControl(label, x, y, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// The SAME 2D control over a PLAIN Vector2 (no animation, no mode switch) — for values that
        /// must never animate (a pivot, an authored placement). `stateKey` should be the edited data
        /// instance so fold/display state survives window rebuilds.
        public static ZuiValue2DControl Vector2Field(string label, Func<Vector2> get, Action<Vector2> set,
            object stateKey, ZuiValue2DControl.Options options, string tooltip,
            Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValue2DControl(label, new ZuiVector2Source(get, set), stateKey, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// A [SerializeReference] polymorphic field: a fold header naming the current concrete type, with a
        /// button that switches it, over a body the CALLER fills with that type's own fields (so a child can
        /// get a better control than Unity's default — a clip-name dropdown, an asset picker). Subscribe to
        /// OnTypeChanged and rebuild: switching the type changes which fields exist.
        public static ZuiManagedRef ManagedRef(SerializedProperty property, string title, string tooltip)
            => new ZuiManagedRef(property, title, tooltip);

        /// DAW-style multi-point envelope editor over a caller-owned List&lt;ZUIEnvelopePoint&gt;
        /// (the same runtime data ZUI.Envelope edits). Pass `onBeforeMutate` to record Undo on the
        /// owning asset — it fires once per gesture, before the first mutation.
        public static ZuiEnvelope Envelope(List<ZUIEnvelopePoint> points, ZuiEnvelopeOptions options,
            string tooltip, Action onChanged, Action onBeforeMutate = null,
            float width = float.NaN, float height = float.NaN)
        {
            var env = new ZuiEnvelope(points, options, tooltip, width, height);
            if (onChanged != null) env.OnChanged += onChanged;
            if (onBeforeMutate != null) env.OnBeforeMutate += onBeforeMutate;
            return env;
        }

        /// A scrub bar over a LOOP of a given number of seconds, split into consecutive coloured bands
        /// (<see cref="ZuiTimelineSegment"/>) with a playhead, tick marks and numeric time labels. Click
        /// anywhere on it to jump the playhead there; drag to scrub. <paramref name="onChanged"/> delivers
        /// the new position in SECONDS.
        ///
        /// Set the bands with <c>SetSegments(...)</c> after construction (and again whenever a length the
        /// host owns changes) — the total, and therefore the whole ruler, is derived from them. It stretches
        /// to the width it is given, so give it a full-width row rather than parking it beside something.
        /// Push a host clock in with <c>SetSecondsWithoutNotify</c>, never the <c>Seconds</c> setter, or a
        /// play tick re-enters <paramref name="onChanged"/>.
        public static ZuiTimeline Timeline(float seconds, string tooltip, Action<float> onChanged = null,
            float height = 22f)
            => new ZuiTimeline(seconds, tooltip, onChanged, height);

        /// A MULTI-LANE clock: N named bands (<see cref="ZuiLane"/>), each placed by its own ABSOLUTE start
        /// and end, on one shared ruler of <paramref name="lengthSeconds"/> seconds, with named instants
        /// (<see cref="ZuiLaneMarker"/>) and one draggable playhead. Click or drag anywhere to move it;
        /// <paramref name="onTimeChanged"/> delivers SECONDS.
        ///
        /// Reach for this — not <c>Z.Timeline</c> — whenever two things on one clock can OVERLAP.
        /// <c>Z.Timeline</c> is by contract a single strip of CONSECUTIVE bands, so on it a "starts at 0.15 s"
        /// dial silently means "starts 0.15 s after the previous one ended", and the dial and the picture stop
        /// agreeing.
        ///
        /// THE HOST OWNS THE LENGTH: pass the clock's full length (computed over every lane, disabled ones
        /// included) and keep it with <c>SetLength</c>, or a lane switched off rescales the ruler and slides
        /// every remaining band sideways. Feed the lanes with <c>SetLanes(...)</c> and the instants with
        /// <c>SetMarkers(...)</c> — both REPLACE the data on the live element, so a host that rebuilds one
        /// card does not lose the playhead or the drag. Push a host transport's clock in with
        /// <c>SetTime</c>, never the <c>Seconds</c> setter, or a play tick re-enters
        /// <paramref name="onTimeChanged"/>. A lane whose owner is disabled is passed with <c>dim: true</c>,
        /// never omitted — omitting it changes the control's height and moves whatever sits above it.
        ///
        /// Pass <paramref name="onLaneMoved"/> (lane index, new START in seconds) to let the user drag a band's
        /// body in time; the host decides what a start means and writes it. One drag is one Undo step, the
        /// ruler keeps scrubbing, and a dim lane stays put. <paramref name="gutterWidth"/> is the gutter's
        /// MAXIMUM — the gutter sizes itself to its longest name up to that.
        public static ZuiLanes Lanes(float lengthSeconds, string tooltip, Action<float> onTimeChanged = null,
            float laneHeight = 16f, float gutterWidth = 96f, Action<int, float> onLaneMoved = null)
            => new ZuiLanes(lengthSeconds, tooltip, onTimeChanged, laneHeight, gutterWidth)
                { OnLaneMoved = onLaneMoved };

        // ── pixel-exact drawing (a bespoke IMGUI preview canvas) ─────────────────────

        /// <summary>Plan a PIXEL-EXACT render for a preview canvas: how big a low-resolution buffer to render,
        /// and how many whole device pixels each of its texels will occupy.
        ///
        /// Reach for this whenever a preview shows an image whose individual pixels are meant to READ as
        /// pixels (pixel art, a palette strip, a low-res buffer blown up). IMGUI measures in GUI points and
        /// the editor rasterizes in device pixels, so an image sized in whole POINTS is a fractional number
        /// of device pixels on any display scaled above 100% — blocks come out alternately n and n+1 pixels
        /// wide with a sliver of the neighbouring colour inside them. It is invisible at 100% scaling, which
        /// is exactly why it ships. See ZuiPixel.cs for the full account.
        ///
        /// Two calls, not one, because a caller that renders its OWN buffer needs the size before it renders
        /// and the destination rect only after: <c>Z.PixelFit(...)</c> → render at <c>fit.width × fit.height</c>
        /// → <c>fit.Place(texture)</c> → <c>Z.DrawPixels(placement, texture)</c>.
        /// <paramref name="pointsPerPixel"/> is the authored pixel size as the user dials it, in points.
        /// <paramref name="container"/> is the IMGUIContainer the viewport rect is local to — where the island
        /// sits inside the window is part of the mapping. Pass Vector2.zero instead from a plain
        /// EditorWindow's OnGUI, whose IMGUI space is already panel space.</summary>
        public static ZuiPixelFit PixelFit(Rect viewport, VisualElement container, float pointsPerPixel)
            => ZuiPixel.Fit(viewport, container, pointsPerPixel);

        /// <inheritdoc cref="PixelFit(Rect,VisualElement,float)"/>
        public static ZuiPixelFit PixelFit(Rect viewport, Vector2 panelOrigin, float pointsPerPixel)
            => ZuiPixel.Fit(viewport, panelOrigin, pointsPerPixel);

        /// <summary>Draw a placed buffer with nearest-neighbour sampling, on whole device pixels. The second
        /// half of <c>Z.PixelFit</c>; the placement comes from <c>fit.Place(texture)</c>, measured off the
        /// texture that actually came back rather than the one that was asked for. Repaint-time only.
        /// <paramref name="material"/> is optional — pass one for a buffer that needs a blend other than the
        /// GUI's own, chiefly a PREMULTIPLIED buffer (anything a TMP/SDF shader rendered), which the default
        /// blend darkens every soft rim of.</summary>
        public static void DrawPixels(in ZuiPixelPlacement placement, Texture buffer, Material material = null)
            => ZuiPixel.Draw(placement, buffer, material);

        /// <summary>Draw an EXISTING pixel-art texture into a preview viewport, pixel-exact in one call:
        /// centred, blown up by the largest whole number of device pixels it fits at, corner snapped onto a
        /// device pixel so no block is ragged. The one-liner for a caller who is not rendering their own
        /// buffer — if you are, use <c>Z.PixelFit</c> first, since the buffer size has to be known before the
        /// render. Returns the placement, so the zoom is there to reason about or log.</summary>
        public static ZuiPixelPlacement DrawPixels(Rect viewport, VisualElement container, Texture buffer,
            Material material = null)
            => ZuiPixel.Draw(viewport, container, buffer, material);

        /// <inheritdoc cref="DrawPixels(Rect,VisualElement,Texture,Material)"/>
        public static ZuiPixelPlacement DrawPixels(Rect viewport, Vector2 panelOrigin, Texture buffer,
            Material material = null)
            => ZuiPixel.Draw(viewport, panelOrigin, buffer, material);

        // ── popover / menu ───────────────────────────────────────────────────────────

        /// A floating ZUI-styled card anchored to <paramref name="anchor"/>'s worldBound, added into the
        /// window's overlay layer. It positions below the anchor (flipping above / clamping when it would
        /// overflow the window), dismisses on an outside click or Esc, and hosts whatever
        /// <paramref name="build"/> adds to its panel. Returns a handle whose Close() dismisses it. The
        /// general primitive for a bespoke flyout (a hover preview, a settings panel); for a menu of
        /// items reach for Z.Menu instead, which composes on top of this.
        public static ZuiPopover Popover(VisualElement anchor, Action<VisualElement> build,
            ZuiPopover.Options options = null)
            => ZuiPopover.Show(anchor, build, options);

        /// A horizontal path strip ("Layer › Bag 2 › Bag 2.1"): the last segment is where you are now
        /// (non-interactive), every other segment is a link back up. See ZuiBreadcrumb for the truncation
        /// and layout rules.
        public static ZuiBreadcrumb Breadcrumb(IReadOnlyList<string> segments, Action<int> onSegmentClicked)
        {
            var b = new ZuiBreadcrumb();
            b.SetPath(segments, onSegmentClicked);
            return b;
        }

        /// A ZUI-styled, richer stand-in for GenericMenu (a fluent builder over Z.Popover): section
        /// headers, labelled items with an optional icon + checkmark, separators, and persistent toggle /
        /// MiniRadio rows. `Z.Menu(anchor).Section(..).Item(..).Show()` — Show() opens it and returns the
        /// ZuiPopover handle. See ZuiMenu for the row semantics (Item closes on click like GenericMenu;
        /// Toggle/Radio stay open).
        public static ZuiMenu Menu(VisualElement anchor) => new ZuiMenu(anchor);
    }

    public static class ZuiExtensions
    {
        /// Explicit width — the sanctioned way to size a control (never leave one to stretch).
        public static T W<T>(this T ve, float width) where T : VisualElement
        {
            ve.style.width = width;
            ve.style.flexGrow = 0f;
            ve.style.flexShrink = 0f;
            return ve;
        }

        /// Explicit height.
        public static T H<T>(this T ve, float height) where T : VisualElement
        {
            ve.style.height = height;
            return ve;
        }

        /// Show/hide without removing from the tree (retained-mode conditional sections).
        public static T Shown<T>(this T ve, bool visible) where T : VisualElement
        {
            ve.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return ve;
        }
    }
}
