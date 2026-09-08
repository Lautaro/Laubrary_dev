// ZuiBox — the framed block that Z.Box returns. A titled box owns its content outright (unlike a bare
// heading, which has to work out its own extent), so clicking its title row folds it away and leaves
// just the title behind.
//
// Children added to a ZuiBox land in its BODY (contentContainer is overridden), so `box.Add(..)` after
// construction still puts the control inside the box, below the title, exactly as before.
//
// An untitled box has nothing to click and stays a plain frame.
//
// Fold state is static and keyed by title+tooltip so it survives the window rebuilds every dial edit
// triggers — same reasoning as ZuiSection.
//
// ── Gear settings (the "titled container with gear-toggle settings" capability) ─────────────────────
// A consumer can opt individual controls into a settings accordion that a ⚙ gear (right-aligned in the
// title row) opens. Each opt-in control gets a checkbox in the accordion; turning it OFF hides the
// control from the body (display:none), and the gear is where it comes back. Controls can be grouped
// under a bold group toggle that flips all its members at once and shows the built-in "dash" tri-state
// when the group is partly on. The gear appears ONLY when at least one control was made toggleable — a
// box nobody calls Toggleable/ToggleGroup on is byte-for-byte the old plain box (no gear, no accordion,
// no layout shift). All of this view-state (fold + gear-open + each control's on/off) persists across
// window rebuilds via the SAME static-dictionary idiom the fold already uses, and is exposed for a
// view-preset store through CaptureView/ApplyView. Keys are consumer-supplied and stable — never the
// display label — so the state survives a relabel.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiBox : VisualElement
    {
        // All three survive the window rebuilds that undo/redo and structural edits trigger, because
        // they are static and keyed by strings derived from the box's stable key — a new ZuiBox instance
        // built for the same box looks its state right back up. Same idiom as s_open, extended.
        static readonly Dictionary<string, bool> s_open = new();       // "<boxKey>"        → is-open   (default true)
        static readonly Dictionary<string, bool> s_gearOpen = new();   // "<boxKey>/gear"   → gear-open (default false)
        static readonly Dictionary<string, bool> s_controlOn = new();  // "<boxKey>/<ctl>"  → shown     (default true)

        readonly VisualElement _body;
        readonly Label _caret;
        readonly string _key;
        readonly VisualElement _titleRow;   // null for an untitled box (nothing to host a gear)
        Label _title;                       // the title label (null for an untitled box) — updated for the collapsed suffix
        string _titleText;                  // base title text, so a collapsed-only suffix can be appended/removed
        Func<string> _headerSuffix;         // set by SetHeaderSuffix; defaults to none

        // ── gear-settings registrations (populated by Toggleable / ToggleGroup) ──
        sealed class ToggleEntry
        {
            public VisualElement control;
            public string key;          // stable, consumer-supplied — NOT the label
            public string label;
            public string groupKey;     // may be null
            public Toggle toggle;       // the checkbox in the accordion (rebuilt each RebuildGear; may be null)
        }
        readonly List<ToggleEntry> _toggleables = new();
        readonly Dictionary<string, string> _groupLabels = new();   // groupKey → label (set by ToggleGroup)
        readonly Dictionary<string, Toggle> _groupToggles = new();  // groupKey → its accordion toggle (rebuilt)
        // groupKey → an optional consumer-supplied header (a divider/label introducing the group) that is
        // hidden whenever EVERY member of the group is toggled off, and shown again when any turns back on.
        readonly Dictionary<string, VisualElement> _groupHeaders = new();

        Label _gear;                    // the ⚙ glyph in the title row (created lazily)
        VisualElement _settingsWrap;    // the accordion strip at the top of the body (rebuilt)
        bool _gearRefreshScheduled;
        VisualElement _help;            // the "?" glyph — header content inserts before it
        VisualElement _headerContent;   // right-aligned controls hosted ON the title row (created lazily)

        /// Host a control ON the title row, right-aligned before the "?" — view dials that govern the
        /// box's content without costing a body row (a zoom, a display toggle). Clicks inside never fold
        /// the box: the hosted strip swallows its own presses before the row's fold Clickable sees them.
        public void AddHeaderContent(VisualElement e)
        {
            if (_titleRow == null || e == null) return;
            if (_headerContent == null)
            {
                _headerContent = new VisualElement();
                _headerContent.AddToClassList("zui-box__headercontent");
                _headerContent.style.flexDirection = FlexDirection.Row;
                _headerContent.style.alignItems = Align.Center;
                _headerContent.RegisterCallback<PointerDownEvent>(ev => ev.StopPropagation());
                if (_help != null)
                    _titleRow.Insert(_titleRow.IndexOf(_help), _headerContent);
                else
                {
                    // No tooltip means no spacer yet — add one so the content still right-aligns.
                    var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                    spacer.style.flexGrow = 1f;
                    _titleRow.Add(spacer);
                    _titleRow.Add(_headerContent);
                }
            }
            _headerContent.Add(e);
        }

        /// Raised after any toggle/gear/fold change the USER makes (never on a programmatic ApplyView),
        /// so a host window can persist the view immediately.
        public event Action ViewChanged;

        public override VisualElement contentContainer => _body;

        public bool IsOpen
        {
            get => _key == null || !s_open.TryGetValue(_key, out bool open) || open;   // default: open
            set { if (_key != null) { s_open[_key] = value; Apply(); } }
        }

        /// `stateKey` distinguishes boxes that share a title — several identical "Matte" boxes down a layer
        /// list would otherwise fold and unfold together, since fold state is keyed by what the box says.
        /// `icon` (optional, a ZUI icon name) draws a tinted glyph in the title row, between the fold caret and
        /// the title, so a box reads apart at a glance. Off by default — a box that names no icon is unchanged.
        public ZuiBox(string title, string tooltip, string stateKey = null, string icon = null)
        {
            AddToClassList("zui-box");

            _body = new VisualElement();
            _body.AddToClassList("zui-box__body");

            if (!string.IsNullOrEmpty(title))
            {
                _key = stateKey ?? title + "" + (tooltip ?? string.Empty);

                _titleRow = new VisualElement();
                _titleRow.AddToClassList("zui-box__titlerow");
                if (!string.IsNullOrEmpty(tooltip)) _titleRow.tooltip = tooltip;

                _caret = new Label("▾");
                _caret.AddToClassList("zui-box__caret");
                _caret.pickingMode = PickingMode.Ignore;
                _titleRow.Add(_caret);

                // Optional leading icon (between the caret and the title). PickingMode is already Ignore from
                // Z.Icon, so a click on it still reaches the title row's fold Clickable.
                var iconEl = Z.Icon(icon, 13f);
                if (iconEl != null)
                {
                    iconEl.AddToClassList("zui-box__icon");
                    _titleRow.Add(iconEl);
                }

                var t = new Label(title) { pickingMode = PickingMode.Ignore };   // the row is the target
                t.AddToClassList("zui-box__title");
                if (!string.IsNullOrEmpty(tooltip)) t.tooltip = tooltip;
                _titleRow.Add(t);
                _title = t;
                _titleText = title;

                if (!string.IsNullOrEmpty(tooltip))
                {
                    var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                    spacer.style.flexGrow = 1f;
                    _titleRow.Add(spacer);
                    _help = Z.HelpIcon(tooltip);
                    _help.pickingMode = PickingMode.Ignore;
                    _titleRow.Add(_help);
                }

                _titleRow.AddManipulator(new Clickable(() => { IsOpen = !IsOpen; ViewChanged?.Invoke(); }));
                // RIGHT-CLICK the title row toggles the gear settings accordion (when the box has a gear) — a
                // shortcut alongside the ⚙ (which stays, since a toggle needs its visible on/off state). Capture
                // phase + StopPropagation so a right-click never also folds the box.
                _titleRow.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 1 || !HasGear) return;
                    GearOpen = !GearOpen;
                    ViewChanged?.Invoke();
                    e.StopPropagation();
                }, TrickleDown.TrickleDown);
                hierarchy.Add(_titleRow);
            }

            hierarchy.Add(_body);
            ZuiLabelAlign.Align(this);   // line up this box's field labels into one tidy column
            Apply();
        }

        void Apply()
        {
            if (_caret == null) return;
            bool open = IsOpen;
            _caret.text = open ? "▾" : "▸";
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("zui-box--closed", !open);
            // A COLLAPSED box can hide active content (e.g. an enabled feature). Show the suffix its
            // provider returns (e.g. " (on)") on the title while closed; drop it when open. No provider ⇒
            // the title is exactly the base text (every box that never opts in is unchanged).
            if (_title != null)
                _title.text = _titleText + (open ? string.Empty : (_headerSuffix?.Invoke() ?? string.Empty));
        }

        // ── optional header checkbox (created lazily by SetHeaderToggle) ──
        Toggle _headerToggle;
        Action<bool> _headerToggleChanged;

        /// Put the "is this on?" checkbox IN the title row, just left of the title, instead of as an
        /// "Enabled" toggle in the body. A box that holds an optional feature (a sweep, a shell, a swarm, an
        /// edge strip, a border) then reads like a section with a header toggle: the header both names the
        /// thing and switches it, and the body holds only its dials. Clicking the checkbox never folds the
        /// box (pointer-down is stopped before the row's fold Clickable, the trick the gear uses); the rest
        /// of the row stays the fold zone. Idempotent: a second call rebinds and refreshes. No-op on an
        /// untitled box. Same shape as ZuiSection.SetHeaderToggle, so both containers behave alike.
        public void SetHeaderToggle(bool value, string tooltip, Action<bool> onChanged)
        {
            if (_titleRow == null || _title == null) return;
            _headerToggleChanged = onChanged;
            if (_headerToggle == null)
            {
                _headerToggle = new Toggle { tooltip = tooltip };
                _headerToggle.AddToClassList("zui-audit-allow-toggle");   // a header switch, not a checkbox setting
                _headerToggle.AddToClassList("zui-box__toggle");
                _headerToggle.style.marginTop = 0f;
                _headerToggle.style.marginBottom = 0f;
                _headerToggle.style.marginLeft = 0f;
                _headerToggle.style.marginRight = 4f;
                _headerToggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                _headerToggle.RegisterValueChangedCallback(e => _headerToggleChanged?.Invoke(e.newValue));
                int idx = _titleRow.IndexOf(_title);
                if (idx < 0) idx = _titleRow.childCount;
                _titleRow.Insert(idx, _headerToggle);
            }
            else if (!string.IsNullOrEmpty(tooltip)) _headerToggle.tooltip = tooltip;
            _headerToggle.SetValueWithoutNotify(value);
        }

        /// Refresh the header checkbox from outside WITHOUT firing onChanged (after an undo, say). No-op if
        /// SetHeaderToggle was never called.
        public void SetHeaderToggleWithoutNotify(bool value) => _headerToggle?.SetValueWithoutNotify(value);

        /// Give the box header a suffix shown ONLY while the box is COLLAPSED — for surfacing
        /// hidden-but-active content (e.g. "(on)" when a folded box holds an enabled feature). `provider`
        /// returns the whole suffix string; return "" for none. DEFAULTS to no suffix (a box that never
        /// calls this is unchanged). No-op on an untitled box (no title to write to).
        public void SetHeaderSuffix(Func<string> provider)
        {
            _headerSuffix = provider;
            Apply();
        }

        // ── gear-settings public API ───────────────────────────────────────────────────────────────

        /// Opt a control into the gear accordion. `key` must be stable (used to persist its shown/hidden
        /// state — never keyed by the display label); `label` is what the accordion toggle shows.
        /// `groupKey` groups it under a group toggle declared by ToggleGroup() — order-independent (the
        /// control may be registered before or after its group is declared). Returns the control for
        /// chaining: `box.Add(box.Toggleable(ctrl, "cooling", "Cooling", "burst"));`.
        /// A control whose persisted state is OFF is hidden immediately here, so it never flashes visible
        /// on the frame before the accordion is built.
        public VisualElement Toggleable(VisualElement control, string key, string label, string groupKey = null)
        {
            if (control == null || string.IsNullOrEmpty(key)) return control;

            var entry = _toggleables.Find(e => e.key == key);
            if (entry == null)
            {
                entry = new ToggleEntry { key = key };
                _toggleables.Add(entry);
            }
            entry.control = control;
            entry.label = label;
            entry.groupKey = groupKey;

            // Apply the persisted shown/hidden state right now — no flash for a control the user hid.
            control.style.display = ControlOn(key) ? DisplayStyle.Flex : DisplayStyle.None;

            ScheduleGearRefresh();
            return control;
        }

        /// Declare a group so its members get a single bold group toggle (with the dash tri-state) in the
        /// gear. Order-independent with Toggleable — call it whenever is convenient while composing.
        /// Pass <paramref name="headerElement"/> (a divider/label the consumer placed above the group's
        /// controls) to have the box hide it whenever EVERY member of the group is off, and reveal it again
        /// when any turns back on — so an entirely-untoggled sub-section shows no dangling header.
        public void ToggleGroup(string groupKey, string label, VisualElement headerElement = null)
        {
            if (string.IsNullOrEmpty(groupKey)) return;
            _groupLabels[groupKey] = label;
            if (headerElement != null) _groupHeaders[groupKey] = headerElement;
            ApplyGroupHeaderVisibility(groupKey);
            ScheduleGearRefresh();
        }

        /// View-state surface (for a view-preset store): fold + gear + every toggleable's shown/hidden.
        /// Keys: "<boxKey>/fold" (true = open), "<boxKey>/gear" (true = accordion open), and
        /// "<boxKey>/<controlKey>" (true = shown) for each toggleable control.
        public void CaptureView(Dictionary<string, bool> into)
        {
            if (into == null || _key == null) return;   // an untitled box has no persistent identity
            into[_key + "/fold"] = IsOpen;
            if (HasGear) into[_key + "/gear"] = GearOpen;
            foreach (var t in _toggleables) into[_key + "/" + t.key] = ControlOn(t.key);
        }

        /// Restore what CaptureView wrote. Programmatic — does NOT raise ViewChanged.
        public void ApplyView(IReadOnlyDictionary<string, bool> from)
        {
            if (from == null || _key == null) return;
            if (from.TryGetValue(_key + "/fold", out bool open)) IsOpen = open;
            if (from.TryGetValue(_key + "/gear", out bool gear)) GearOpen = gear;
            foreach (var t in _toggleables)
                if (from.TryGetValue(_key + "/" + t.key, out bool on))
                {
                    SetControlOn(t, on);
                    t.toggle?.SetValueWithoutNotify(on);
                }
            RefreshAllGroupToggles();
            RefreshAllGroupHeaders();
        }

        // ── gear-settings internals ──────────────────────────────────────────────────────────────────

        bool HasGear => _titleRow != null && _toggleables.Count > 0;

        bool GearOpen
        {
            get => _key != null && s_gearOpen.TryGetValue(_key + "/gear", out bool v) && v;   // default: closed
            set { if (_key != null) { s_gearOpen[_key + "/gear"] = value; ApplyGear(); } }
        }

        bool ControlOn(string controlKey)
            => _key == null || !s_controlOn.TryGetValue(_key + "/" + controlKey, out bool on) || on;   // default: shown

        void SetControlOn(ToggleEntry t, bool on)
        {
            if (_key != null) s_controlOn[_key + "/" + t.key] = on;
            if (t.control != null) t.control.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // A single coalesced rebuild after composition: the consumer calls Toggleable/ToggleGroup while
        // building the body, and one deferred pass reflects whatever was registered by first layout.
        void ScheduleGearRefresh()
        {
            if (_gearRefreshScheduled) return;
            _gearRefreshScheduled = true;
            // One deferred pass after composition reflects whatever Toggleable/ToggleGroup registered —
            // including the final all-off/any-on state each group header should show.
            schedule.Execute(() => { _gearRefreshScheduled = false; RebuildGear(); RefreshAllGroupHeaders(); });
        }

        void RebuildGear()
        {
            if (_titleRow == null) return;   // untitled: no header to host a gear

            // Tear down the old accordion; it is rebuilt wholesale below.
            if (_settingsWrap != null && _settingsWrap.parent != null) _settingsWrap.RemoveFromHierarchy();
            _settingsWrap = null;

            if (_toggleables.Count == 0)
            {
                if (_gear != null) { _gear.RemoveFromHierarchy(); _gear = null; }
                return;
            }

            // The ⚙ glyph lives in the title row, right-aligned. It is its OWN pointer target (the caret,
            // title and help are PickingMode.Ignore so clicks on them fold the box); a Clickable drives it
            // and stops the pointer-down from reaching the title row's fold Clickable, so opening settings
            // never also folds the box. The explicit StopPropagation guard makes that independent of
            // Clickable's internal propagation behaviour.
            if (_gear == null)
            {
                _gear = new Label("⚙") { tooltip = "Which controls are shown" };
                _gear.AddToClassList("zui-togglebutton");   // palette-driven (accent-soft when on) — no new USS
                _gear.style.marginLeft = StyleKeyword.Auto; // right-align in the header row
                _gear.style.fontSize = 12f;
                _gear.AddManipulator(new Clickable(() => { GearOpen = !GearOpen; ViewChanged?.Invoke(); }));
                _gear.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                _titleRow.Add(_gear);
            }

            _settingsWrap = BuildSettings();
            _body.Insert(0, _settingsWrap);   // top of the body
            ApplyGear();
            RefreshAllGroupToggles();
        }

        // The framed strip listing group toggles (bold) and per-control toggles (indented under a group).
        // Groups appear at the position of their first member, with every member clustered there; an
        // ungrouped control (null groupKey, or a groupKey never declared via ToggleGroup) sits at the
        // top level in its own registration order. Deterministic and order-preserving.
        VisualElement BuildSettings()
        {
            _groupToggles.Clear();

            var wrap = new VisualElement();
            wrap.AddToClassList("zui-box");     // a nested .zui-box is a quiet sub-fill inset strip (palette-driven)
            wrap.style.marginTop = 0;
            wrap.style.marginBottom = 6;

            var cap = new Label("Shown controls");
            cap.AddToClassList("zui-text--small");
            cap.style.marginBottom = 2;
            wrap.Add(cap);

            var emittedGroups = new HashSet<string>();
            foreach (var t in _toggleables)
            {
                string g = DeclaredGroup(t.groupKey);
                if (g != null)
                {
                    if (emittedGroups.Add(g))
                    {
                        wrap.Add(BuildGroupToggle(g));
                        foreach (var m in _toggleables)
                            if (m.groupKey == g) wrap.Add(BuildControlToggle(m, indent: 14f));
                    }
                    // else: already emitted alongside the group's first member
                }
                else
                {
                    wrap.Add(BuildControlToggle(t, indent: 0f));
                }
            }
            return wrap;
        }

        string DeclaredGroup(string groupKey)
            => !string.IsNullOrEmpty(groupKey) && _groupLabels.ContainsKey(groupKey) ? groupKey : null;

        Toggle BuildControlToggle(ToggleEntry t, float indent)
        {
            var tog = new Toggle(t.label) { tooltip = "Show or hide " + (t.label ?? t.key) };
            tog.AddToClassList("zui-audit-allow-toggle");   // gear-strip chrome, pending a gear restyle
            tog.SetValueWithoutNotify(ControlOn(t.key));
            tog.style.marginLeft = indent;
            tog.style.marginTop = 1f;
            tog.style.marginBottom = 1f;
            var self = t;
            tog.RegisterValueChangedCallback(ev =>
            {
                SetControlOn(self, ev.newValue);
                if (!string.IsNullOrEmpty(self.groupKey))
                {
                    RefreshGroupToggle(self.groupKey);
                    ApplyGroupHeaderVisibility(self.groupKey);   // last member off → drop the header
                }
                ViewChanged?.Invoke();
            });
            t.toggle = tog;
            return tog;
        }

        Toggle BuildGroupToggle(string groupKey)
        {
            string label = _groupLabels.TryGetValue(groupKey, out var l) && !string.IsNullOrEmpty(l) ? l : groupKey;
            var tog = new Toggle(label) { tooltip = "Show or hide all " + label + " controls" };
            tog.AddToClassList("zui-audit-allow-toggle");   // gear-strip chrome, pending a gear restyle
            var lbl = tog.Q<Label>();
            if (lbl != null) lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            tog.style.marginTop = 2f;
            tog.style.marginBottom = 1f;
            _groupToggles[groupKey] = tog;
            ApplyGroupState(groupKey, tog);   // initial value + dash
            tog.RegisterValueChangedCallback(ev =>
            {
                foreach (var m in _toggleables)
                    if (m.groupKey == groupKey)
                    {
                        SetControlOn(m, ev.newValue);
                        m.toggle?.SetValueWithoutNotify(ev.newValue);
                    }
                RefreshGroupToggle(groupKey);   // clears the dash now the members agree
                ApplyGroupHeaderVisibility(groupKey);   // whole group toggled at once → header follows
                ViewChanged?.Invoke();
            });
            return tog;
        }

        void RefreshGroupToggle(string groupKey)
        {
            if (_groupToggles.TryGetValue(groupKey, out var tog) && tog != null) ApplyGroupState(groupKey, tog);
        }

        void RefreshAllGroupToggles()
        {
            foreach (var kv in _groupToggles)
                if (kv.Value != null) ApplyGroupState(kv.Key, kv.Value);
        }

        void RefreshAllGroupHeaders()
        {
            foreach (var kv in _groupHeaders)
                ApplyGroupHeaderVisibility(kv.Key);
        }

        // Hide a group's registered header element when every member is off; show it when any is on.
        // A group whose members haven't registered yet counts as "nothing to hide" and stays visible —
        // the deferred RefreshAllGroupHeaders after composition settles it to the real state.
        void ApplyGroupHeaderVisibility(string groupKey)
        {
            if (!_groupHeaders.TryGetValue(groupKey, out var header) || header == null) return;
            bool anyMember = false, anyOn = false;
            foreach (var m in _toggleables)
                if (m.groupKey == groupKey)
                {
                    anyMember = true;
                    if (ControlOn(m.key)) { anyOn = true; break; }
                }
            bool show = !anyMember || anyOn;
            header.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ApplyGroupState(string groupKey, Toggle tog)
        {
            int total = 0, onCount = 0;
            foreach (var m in _toggleables)
                if (m.groupKey == groupKey) { total++; if (ControlOn(m.key)) onCount++; }
            bool allOn = total > 0 && onCount == total;
            bool noneOn = onCount == 0;
            tog.showMixedValue = !(allOn || noneOn);   // the built-in dash for a partly-on group
            tog.SetValueWithoutNotify(allOn);
        }

        void ApplyGear()
        {
            if (_settingsWrap != null)
                _settingsWrap.style.display = GearOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_gear != null)
                _gear.EnableInClassList("zui-togglebutton--on", GearOpen);
        }
    }
}
