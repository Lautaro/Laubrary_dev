using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A floating card that opens at a point, can be DRAGGED by its header, and can be PINNED so it survives
    /// the actions and outside clicks that would otherwise dismiss it.
    ///
    /// This is the shape a canvas tool needs and a `ZuiMenu` deliberately is not: a menu is a transient
    /// one-decision flyout, while a tool card is a small workspace the user keeps beside the thing they are
    /// editing — sometimes for a whole session, which is what the pin is for. The Tileset Builder's grid
    /// actions card grew this behaviour first; the Cartographer canvas wanted the same thing, and a third
    /// hand-rolled copy is where a pattern becomes a control.
    ///
    /// Only the CHROME lives here — the frame, the drag, the pin, the outside-click dismissal. What goes
    /// inside is the caller's, rebuilt in place through `Rebuild()` so switching modes never moves the card
    /// or drops the pin.
    ///
    /// Consumers: the Cartographer canvas's Tool card, and the tileset grid's Selection card — the latter
    /// being the hand-rolled original, retrofitted onto this class 2026-08-03 when the grid itself was
    /// extracted into the shared `TilesetGridView`.
    public sealed class ZuiPinCard
    {
        readonly VisualElement root;
        readonly VisualElement card;
        readonly VisualElement body;
        readonly Action<VisualElement> buildBody;
        EventCallback<PointerDownEvent> outsideHandler;

        /// Whether the user has pinned this card open. A pinned card ignores outside clicks and survives its
        /// own action buttons; an unpinned one behaves like a menu.
        public bool Pinned { get; private set; }

        /// True while the card is in the visual tree.
        public bool IsOpen => card != null && card.parent != null;

        ZuiPinCard(VisualElement root, VisualElement card, VisualElement body, Action<VisualElement> buildBody, bool pinned)
        {
            this.root = root;
            this.card = card;
            this.body = body;
            this.buildBody = buildBody;
            Pinned = pinned;
        }

        /// THE ENTRY POINT TO USE. Open the card the gesture asks for, re-using whatever this caller had
        /// open before (pass null the first time; assign the result back to your field).
        ///
        /// The rule that makes this the right overload: **a card that is open AND pinned does not move.**
        /// Pinning is the user saying "leave this where I put it" — re-firing the gesture somewhere else
        /// must refresh the card and raise it, never teleport it to the pointer. Yanking a parked
        /// workspace out from under someone is the same reflow the stable-workspace rule forbids, and it
        /// makes the pin useless: the one thing a pin should guarantee is that the card stays put.
        ///
        /// An UNPINNED card behaves like a menu — it re-opens at the new point, which is what an unpinned
        /// flyout is for. A card that was pinned and then closed carries its pin forward, because the pin
        /// is a preference about the card, not about that one appearance of it.
        public static ZuiPinCard Show(ZuiPinCard existing, VisualElement host, Vector2 panelPos,
            string title, string tooltip, Action<VisualElement> buildBody)
        {
            if (existing != null && existing.IsOpen && existing.Pinned)
            {
                existing.Rebuild();   // content tracks the current state; position deliberately does not
                existing.Raise();
                return existing;
            }
            bool carryPin = existing != null && existing.Pinned;
            existing?.Close();
            return Show(host, panelPos, title, tooltip, buildBody, carryPin);
        }

        /// Unconditionally open a NEW card at `panelPos` (a world/panel-space point, e.g. a
        /// PointerDownEvent's `position`). Prefer the overload above — this one has no memory of a card
        /// already on screen, so it will happily move a pinned one or leave two stacked up.
        ///
        /// `title` names the card, `buildBody` fills it and is re-run by `Rebuild()`, and `startPinned`
        /// carries a pin state across a close/reopen cycle.
        public static ZuiPinCard Show(VisualElement host, Vector2 panelPos, string title, string tooltip,
            Action<VisualElement> buildBody, bool startPinned = false)
        {
            if (host?.panel == null || buildBody == null) return null;
            // The card lives inside the HOST, not in the panel's root: a window's own tree is where its
            // chrome belongs, and anything parented above it escapes the window's coordinate space (and
            // its stylesheet). Host must therefore be something window-sized and unclipped —
            // rootVisualElement, not the small element the click came from.
            var root = host;
            var local = root.WorldToLocal(panelPos);

            var card = new VisualElement();
            card.AddToClassList("zui-pin-card");
            card.style.position = Position.Absolute;
            card.style.left = local.x;
            card.style.top = local.y;
            card.style.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 0.98f);
            card.style.borderTopWidth = card.style.borderBottomWidth =
                card.style.borderLeftWidth = card.style.borderRightWidth = 1f;
            var edge = new Color(0f, 0f, 0f, 0.85f);
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = edge;
            card.style.paddingLeft = card.style.paddingRight =
                card.style.paddingTop = card.style.paddingBottom = 4f;
            card.style.minWidth = 190f;

            var bodyEl = new VisualElement();
            var instance = new ZuiPinCard(root, card, bodyEl, buildBody, startPinned);

            var header = Z.Row(
                Z.Text(title, ZuiText.Small, tooltip),
                Z.Flexible(),
                Z.ToggleButton("Pin", "Keep this card open after actions and clicks elsewhere.",
                    startPinned, v => instance.Pinned = v),
                Z.Button("×", "Close this card.", instance.Close).W(20f));
            MakeDraggable(header, card);
            card.Add(header);
            card.Add(bodyEl);
            instance.Rebuild();

            instance.outsideHandler = e =>
            {
                if (instance.Pinned || !instance.IsOpen) return;
                if (!card.worldBound.Contains((Vector2)e.position)) instance.Close();
            };
            root.RegisterCallback(instance.outsideHandler, TrickleDown.TrickleDown);

            root.Add(card);
            card.BringToFront();
            return instance;
        }

        /// Refill the card's body without moving the card or touching its pin — what a mode switch inside
        /// the card wants, and the reason the body is a separate element from the frame.
        public void Rebuild()
        {
            if (body == null) return;
            body.Clear();
            buildBody(body);
        }

        /// What an ACTION inside the card should call when it finishes: a pinned card stays and refreshes
        /// (so enabled-ness tracks the new state), an unpinned one closes like a menu item would.
        public void AfterAction()
        {
            if (Pinned) Rebuild();
            else Close();
        }

        /// Lift the card above anything added to the host since it opened — what a re-fired gesture does
        /// instead of moving a pinned card, so the answer to "where did my card go" is never "buried".
        public void Raise() => card?.BringToFront();

        public void Close()
        {
            if (outsideHandler != null)
            {
                root?.UnregisterCallback(outsideHandler, TrickleDown.TrickleDown);
                outsideHandler = null;
            }
            card?.RemoveFromHierarchy();
        }

        /// Drag the whole card by `handle`. Pointer capture is what makes this survive a fast drag leaving
        /// the header's own rect mid-gesture.
        static void MakeDraggable(VisualElement handle, VisualElement target)
        {
            bool dragging = false;
            Vector2 grabOffset = default;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                grabOffset = (Vector2)e.position - new Vector2(target.resolvedStyle.left, target.resolvedStyle.top);
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                var p = (Vector2)e.position - grabOffset;
                target.style.left = p.x;
                target.style.top = p.y;
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(e.pointerId);
            });
        }
    }
}
