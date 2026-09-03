// DotGenWindow.Cards — the four module sections: Placement, Selectors, Mutators, Drawers.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE CARD CONTRACT — what W2.2 fills in, and everything the shell already provides for it.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
//
// The shell (DotGenWindow.cs) builds the four sections ONCE, in `BuildCards`, and never touches their
// bodies again. Each section owns a body host element; the four Rebuild* methods below clear their own host
// and refill it. That is the whole contract: rebuild a BODY, never the flow, so the pane the author is
// working in cannot move under them when a selection changes or a card is added.
//
//  Sections and their body hosts (created by BuildCards, already added to the column flow and already
//  listed in the section toggle bar — do not create, re-parent or re-register them):
//      ZuiSection placementSection   → VisualElement placementHost
//      ZuiSection selectorsSection   → VisualElement selectorsHost
//      ZuiSection mutatorsSection    → VisualElement mutatorsHost
//      ZuiSection drawersSection     → VisualElement drawersHost
//
//  The four rebuild entry points (called by the shell on load, on selection change, and after any
//  structural edit — W2.2 replaces the bodies of these four methods and nothing else):
//      void RebuildPlacement()
//      void RebuildSelectors()
//      void RebuildMutators()
//      void RebuildDrawers()
//  All four are called from `RefillSelectionSections()` (DotGenWindow.cs) in that order, followed by
//  MarkDirty(). Each must tolerate `doc == null`, a null selected generator, and being called twice.
//
//  Hover / selection state for the gizmo pass (window-only, never persisted — POC §19.1):
//      string hoveredModuleId   — the module under the pointer, or null. Set it from a card root's
//                                 PointerEnter/PointerLeave via HoverModule(id) / HoverModule(null).
//      string selectedModuleId  — the module whose card was last clicked, or null for "the generator area".
//                                 Set it via SelectModule(id). The shell clears it whenever the selected
//                                 GENERATOR changes, so a stale id can never point into another generator.
//  Both are read by DotGenWindow.Gizmos.cs (W2.3); neither means anything to the document.
//
//  Editing helpers the shell already owns (use these — never mutate the document directly):
//      void Dirty(Action apply, string label = "Edit DotGen")            — record, apply, dirty, invalidate
//      void DirtyRepaintOnly(Action apply, string label = "Edit DotGen") — for edits that change only how the
//                                                                          document is looked at
//      void MarkDirty()                                                  — the picture is stale
//      Action Record(string label)                                       — the `onBeforeMutate` half, for a
//                                                                          control that brackets its own drag
//      void Applied()                                                    — the commit half of the same pair
//      ZuiMicroSlider Dial(label, value, min, max, tooltip, set, defaultValue, decimals, width)
//      ZuiMicroSlider DialInt(label, value, min, max, tooltip, set, defaultValue, width)
//      void RefillSelectionSections()                                    — all four bodies + the generator card
//      void RebuildTree()                                                — the hierarchy rows
//      DotGen doc                                                        — the document (may be null)
//
//  For a reflected module body, `ZuiReflect.Options` should be wired exactly as Pyre wires its modifiers:
//      OnBeforeChange   = Record("Edit DotGen")   (or () => Undo.RecordObject(doc, "…"))
//      OnChanged        = Applied
//      OnStructureChanged = the card's own rebuild (a [ZUIShowIf] gate changed, so the body must redraw)
//  ZuiMicroSlider brackets a drag itself (ZuiUndoGesture), so one drag is one undo step for free, and
//  ZuiReflect now passes the type default so a double-click restores it.
//
//  Module ids are minted by the DOCUMENT, never by a module: `doc.NewId("place" | "sel" | "mut" | "draw")`.
//  A new module instance comes from `DotModuleRegistry.Create(entry)`, which sets its default name and
//  leaves the id blank.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────

using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.DotGen.Editor
{
    public partial class DotGenWindow
    {
        ZuiSection placementSection, selectorsSection, mutatorsSection, drawersSection;
        VisualElement placementHost, selectorsHost, mutatorsHost, drawersHost;

        /// The module under the pointer, and the module whose card was last clicked. Both are properties of
        /// looking at the document, so they live on the window and are never saved.
        string hoveredModuleId;
        string selectedModuleId;

        void BuildCards(VisualElement host, DotGen d)
        {
            placementSection = Z.Section("Placement",
                "How the selected generator fills its area with dots. Placement creates the dots — and, for a "
                + "grid or a row of boxes, the cells a child or a drawer can be measured against.",
                "dotgen.placement");
            placementHost = new VisualElement();
            placementSection.Add(placementHost);
            host.Add(placementSection);

            selectorsSection = Z.Section("Selectors",
                "Reusable strengths over this generator's dots. A selector changes nothing by itself — a "
                + "mutator or a drawer decides what its weight means.",
                "dotgen.selectors");
            selectorsHost = new VisualElement();
            selectorsSection.Add(selectorsHost);
            host.Add(selectorsSection);

            mutatorsSection = Z.Section("Mutators",
                "Moves and removals applied to the dots, in the order listed. Each one reads a selector, or "
                + "every dot equally.",
                "dotgen.mutators");
            mutatorsHost = new VisualElement();
            mutatorsSection.Add(mutatorsHost);
            host.Add(mutatorsSection);

            drawersSection = Z.Section("Drawers",
                "Shapes painted from this generator's areas or cells, after the dots are final. A drawer never "
                + "moves a dot and never changes what spawns.",
                "dotgen.drawers");
            drawersHost = new VisualElement();
            drawersSection.Add(drawersHost);
            host.Add(drawersSection);
        }

        /// Set the module the gizmo pass should follow in Hovered mode. Null clears it.
        void HoverModule(string moduleId)
        {
            if (hoveredModuleId == moduleId) return;
            hoveredModuleId = moduleId;
            if (doc != null && doc.gizmoMode == DotGizmoMode.Hovered) preview?.MarkDirtyRepaint();
        }

        /// Set the module the gizmo pass should follow in Selected mode. Null means the generator's own area.
        void SelectModule(string moduleId)
        {
            if (selectedModuleId == moduleId) return;
            selectedModuleId = moduleId;
            if (doc != null && doc.gizmoMode == DotGizmoMode.Selected) preview?.MarkDirtyRepaint();
        }

        // ── the four bodies (W2.2) ───────────────────────────────────────────────────────────
        // Empty by design at W2.1: the shell, the picture and the hierarchy are complete and operable
        // without them, and filling these four is one self-contained task against the contract above.

        void RebuildPlacement()
        {
            if (placementHost == null) return;
            placementHost.Clear();
        }

        void RebuildSelectors()
        {
            if (selectorsHost == null) return;
            selectorsHost.Clear();
        }

        void RebuildMutators()
        {
            if (mutatorsHost == null) return;
            mutatorsHost.Clear();
        }

        void RebuildDrawers()
        {
            if (drawersHost == null) return;
            drawersHost.Clear();
        }
    }
}
