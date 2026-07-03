// ZuiMenu — gamepad/keyboard-navigable menus for immediate-mode UI. IMGUI has no focus concept, so
// this owns one: a focused index that wraps, a highlight drawn behind the focused item, and an
// "activate" latch. Input stays the GAME's job (any backend): call MoveFocus(±1) / Activate() from
// your input handling, then draw items inside a ZuiStack. Mouse keeps working — hovering focuses,
// clicking activates — so the same menu serves desktop and deck.
//
//   var s = Zui.BeginStack(Zui.Panel(ZuiAnchor.Center, 300, 260, bg));
//   _menu.Begin();
//   if (_menu.Item(ref s, "Continue")) Close();
//   if (_menu.Item(ref s, "Settings")) OpenSettings();
//   if (_menu.Item(ref s, "Quit"))     QuitGame();
//   _menu.End();

using UnityEngine;

namespace ZuiRuntime
{
    public class ZuiMenu
    {
        public int Focus;
        public Color HighlightColor = new Color(1f, 1f, 1f, 0.14f);

        int _drawIndex;
        int _lastCount;
        bool _activate;

        /// <summary>Move focus by ±1 (call from input, e.g. dpad/stick down = +1). Wraps.</summary>
        public void MoveFocus(int delta)
        {
            if (_lastCount <= 0) return;
            Focus = ((Focus + delta) % _lastCount + _lastCount) % _lastCount;
        }

        /// <summary>Activate the focused item (call from input, e.g. the A button).</summary>
        public void Activate() => _activate = true;

        /// <summary>Call before drawing the frame's items.</summary>
        public void Begin() => _drawIndex = 0;

        /// <summary>
        /// One menu entry on the stack. Returns true when chosen — by mouse click, or by gamepad
        /// Activate() while focused. Hovering with the mouse moves focus, so both inputs agree.
        /// </summary>
        public bool Item(ref ZuiStack s, string label, float pts = 16f)
        {
            int index = _drawIndex++;
            bool focused = index == Focus;

            var style = Zui.ButtonStyle(pts);
            float h = style.CalcSize(new GUIContent(label)).y + UIScale.S(8f);
            var r = s.Next(h);

            if (r.Contains(Event.current.mousePosition)) Focus = index;
            if (focused)
                Zui.FillRect(new Rect(r.x - UIScale.S(4f), r.y, r.width + UIScale.S(8f), r.height), HighlightColor);

            bool clicked = GUI.Button(r, label, style);
            if (ZuiAudit.Recording)
                ZuiAudit.Record(new ZuiDrawRecord
                {
                    Kind = "menuitem", Rect = r, Text = label, FontPx = UIScale.Font(pts),
                    NeededWidth = style.CalcSize(new GUIContent(label)).x, Interactive = true,
                });
            bool activated = focused && _activate;
            return clicked || activated;
        }

        /// <summary>Call after the frame's items; consumes the activate latch and records the item count.</summary>
        public void End()
        {
            _lastCount = _drawIndex;
            if (Focus >= _lastCount) Focus = Mathf.Max(0, _lastCount - 1);
            _activate = false;
        }
    }
}
