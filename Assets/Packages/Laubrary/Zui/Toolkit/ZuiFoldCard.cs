// ZuiFoldCard — make a "card" (a Z.Box(null,null) with a header row + a reflected field body) FOLDABLE:
// a click anywhere on the header collapses the body to just that header, and a second click expands it.
// Used by the parallel modifier-list UIs (PyrePlus, Chunks) so a long stack of modifier cards folds down
// to a scannable column of headers — each still showing its grip / enable / name / ✕, so you can reorder,
// enable, remove or identify a modifier without expanding it. Only the field body hides.
//
// Why a helper and not ZuiBox's own title-fold: a ZuiBox folds its ENTIRE body from its TITLE row, but a
// modifier card's header controls (grip / enable / ✕) live IN the body and must stay visible when
// collapsed — so the card needs a fold that keeps the header and hides only the fields below it. That is
// what this adds, without disturbing ZuiBox.
//
// Fold state persists PER MODIFIER INSTANCE — keyed by the modifier object via a ConditionalWeakTable —
// so it survives the wholesale window rebuilds that undo / reorder / selection trigger (the card element
// is thrown away and rebuilt each time, but the modifier instance is the same), it is collected when the
// modifier is (no leak), and it never drifts the way an index-keyed table would when the list reorders.
// Default = expanded. This mirrors how ZuiValueControl keys its per-control UI state by the ZUIValue
// instance rather than by position.
//
// The header is built by the caller (grip / enable toggle / name / ✕ …); this helper prepends a fold
// caret and wires a Clickable on the header — NOT a raw PointerDownEvent, which does not fire reliably for
// a header inside a ScrollView (the same reason ZuiSection / ZuiBox headers use Clickable). Interactive
// header controls that must NOT fold the card are passed as `nonFolding`, so their pointer-down is stopped
// before it reaches the fold Clickable (a drag grip already stops its own; a bare Toggle / Button does not).
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiFoldCard
    {
        // Weak keys: a removed modifier's fold state is collected with it, and reordering the list never
        // shuffles state between cards (index-keyed state would). The StrongBox is just a mutable bool cell.
        static readonly ConditionalWeakTable<object, StrongBox<bool>> s_expanded = new();

        public static bool IsExpanded(object key)
            => key == null || !s_expanded.TryGetValue(key, out var b) || b.Value;   // default: expanded

        public static void SetExpanded(object key, bool value)
        {
            if (key == null) return;
            if (s_expanded.TryGetValue(key, out var b)) b.Value = value;
            else s_expanded.Add(key, new StrongBox<bool>(value));
        }

        /// Wire `header` to fold `body`, persisting the fold state per `foldKey` (the modifier instance).
        /// A fold caret is prepended to the header and flips ▾/▸ with the state; `body`'s visibility is set
        /// from the persisted state immediately (no flash). `body` may be null — a header-only card (e.g. a
        /// disabled modifier with no fields) has nothing to fold, so no caret is added and the header is not
        /// made clickable. Each control in `nonFolding` gets a pointer-down guard so clicking it never folds
        /// the card (the grip already guards itself; a Toggle / Button does not).
        public static void Wire(object foldKey, VisualElement header, VisualElement body,
            params VisualElement[] nonFolding)
            => Wire(foldKey, header, body, true, nonFolding);

        /// As above, but `showCaret:false` omits the prepended fold caret — the header still folds on click, it
        /// just carries no ▾/▸ glyph (e.g. a fill card whose header is a preview strip, not a titled row).
        public static void Wire(object foldKey, VisualElement header, VisualElement body, bool showCaret,
            params VisualElement[] nonFolding)
        {
            if (header == null || body == null) return;   // nothing to fold

            if (nonFolding != null)
                foreach (var c in nonFolding)
                    c?.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            Label caret = null;
            if (showCaret)
            {
                caret = new Label(IsExpanded(foldKey) ? "▾" : "▸") { pickingMode = PickingMode.Ignore };
                caret.AddToClassList("zui-box__caret");
                header.Insert(0, caret);
            }

            void Apply()
            {
                bool open = IsExpanded(foldKey);
                if (caret != null) caret.text = open ? "▾" : "▸";
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Apply();

            // Clickable (not a raw PointerDownEvent): a header inside a ScrollView does not receive a bare
            // PointerDownEvent reliably — the same reason ZuiSection / ZuiBox use Clickable for their headers.
            header.AddManipulator(new Clickable(() => { SetExpanded(foldKey, !IsExpanded(foldKey)); Apply(); }));
        }
    }
}
