using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ZuiRuntime;

namespace Laubrary.InputGuide
{
    /// <summary>
    /// Compact control legend and rebinding overlay with optional authored runtime presentation. A host menu
    /// opens either page through OpenLegend/OpenRebinding; live layout and navigation remain local to this overlay.
    /// </summary>
    public sealed class InputGuideOverlay : MonoBehaviour
    {
        enum Page { Legend, Rebind }

        [Tooltip("Runtime input owner whose live bindings this overlay displays and edits.")]
        [SerializeField] InputGuideRuntime guide;

        [Tooltip("Open the controls legend as soon as this component starts.")]
        [SerializeField] bool showOnStart;

        [Tooltip("Presentation of this overlay. Leave empty to use the shared Resources skin or compatibility defaults.")]
        [SerializeField] ZuiRuntimeSkin presentation;

        // Keep authored panel tints in existing scenes when no semantic skin has been supplied.
        [HideInInspector]
        [SerializeField] Color panelColor = new Color(0.055f, 0.065f, 0.085f, 0.98f);

        readonly List<BindingRow> _bindingRows = new List<BindingRow>();
        readonly ZuiMenu _rebindMenu = new ZuiMenu();
        Page _page;
        InputSchemeKind _rebindScheme = InputSchemeKind.KeyboardMouse;
        InputLegendModel _legend;
        bool _confirmReset;
        GUIStyle _title, _glyph, _caption, _row;
        ZuiRuntimeSkin _skin;
        ZuiOverlayLayout Layout => _skin.overlay;

        public bool Visible { get; private set; }

        struct BindingRow
        {
            public InputActionReference Reference;
            public Guid BindingId;
            public string Category;
            public string Label;
            public string Binding;
        }

        void OnEnable()
        {
            if (guide != null)
            {
                guide.ActiveSchemeChanged += OnSchemeChanged;
                guide.BindingsChanged += Refresh;
            }
            if (showOnStart) OpenLegend();
        }

        void OnDisable()
        {
            if (guide != null)
            {
                guide.ActiveSchemeChanged -= OnSchemeChanged;
                guide.BindingsChanged -= Refresh;
                guide.CancelRebind();
            }
        }

        public void OpenLegend()
        {
            Visible = true;
            _page = Page.Legend;
            _confirmReset = false;
            Refresh();
        }

        public void OpenRebinding()
        {
            Visible = true;
            _page = Page.Rebind;
            _confirmReset = false;
            _rebindScheme = guide != null && guide.ActiveScheme == InputSchemeKind.Gamepad
                ? InputSchemeKind.Gamepad
                : InputSchemeKind.KeyboardMouse;
            Refresh();
        }

        public void Close()
        {
            if (guide != null) guide.CancelRebind();
            Visible = false;
            _confirmReset = false;
        }

        void OnSchemeChanged(InputSchemeKind _) { if (_page == Page.Legend) Refresh(); }

        void Refresh()
        {
            if (guide == null || guide.Catalog == null) return;
            InputSchemeKind scheme = guide.ActiveScheme == InputSchemeKind.Unknown
                ? InputSchemeKind.KeyboardMouse
                : guide.ActiveScheme;
            _legend = InputLegendBuilder.Build(guide, scheme);
            BuildBindingRows();
        }

        void Update()
        {
            if (!Visible || guide == null || guide.IsRebinding) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame))
            {
                Close();
                return;
            }

            if ((kb != null && kb.qKey.wasPressedThisFrame) || (gp != null && gp.leftShoulder.wasPressedThisFrame))
            {
                _page = Page.Legend;
                _confirmReset = false;
            }
            if ((kb != null && kb.eKey.wasPressedThisFrame) || (gp != null && gp.rightShoulder.wasPressedThisFrame))
            {
                _page = Page.Rebind;
                _confirmReset = false;
            }

