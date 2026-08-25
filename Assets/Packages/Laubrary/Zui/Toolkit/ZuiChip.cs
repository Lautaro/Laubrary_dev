// ZuiChip — the standard look for a REFERENCE: a small accentuated pill showing what is currently
// referenced ("[Hero Character]", "[Shoot]"), which you click to pick a different one.
//
// Why a distinct control rather than a dropdown or a text field:
//
//   • A name is typed ONCE where the thing is declared, and PICKED everywhere it is referenced (the
//     standing Laubrary rule). A chip cannot be typed into, so the rule is enforced by the control instead
//     of by whoever writes the window.
//   • References want to be SPOTTABLE. A dropdown looks like every other dropdown, so a window full of
//     references reads as a window full of settings. The pill's tint and weight make "this points at
//     something else" visible at a glance, the same way a latched toggle makes "this is on" visible.
//   • A reference has more to offer than picking — open it, create a new one, clear it. Those belong on a
//     RIGHT-CLICK context card (the same gesture every MultiCont value uses), not on a row of buttons
//     stealing the width of every field that happens to hold a reference.
//
// An optional thumbnail sits INSIDE the pill, and only when there is a real one to show: a chip for
// something with no picture (a Faction, a damage type) lays out as a name alone rather than reserving an
// empty square, per the LauAsset thumbnail rule.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiChip : VisualElement
    {
        readonly Label _label = new Label();
        readonly Label _pick = new Label("▾");
        Image _thumb;
        VisualElement _dot;

        /// Left-click: the chip's ONE action — normally "pick a different one".
        public Action<ZuiChip> OnActivate;
        /// Right-click: the context card (everything else this reference can do).
        public Action<ZuiChip> OnContext;

        /// <summary>Dragging an asset from the Project window onto the chip. Both must be set for the chip to
        /// accept a drop — <see cref="Accepts"/> decides (the dragged object is the right type), <see cref="OnDrop"/>
        /// assigns. Wired because an ObjectField accepts drops, and a control that replaces one must not quietly
        /// take that away.</summary>
        public Func<UnityEngine.Object, bool> Accepts;
        public Action<UnityEngine.Object> OnDrop;

        public ZuiChip(string text, string tooltip, bool empty = false)
        {
            this.tooltip = tooltip;
            AddToClassList("zui-chip");
            _label.AddToClassList("zui-chip__label");
            _label.pickingMode = PickingMode.Ignore;
            Add(_label);

            // The affordance that says "clicking this opens a picker". Without it a chip is only a tinted
            // name, and a cold user reads it as a VALUE — or, per a real complaint about the Chunks window,
            // as a text field they are expected to type an asset name into. Tint and hover alone cannot
            // carry that: hover requires the user to already suspect the thing is interactive.
            //
            // A caret rather than a bespoke picker glyph, because every other ZUI chooser (ZuiBox,
            // ZuiFoldCard, ZuiManagedRef) already draws "▾" for "there is more here when you click" — the
            // chip joins that language instead of inventing a second one, and "▾" is proven to render in
            // the editor font. It is added ONCE, here, so no call site changes: the trailing position
            // survives Thumbnail/SetColourDot, which both Insert(0, …) ahead of the label.
            //
            // PickingMode.Ignore: it is decoration. The whole pill is the click target, so the caret must
            // never swallow the PointerDownEvent that drives OnActivate/OnContext.
            _pick.AddToClassList("zui-chip__pick");
            _pick.pickingMode = PickingMode.Ignore;
            Add(_pick);

            Set(text, empty);

            // Pointer-down, not Clickable: right-click has to be distinguishable, and a Clickable would
            // swallow button 1 as a plain click.
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    if (OnContext == null) return;
                    OnContext(this);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0 || OnActivate == null) return;
                OnActivate(this);
                e.StopPropagation();
            });

            RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (!AcceptsDrag()) return;
                UnityEditor.DragAndDrop.visualMode = UnityEditor.DragAndDropVisualMode.Link;
                AddToClassList("zui-chip--drop");
                e.StopPropagation();
            });
            RegisterCallback<DragLeaveEvent>(_ => RemoveFromClassList("zui-chip--drop"));
            RegisterCallback<DragExitedEvent>(_ => RemoveFromClassList("zui-chip--drop"));
            RegisterCallback<DragPerformEvent>(e =>
            {
                RemoveFromClassList("zui-chip--drop");
                if (!AcceptsDrag()) return;
                OnDrop(Dragged());
                UnityEditor.DragAndDrop.AcceptDrag();
                e.StopPropagation();
            });
        }

        static UnityEngine.Object Dragged()
        {
            var refs = UnityEditor.DragAndDrop.objectReferences;
            return refs != null && refs.Length == 1 ? refs[0] : null;
        }

        bool AcceptsDrag()
        {
            if (Accepts == null || OnDrop == null) return false;
            var o = Dragged();
            return o != null && Accepts(o);
        }

        /// The referenced thing's name, plus whether this is the EMPTY state — styled hollow (no reference
        /// tint) with a dimmed italic name, so "nothing picked yet" never reads as a picked thing called
        /// "· none ·". (This used to promise a DASHED border; UI Toolkit has no `border-style`, so that was
        /// never expressible — see the .zui-chip--empty comment in ZuiToolkit.uss.)
        public void Set(string text, bool empty)
        {
            _label.text = string.IsNullOrEmpty(text) ? "· none ·" : text;
            EnableInClassList("zui-chip--empty", empty || string.IsNullOrEmpty(text));
        }

        /// A thumbnail inside the pill. Null REMOVES the image entirely rather than leaving a blank square —
        /// an empty thumbnail slot claims width and promises a picture that does not exist.
        public Texture2D Thumbnail
        {
            set
            {
                if (value == null)
                {
                    if (_thumb != null) { _thumb.RemoveFromHierarchy(); _thumb = null; }
                    return;
                }
                if (_thumb == null)
                {
                    _thumb = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    _thumb.AddToClassList("zui-chip__thumb");
                    Insert(0, _thumb);
                }
                _thumb.image = value;
            }
        }

        /// <summary>A small solid colour square in the thumbnail's place — for something whose visual identity
        /// IS a colour (a Faction, a damage type, a tile tag) rather than a picture. Honest, and no wider than
        /// the blank square it replaces.</summary>
        public void SetColourDot(Color colour)
        {
            if (_dot == null)
            {
                _dot = new VisualElement { pickingMode = PickingMode.Ignore };
                _dot.AddToClassList("zui-chip__dot");
                Insert(0, _dot);
            }
            _dot.style.backgroundColor = colour;
        }

        /// Cap the pill's width; the name ellipsises rather than pushing the rest of the row off screen.
        public ZuiChip MaxW(float px)
        {
            style.maxWidth = px;
            return this;
        }
    }
}
