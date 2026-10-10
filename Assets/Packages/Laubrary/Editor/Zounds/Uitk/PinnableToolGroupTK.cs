using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A group of tools that shows either as a popover from its anchor or pinned into the window's bar (owner, 2026-10-10:
    /// "a new type where we can have them pinned and showing on the bar or just as a popover context menu"). The Zounds
    /// editors' playback options (anchor: the Play button) and edit tools (anchor: the Edit tools button) are both one of
    /// these; never write a second pin/popover mechanism next to it.
    ///
    /// Unpinned (the default): a right-click on the anchor opens the group's content in a popover, with a Pin toggle at its
    /// end. Pinned: the host lays the content out in its bar (<see cref="BuildPinned"/>), the same Pin toggle shows lit
    /// (click it to unpin), and a right-click on the pinned group, away from its controls, or on the anchor, offers Unpin.
    /// The pin is remembered per machine and per group (an editor preference, like the other per-machine view switches;
    /// not part of Undo, being view state).
    ///
    /// The group owns only the presenting: its content (and what it does) is the host's builder, handed a
    /// <see cref="Ctx"/> that says which way it is shown, carries the Pin toggle to place, and lets an action close the
    /// popover after it runs (a setting leaves it open).
    /// </summary>
    internal sealed class PinnableToolGroupTK {

        /// <summary>What the content builder is given.</summary>
        internal sealed class Ctx {
            /// <summary>True: built into the window's bar; false: built into the popover.</summary>
            public bool pinned;
            /// <summary>The Pin toggle, for the builder to place (at the end of its first row, before any variable-width text).</summary>
            public ZuiToggleButton pin;
            /// <summary>An action finished: the popover closes (pinned: nothing).</summary>
            public Action done = () => { };
        }

        readonly string prefKey;
        readonly Func<Ctx, VisualElement> build;
        /// <summary>The group's name in tooltips ("playback options", "edit tools").</summary>
        internal readonly string name;
        /// <summary>Where it opens from, in tooltips ("the Play button").</summary>
        internal readonly string anchorName;
        /// <summary>Where it goes when pinned, in tooltips ("beside Play").</summary>
        internal readonly string pinnedWhere;
        /// <summary>Which side of the anchor the popover prefers.</summary>
        internal ZuiPopover.Side side = ZuiPopover.Side.Below;
        /// <summary>The Pin toggle's height (the host's row height).</summary>
        internal float rowH = 20f;
        /// <summary>After the pin changes: the host lays itself out again.</summary>
        internal Action changed;

        /// <summary>The popover while it is open (kept checks).</summary>
        internal ZuiPopover Popover { get; private set; }
        /// <summary>The Unpin menu last opened (kept checks).</summary>
        internal ZuiPopover UnpinMenu { get; private set; }

        internal PinnableToolGroupTK(string prefKey, string name, string anchorName, string pinnedWhere, Func<Ctx, VisualElement> build) {
            this.prefKey = prefKey; this.name = name; this.anchorName = anchorName; this.pinnedWhere = pinnedWhere; this.build = build;
        }

        /// <summary>Whether this group is pinned on this machine (default: not).</summary>
        internal bool Pinned => EditorPrefs.GetBool(prefKey, false);

        /// <summary>A group's pin by its key, for code that has no group object (kept checks, saving and restoring it).</summary>
        internal static bool IsPinned(string prefKey) => EditorPrefs.GetBool(prefKey, false);
        internal static void SetPinnedQuietly(string prefKey, bool v) => EditorPrefs.SetBool(prefKey, v);

        /// <summary>Pins or unpins, closing whatever popover or menu is open, and lets the host lay itself out again.</summary>
        internal void SetPinned(bool v) {
            ClosePopover();
            if (Pinned == v) return;
            EditorPrefs.SetBool(prefKey, v);
            changed?.Invoke();
        }

        internal void ClosePopover() {
            var p = Popover; Popover = null;
            p?.Close();
            var m = UnpinMenu; UnpinMenu = null;
            m?.Close();
        }

        /// <summary>The anchor's line for its tooltip: how to reach the group now.</summary>
        internal string AnchorHint => Pinned
            ? "The " + name + " are pinned " + pinnedWhere + ". " + (anchorLeftToo ? "Click" : "Right-click") + " here to unpin them."
            : (anchorLeftToo ? "Click or right-click" : "Right-click") + ": the " + name + " (pin them from there to keep them " + pinnedWhere + ").";
        bool anchorLeftToo;

        // ─────────────────────────── the two ways it shows ───────────────────────────

        /// <summary>Opens the group's content as a popover at the anchor (pinned: the Unpin menu instead).</summary>
        internal void Open(VisualElement anchor) {
            if (anchor == null || anchor.panel == null) return;
            if (Pinned) { ShowUnpinMenu(anchor); return; }
            ClosePopover();
            var ctx = new Ctx { pinned = false };
            ctx.pin = PinToggle(false);
            ZuiPopover pop = null;
            pop = Z.Popover(anchor, panel => {
                panel.AddToClassList("zs-toolgroup");
                panel.AddToClassList("zs-toolgroup--popover");
                var content = build(ctx);
                if (content != null) panel.Add(content);
            }, new ZuiPopover.Options { preferredSide = side, onClosed = () => { if (Popover == pop) Popover = null; } });
            Popover = pop;
            ctx.done = () => { if (Popover == pop) ClosePopover(); };
        }

        /// <summary>The group's content for the host's bar while pinned; null while it is not. A right-click on it, away
        /// from its controls, offers Unpin.</summary>
        internal VisualElement BuildPinned() {
            if (!Pinned) return null;
            var ctx = new Ctx { pinned = true };
            ctx.pin = PinToggle(true);
            var content = build(ctx);
            if (content == null) return null;
            content.AddToClassList("zs-toolgroup");
            content.AddToClassList("zs-toolgroup--pinned");
            WireUnpinMenu(content);
            return content;
        }

        /// <summary>The Pin toggle: lit while pinned (a click unpins), unlit in the popover (a click pins).</summary>
        internal ZuiToggleButton PinToggle(bool pinned) {
            string tip = pinned
                ? "The " + name + " are pinned " + pinnedWhere + ". Click to unpin them: they then open from a right-click on " + anchorName + "."
                : "Pin the " + name + ": keep them " + pinnedWhere + " instead of opening them from a right-click on " + anchorName + ".";
            var t = ZS.Toggle("Pin", tip, pinned, v => SetPinned(v), "RichToggle", ZUICornerMask.All, 40f, rowH);
            t.name = "toolgroup-pin";
            t.AddToClassList("zs-toolgroup__pin");
            return t;
        }

        // ─────────────────────────── right-clicks ───────────────────────────

        /// <summary>A right-click on the anchor opens the group (pinned: offers Unpin). With <paramref name="leftClickToo"/>,
        /// so does a left click (an anchor with no other job).</summary>
        internal void WireAnchor(VisualElement anchor, bool leftClickToo = false) {
            anchorLeftToo |= leftClickToo;
            anchor.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                e.StopPropagation();
                Open(anchor);
            }, TrickleDown.TrickleDown);
            if (leftClickToo && anchor is Button b) b.clicked += () => Open(anchor);
        }

        /// <summary>A right-click on <paramref name="e"/> away from its controls offers Unpin while the group is pinned.</summary>
        internal void WireUnpinMenu(VisualElement e) {
            e.RegisterCallback<PointerDownEvent>(ev => {
                if (ev.button != 1 || !Pinned || OnControl(ev.target as VisualElement, e)) return;
                ev.StopPropagation();
                ShowUnpinMenu(ev.target as VisualElement ?? e);
            });
        }

        void ShowUnpinMenu(VisualElement at) {
            ClosePopover();
            UnpinMenu = Z.Menu(at)
                .Item("Unpin", "Take the " + name + " out of the window: they then open from a right-click on " + anchorName + ".",
                      () => SetPinned(false), false, true, "push-pin-slash")
                .Show();
        }

        /// <summary>Whether a press landed on one of the group's controls (they keep their own right-click).</summary>
        static bool OnControl(VisualElement t, VisualElement root) {
            for (var v = t; v != null && v != root; v = v.parent)
                if (v is Button || v is ZuiSkinSlider || v is TextField || v.focusable) return true;
            return false;
        }
    }
}