            if (_page != Page.Rebind) return;
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame)
                      || (gp != null && (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame));
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame)
                        || (gp != null && (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame));
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame)
                        || (gp != null && (gp.dpad.left.wasPressedThisFrame || gp.leftStick.left.wasPressedThisFrame));
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame)
                         || (gp != null && (gp.dpad.right.wasPressedThisFrame || gp.leftStick.right.wasPressedThisFrame));
            bool accept = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                          || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (up) _rebindMenu.MoveFocus(-1);
            if (down) _rebindMenu.MoveFocus(1);
            if (left || right)
            {
                _rebindScheme = _rebindScheme == InputSchemeKind.Gamepad ? InputSchemeKind.KeyboardMouse : InputSchemeKind.Gamepad;
                _confirmReset = false;
                BuildBindingRows();
            }
            if (accept) _rebindMenu.Activate();
        }

        void OnGUI()
        {
            if (!Visible || guide == null || guide.Catalog == null || guide.Actions == null) return;
            EnsureStyles();
            Zui.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), _skin.backdrop);

            float width = Mathf.Min(Screen.width * Layout.viewportFraction.x, UIScale.S(Layout.panelSize.x));
            float height = Mathf.Min(Screen.height * Layout.viewportFraction.y, UIScale.S(Layout.panelSize.y));
            Rect outer = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Color panel = presentation != null || Zui.DefaultRuntimeSkin != null ? _skin.panel : panelColor;
            Rect content = Zui.Panel(outer, panel, Layout.panelPadding);

            float headerH = UIScale.S(Layout.headerHeight);
            Rect header = new Rect(content.x, content.y, content.width, headerH);
            GUI.Label(new Rect(header.x, header.y, header.width * Layout.titleWidthFraction, header.height), "CONTROLS", _title);
            DrawTabs(header);

            float tipH = UIScale.S(Layout.tooltipHeight);
            Rect body = new Rect(content.x, header.yMax + UIScale.S(Layout.bodyGap), content.width,
                content.height - headerH - tipH - UIScale.S(Layout.bodyReservedSpacing));
            if (_page == Page.Legend) DrawLegend(body);
            else DrawRebinding(body);

            string tip = GUI.tooltip;
            Rect tipRect = new Rect(content.x, content.yMax - tipH, content.width, tipH);
            GUI.Label(tipRect, string.IsNullOrEmpty(tip) ? "" : tip, Zui.TextStyle(_skin, ZuiTextRole.Tooltip));

            if (guide.IsRebinding) DrawCapturePrompt(outer);
        }

        void DrawTabs(Rect header)
        {
            float buttonH = UIScale.S(Layout.tabHeight);
            float closeW = UIScale.S(Layout.closeWidth);
            float tabW = UIScale.S(Layout.tabWidth);
            float gap = UIScale.S(Layout.controlGap);
            float y = header.y + (header.height - buttonH) * 0.5f;
            float x = header.xMax - closeW - tabW * 2f - gap * 2f;

            if (Segment(new Rect(x, y, tabW, buttonH), "Legend", "Show the binding graphic for the device used most recently.", _page == Page.Legend))
            {
                _page = Page.Legend;
                _confirmReset = false;
            }
            x += tabW + gap;
            if (Segment(new Rect(x, y, tabW, buttonH), "Rebind", "Change and save keyboard, mouse, or gamepad bindings.", _page == Page.Rebind))
            {
                _page = Page.Rebind;
                _confirmReset = false;
                BuildBindingRows();
            }
            x += tabW + gap;
            if (Button(new Rect(x, y, closeW, buttonH), "Close", "Close the controls overlay.")) Close();
        }

        void DrawLegend(Rect body)
        {
            InputSchemeKind scheme = guide.ActiveScheme == InputSchemeKind.Unknown
                ? InputSchemeKind.KeyboardMouse
                : guide.ActiveScheme;
            float detailsW = Mathf.Clamp(body.width * Layout.detailsWidthFraction,
                UIScale.S(Layout.detailsWidthRange.x), UIScale.S(Layout.detailsWidthRange.y));
            float gap = UIScale.S(Layout.legendGap);
            Rect graphic = new Rect(body.x, body.y + UIScale.S(Layout.graphicTop), body.width - detailsW - gap,
                body.height - UIScale.S(Layout.graphicTop));
            Rect details = new Rect(graphic.xMax + gap, body.y, detailsW, body.height);

            GUI.Label(new Rect(body.x, body.y, graphic.width, UIScale.S(Layout.deviceHeadingHeight)),
                scheme == InputSchemeKind.Gamepad ? "GAMEPAD" : "KEYBOARD / MOUSE", _row);
            if (_legend == null) _legend = InputLegendBuilder.Build(guide, scheme);
            if (scheme == InputSchemeKind.Gamepad)
                ZuiGamepad.DrawFullMap(graphic, _legend.Gamepad, _glyph, _caption);
            else
                ZuiKeyboard.DrawFullMap(graphic, _legend.Keyboard, _glyph, _caption);

            var stack = Zui.BeginScrollStack("InputGuide.Legend", details, Layout.stackGap);
            string category = null;
            var descriptions = guide.Catalog.ActionDescriptions;
            for (int i = 0; i < descriptions.Count; i++)
            {
                var item = descriptions[i];
                if (item == null || !item.showInGuide || item.action == null) continue;
                if (!string.Equals(category, item.category, StringComparison.Ordinal))
                {
                    category = item.category;
                    if (!string.IsNullOrWhiteSpace(category)) stack.Label(category.ToUpperInvariant(), _skin, ZuiTextRole.Category);
                }
                string binding = guide.GetBindingDisplay(item.action, scheme);
                stack.Label(binding + "  " + item.DisplayName, _skin, ZuiTextRole.Body);
                if (!string.IsNullOrWhiteSpace(item.description)) stack.Label(item.description, _skin, ZuiTextRole.Description);
            }
            if (_legend != null && _legend.Overflow.Count > 0)
            {
                stack.Label("OTHER", _skin, ZuiTextRole.Category);
                for (int i = 0; i < _legend.Overflow.Count; i++) stack.Label(_legend.Overflow[i], _skin, ZuiTextRole.Overflow);
            }
            Zui.EndScrollStack(ref stack);
        }

        void DrawRebinding(Rect body)
        {
            float switchH = UIScale.S(Layout.switchHeight);
            Rect switcher = new Rect(body.x, body.y, body.width, switchH);
            float half = (switcher.width - UIScale.S(Layout.controlGap)) * 0.5f;
            if (Segment(new Rect(switcher.x, switcher.y, half, switchH), "Keyboard / Mouse", "Edit keyboard and mouse bindings.", _rebindScheme == InputSchemeKind.KeyboardMouse))
            {
                _rebindScheme = InputSchemeKind.KeyboardMouse;
                _confirmReset = false;
                BuildBindingRows();
            }
            if (Segment(new Rect(switcher.x + half + UIScale.S(Layout.controlGap), switcher.y, half, switchH), "Gamepad", "Edit gamepad bindings.", _rebindScheme == InputSchemeKind.Gamepad))
            {
                _rebindScheme = InputSchemeKind.Gamepad;
                _confirmReset = false;
                BuildBindingRows();
            }

            Rect list = new Rect(body.x, switcher.yMax + UIScale.S(Layout.listGap), body.width, body.height - switchH - UIScale.S(Layout.listGap));
            var stack = Zui.BeginScrollStack("InputGuide.Rebind", list, Layout.stackGap);
            _rebindMenu.Begin();
            string category = null;
            for (int i = 0; i < _bindingRows.Count; i++)
            {
                var row = _bindingRows[i];
                if (!string.Equals(category, row.Category, StringComparison.Ordinal))
                {
                    category = row.Category;
                    if (!string.IsNullOrWhiteSpace(category)) stack.Label(category.ToUpperInvariant(), _skin, ZuiTextRole.Category);
                }
                if (_rebindMenu.Item(ref stack, row.Label + "    [ " + row.Binding + " ]", _skin))
                {
                    _confirmReset = false;
                    guide.BeginRebind(row.Reference, row.BindingId, _rebindScheme);
                }
            }

            stack.Space(Layout.resetGap);
            if (!_confirmReset)
            {
                if (_rebindMenu.Item(ref stack, "Reset defaults…", _skin, ZuiTextRole.Action)) _confirmReset = true;
            }
            else
            {
                if (_rebindMenu.Item(ref stack, "Confirm reset", _skin, ZuiTextRole.Action))
                {
                    guide.ResetBindings();
                    _confirmReset = false;
                }
                if (_rebindMenu.Item(ref stack, "Keep bindings", _skin, ZuiTextRole.Action)) _confirmReset = false;
            }
            _rebindMenu.End();
            Zui.EndScrollStack(ref stack);
        }

        void DrawCapturePrompt(Rect outer)
        {
            Zui.FillRect(outer, _skin.promptBackdrop);
            float width = outer.width * Layout.promptWidthFraction;
            float height = UIScale.S(Layout.promptHeight);
            float pad = UIScale.S(Layout.promptPadding);
            Rect prompt = new Rect(outer.center.x - width * 0.5f, outer.center.y - height * 0.5f, width, height);
            Zui.FillRect(prompt, _skin.promptPanel);
            GUI.Label(new Rect(prompt.x + pad, prompt.y + pad, prompt.width - pad * 2f, UIScale.S(Layout.promptTextHeight)),
                guide.RebindingPrompt ?? "Press a control", Zui.TextStyle(_skin, ZuiTextRole.Prompt));
            GUI.Label(new Rect(prompt.x + pad, prompt.yMax - UIScale.S(Layout.promptHintBottom), prompt.width - pad * 2f, UIScale.S(Layout.promptHintHeight)),
                "Esc / B  Cancel", Zui.TextStyle(_skin, ZuiTextRole.PromptHint));
        }

        void BuildBindingRows()
        {
            _bindingRows.Clear();
            if (guide == null || guide.Catalog == null || guide.Actions == null) return;
            var descriptions = guide.Catalog.ActionDescriptions;
            for (int d = 0; d < descriptions.Count; d++)
            {
                var metadata = descriptions[d];
                if (metadata == null || !metadata.showInGuide || metadata.action == null) continue;
                var action = guide.Resolve(metadata.action);
                if (action == null) continue;
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite || !guide.BindingBelongsToScheme(action, i, _rebindScheme)) continue;
                    string part = binding.isPartOfComposite && !string.IsNullOrWhiteSpace(binding.name)
                        ? " — " + char.ToUpperInvariant(binding.name[0]) + binding.name.Substring(1)
                        : string.Empty;
                    _bindingRows.Add(new BindingRow
                    {
                        Reference = metadata.action,
                        BindingId = binding.id,
                        Category = metadata.category,
                        Label = metadata.DisplayName + part,
                        Binding = action.GetBindingDisplayString(i)
                    });
                }
            }
        }

        bool Button(Rect rect, string label, string tooltip)
        {
            return GUI.Button(rect, new GUIContent(label, tooltip), Zui.ButtonStyle(_skin));
        }

        // Runtime ZUI's navigable-menu pattern draws a persistent highlight behind the focused item. Fixed
        // choices use the same treatment here, but keep it latched on the selected item like a segmented radio.
        bool Segment(Rect rect, string label, string tooltip, bool selected)
            => Zui.Segment(rect, new GUIContent(label, tooltip), selected, _skin);

        void EnsureStyles()
        {
            _skin = Zui.ResolveRuntimeSkin(presentation);
            _title = Zui.TextStyle(_skin, ZuiTextRole.Title);
            _glyph = Zui.TextStyle(_skin, ZuiTextRole.Glyph);
            _caption = Zui.TextStyle(_skin, ZuiTextRole.Caption);
            _row = Zui.TextStyle(_skin, ZuiTextRole.Device);
        }
    }
}
