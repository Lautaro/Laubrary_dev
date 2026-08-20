// PyrePlusWindow.CherryFraming — the lower-right UITK panel for cherry-picking frames from the spec's own
// baked Pyre animation. Two stacked foldable boxes: the SOURCE grid on top (every baked frame as a clickable
// thumbnail, click to add to the cherry sequence) and the CHERRY slots on the bottom (one card per slot, with
// a drag-reorder grip, a remove ×, a click-to-remove body, and a right-click popover holding the slot's
// Variable Length + MultiFrame controls as LIVE ZUI widgets — the same idiom the SpriteFx / Pyre modifier
// stacks use). Preview playback options (Delay, Zound) live in a controls row above both grids.
//
// All edits route through Undo.RecordObject + EditorUtility.SetDirty via the window's `Dirty` helper, so undo
// works on every grid mutation. The tile thumbnails are Texture2D objects from `cherryStripCache` (separate
// from the main preview's filmstrip cache, so turning Strip off never blanks this panel).
using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // ── Cherry panel host (UITK) ─────────────────────────────────────────────────────────
        // The section lives in the UITK tree so the controls (toggle, delay, zound) flow with the rest of the
        // window's layout. Inside, a ScrollView holds the controls + two foldable boxes (Source + Cherry), so a
        // long cherry sequence or a narrow window scrolls cleanly instead of clipping tiles.
        VisualElement cherryPanelHost;
        ScrollView cherryScroll;
        // The two collapsible boxes inside the scroll view, rebuilt when the cherry list changes.
        ZuiBox cherrySourceBox;
        ZuiBox cherrySlotsBox;
        // Source-grid host: refilled whenever frameCount, canvasSize, or the source cache invalidates.
        VisualElement cherrySourceGridHost;
        // Cherry-slots host: refilled on every structural change (add / remove / reorder / right-click popover edit).
        VisualElement cherrySlotsHost;
        // The original parent (rightPane) of cherryPanelHost, captured at build time so a RebuildCherryPanel
        // (triggered by the Enabled toggle) re-parents the new panel back into the same column flow instead of
        // attaching it to rootVisualElement (which is what the toggle used to do — the panel would end up
        // stacked at the bottom of the entire window, below the dials/preview/chrome).
        VisualElement cherryPanelParent;
        // ── Selection state ─────────────────────────────────────────────────────────────────────
        // Two parallel sets of indices into the two grids. Cherry uses indices into s.cherryFrames; source uses
        // indices into the baked frame range (0..s.frameCount-1). Both are cleared on any structural change
        // (add/remove/reorder) because indices would point at the wrong row otherwise. Selection lives on the
        // WINDOW (not the spec), so closing the window or switching assets discards it.
        readonly HashSet<int> cherrySelected = new HashSet<int>();
        readonly HashSet<int> sourceSelected = new HashSet<int>();
        int cherrySelectionAnchor = -1;   // the "last clicked" cherry index — Shift+click extends FROM this
        int sourceSelectionAnchor = -1;   // the same idea for source tiles
        // "Primary" selected index (ZuiThumbGrid needs one even though Cherry never reads it back) and the
        // in-flight drag snapshot. WINDOW-level (not per-card closures) because a drag gesture spans two
        // separate UITK events fired on two DIFFERENT cards (press on card A, release on card B) — a per-card
        // closure on B would never see what was armed on A.
        int cherryPrimary = -1;
        int cherryDragFrom = -1;
        List<int> cherryDragSelection;
        // Per-tile tile-size knob, persisted on the spec (`previewCherryStripSize`) so the chosen size survives reloads.
        const float CherryTilePxMin = 32f, CherryTilePxMax = 256f;
        const float CherryTileDefaultPx = 96f;
        // The width reserved for the variable-length badge in the corner of each cherry tile. Drives the inset
        // of the lower-right text element so a 2-digit number stays inside the tile.
        const float CherryLengthBadgeW = 28f, CherryLengthBadgeH = 14f;
        // The fade-in delete button. Half on, half off the top-right corner of the card (offset by 50% of its own
        // size) so it reads as a "ribbon" attached to the card. Starts hidden; reveals on card hover via USS.
        const float CherryDeleteBtnSize = 22f;
        // USS class names (added to ZuiToolkit.uss too) for card selection + hover-revealed delete button.
        const string UssCherryCard = "zui-cherry-card";
        const string UssCherryCardSelected = "zui-cherry-card--selected";
        const string UssCherryCardDelete = "zui-cherry-card__delete";
        const string UssSourceTile = "zui-source-tile";
        const string UssSourceTileSelected = "zui-source-tile--selected";

        // ── Section builder ──────────────────────────────────────────────────────────────
        void BuildCherryPanel(VisualElement root, PyrePlusSpec s)
        {
            cherryPanelHost = new VisualElement();
            cherryPanelHost.style.flexGrow = 1f;
            cherryPanelHost.style.minHeight = 200f;
            cherryPanelHost.style.flexShrink = 1f;
            // Capture the parent the FIRST time we build, so a subsequent RebuildCherryPanel (triggered by
            // toggling Enabled or picking a Zound) re-attaches the new panel back into the rightPane column flow
            // instead of stacking at the bottom of rootVisualElement. The parameter `root` IS that parent —
            // rightPane in the BuildAsset call site — and we trust it; if cherryPanelParent ever goes stale the
            // Rebuild falls back to rootVisualElement (a graceful misplacement rather than an exception).
            cherryPanelParent = root;
            root.Add(cherryPanelHost);

            // The collapsible section. Stable key so saved views don't orphan the fold state on a title reword.
            // The section's HEADER carries the Enabled toggle (Z.Section.SetHeaderToggle, same idiom the Swarm
            // section uses) — checked = the cherry sequencer drives the preview; unchecked = the raw frame
            // preview shows. The toggle IS data (previewCherryDelay = 0 when off, transport reveals nothing
            // about cherry), not view state, so it lives on the spec (previewCherryDelay-not-applicable state is
            // inferred from the bool, not stored separately).
            var section = Z.Section("CherryFraming",
                "Cherry-pick frames from the spec's own baked animation. When the header toggle is on, the main " +
                "preview plays the cherry sequence (looping, with optional Delay + Zound). Click a source frame " +
                "(top) to add it to the cherry sequence; drag a cherry card to reorder; right-click a cherry card " +
                "for Variable Length (Static / MinMax) and MultiFrame options. Empty cherry = blank preview.",
                "pyreplus.cherry", icon: "frame-corners");
            section.SetHeaderToggle(s.cherryEnabled,
                "Turn CherryFraming on. The main preview then plays the cherry sequence (or blank if the cherry list is empty).",
                v =>
                {
                    Dirty(() => s.cherryEnabled = v);
                    if (!v) ResetCherryPlayback();
                    RebuildTransport(s);
                    preview?.MarkDirtyRepaint();
                    RebuildCherryPanel(s);
                });

            // ScrollView wraps the whole interior so a tall cherry list (or a narrow window) scrolls instead of
            // clipping the bottom of the grid. ScrollViewMode.Vertical is the standard idiom.
            cherryScroll = new ScrollView(ScrollViewMode.Vertical);
            cherryScroll.style.flexGrow = 1f;
            cherryScroll.style.minHeight = 240f;
            cherryScroll.Add(section);
            cherryPanelHost.Add(cherryScroll);

            // Disabled: draw nothing but the header (matches the Swarm section's "Off = body shows nothing"
            // idiom). The controls row + both grids build real UI state (selection, drag hosts) that has no
            // meaning while cherry is off, so skip building them entirely rather than hiding them via style.
            if (!s.cherryEnabled)
            {
                cherrySourceBox = null; cherrySlotsBox = null;
                cherrySourceGridHost = null; cherrySlotsHost = null;
                return;
            }

            // ── Controls row (tile size + zound + zound frame) ────────────────────────────────────────
            // Delay moved to the transport row alongside Zoom and Speed (it's a TRANSPORT concern, not a cherry-
            // list concern — applies regardless of whether cherry is on). The Enabled toggle moved to the section
            // header (above). Everything else stays the same.
            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.flexWrap = Wrap.Wrap;
            controls.style.flexShrink = 0f;

            controls.Add(Z.MicroSlider("Tile px", s.previewCherryStripSize, CherryTilePxMin, CherryTilePxMax,
                "Size of each frame tile in the cherry grids, in pixels (32–256). Cosmetic — does not re-render frames.",
                v =>
                {
                    Undo.RecordObject(s, "Edit Pyre Plus");
                    s.previewCherryStripSize = Mathf.Clamp(Mathf.RoundToInt(v), (int)CherryTilePxMin, (int)CherryTilePxMax);
                    EditorUtility.SetDirty(s);
                    RebuildCherryGrids(s);
                }, 140f, showValue: true, decimals: 0));

            // Zound — by name. The field is a horizontal row: the current name as a Label + a "Pick…" button that
            // opens the project's existing ZoundPickerPopup (the same one the Laumination Builder's frame-event rows
            // use). Empty string = no sound.
            controls.Add(BuildZoundPickerField(s));
            controls.Add(Z.HGroup(
                Z.MicroSlider("Zound frame", s.previewZoundFrame, 0f, 64f,
                    "Which beat of the preview loop the Zound plays on. 0 = on the first beat of the first iteration. " +
                    "If the Zound's target beat is past the end of the cherry sequence, it fires at sequence end.",
                    v =>
                    {
                        Undo.RecordObject(s, "Edit Pyre Plus");
                        s.previewZoundFrame = Mathf.Clamp(Mathf.RoundToInt(v), 0, 64);
                        EditorUtility.SetDirty(s);
                    }, 200f, showValue: true, decimals: 0),
                Z.Button("Clear",
                    "Clear the current Zound selection (no sound).",
                    () =>
                    {
                        Undo.RecordObject(s, "Edit Pyre Plus");
                        s.previewZoundName = "";
                        EditorUtility.SetDirty(s);
                        RebuildCherryPanel(s);
                    })
            ));

            section.Add(controls);

            // ── Source grid box (top) ────────────────────────────────────────────────────────────────
            cherrySourceBox = Z.BoxKeyed("Source frames",
                "Every baked Pyre frame as a clickable thumbnail. Click one to APPEND it to the cherry sequence. " +
                "The list is the spec's own frame cache — there is no external animation to point at.",
                "pyreplus.cherry.source", icon: "image-multiple");
            cherrySourceGridHost = new VisualElement();
            cherrySourceGridHost.style.flexDirection = FlexDirection.Row;
            cherrySourceGridHost.style.flexWrap = Wrap.Wrap;
            cherrySourceGridHost.style.flexShrink = 0f;
            cherrySourceBox.Add(cherrySourceGridHost);
            section.Add(cherrySourceBox);

            // ── Cherry slots box (bottom) ─────────────────────────────────────────────────────────────
            cherrySlotsBox = Z.BoxKeyed("Cherry slots",
                "The cherry sequence the preview plays. Click a card to SELECT (Shift / Ctrl to multi-select); " +
                "right-click for the Variable Length (Static / MinMax) and MultiFrame popover; drag a card to reorder " +
                "(if it's part of a multi-selection, all selected move together); Delete removes the selection; " +
                "Ctrl+D duplicates it. The lower-right number is the slot's resolved length multiplier (× beats). " +
                "The × button (top-right corner, half off the card) deletes just that one slot on hover.",
                "pyreplus.cherry.slots", icon: "stack");
            cherrySlotsHost = new VisualElement();
            cherrySlotsHost.style.flexDirection = FlexDirection.Row;
            cherrySlotsHost.style.flexWrap = Wrap.Wrap;
            cherrySlotsHost.style.flexShrink = 0f;
            // Keyboard shortcuts (Delete / Ctrl+D) need an element that captures KeyDown — install on the host.
            // The handler ignores key events whose target is a TextField / TextInputBaseField so typing into the
            // popover's source-frames field doesn't lose the user's text.
            cherrySlotsHost.RegisterCallback<KeyDownEvent>(CherryKeyboardShortcuts);
            // A release that lands in the host's own empty space (not on any card — e.g. dragging past the
            // last card in a row) never reaches a card's PointerUp handler. Clear the armed drag here so a
            // later press doesn't inherit a stale snapshot.
            cherrySlotsHost.RegisterCallback<PointerUpEvent>(e => { cherryDragFrom = -1; cherryDragSelection = null; });
            cherrySlotsBox.Add(cherrySlotsHost);
            section.Add(cherrySlotsBox);

            // Initial fill of both grids. Wrapped in a refresh that respects the cherryStripCache, so the
            // thumbnails come from the existing source-frame textures (no re-render).
            RebuildCherryGrids(s);
        }

        // Rebuild the entire cherry panel from scratch. The new panel goes back into the SAME parent as the old
        // one (the captured `cherryPanelParent`, which is rightPane in BuildAsset's call site), so it stays in
        // the column flow after the rebuild — the previous version passed rootVisualElement here, which detached
        // the panel from the layout and stacked it at the bottom of the window (Bug: Enabled toggle misplacement).
        // The clear() before the rebuild removes the OLD host from its parent; we then build a NEW host and add
        // it back to the captured parent. We also clear selection, because the previous selection's indices no
        // longer map to a meaningful row identity until the next RebuildCherryGrids.
        void RebuildCherryPanel(PyrePlusSpec s)
        {
            if (cherryPanelHost == null) return;
            cherryPanelHost.RemoveFromHierarchy();
            cherryPanelHost = null;
            ClearCherrySelection();
            ClearSourceSelection();
            BuildCherryPanel(cherryPanelParent ?? rootVisualElement, s);
        }

        // Refill both grids WITHOUT rebuilding the whole panel — preserves scroll position + fold state + the
        // user's place in the controls. Called on tile-size change and on any cherry-list structural change.
        void RebuildCherryGrids(PyrePlusSpec s)
        {
            if (cherrySourceGridHost == null || cherrySlotsHost == null || s == null) return;
            cherrySourceGridHost.Clear();
            cherrySlotsHost.Clear();
            EnsureCherryStripCache(s);
            BuildCherrySourceGrid(s);
            BuildCherrySlotsGrid(s);
        }

        // ── Zound picker field ─────────────────────────────────────────────────────────────────────
        // The picker returns a Zound NAME (string), matching ZoundsProject's own addressing. The field is a
        // horizontal row: the current name as a Label (or "—" when empty) + a "Pick…" button that opens the
        // project's existing ZoundPickerPopup (the same one the Laumination Builder's frame-event rows use).
        VisualElement BuildZoundPickerField(PyrePlusSpec s)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 0f;
            row.style.alignItems = Align.Center;

            var nameLabel = new Label(string.IsNullOrEmpty(s.previewZoundName) ? "—" : s.previewZoundName)
            {
                tooltip = "The Zound that will play on loop start (preview only). Empty = no sound.",
            };
            nameLabel.style.width = 160f;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.AddToClassList("zui-text");
            row.Add(nameLabel);

            var pickBtn = Z.Button("Pick…",
                "Open the Zound picker. The picked Zound is stored by name and played via ZoundEngine.PlayZound.",
                () =>
                {
                    Vector2 pos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                    Laubrary.LaunimatorZounds.Editor.ZoundPickerPopup.Show(pos, picked =>
                    {
                        Undo.RecordObject(s, "Pick Zound");
                        s.previewZoundName = picked ?? "";
                        EditorUtility.SetDirty(s);
                        RebuildCherryPanel(s);
                    });
                });
            row.Add(pickBtn);

            return row;
        }

        // ── Source grid (UITK) ───────────────────────────────────────────────────────────────────────────
        // One VisualElement per source frame, sized by previewCherryStripSize, texture = cherryStripCache[i]. The
        // element's background-image IS the thumbnail (UITK's StyleBackground takes a Texture2D directly), so
        // there's no per-tile IMGUI repaint — the layout engine handles everything.
        //
        // Interaction (the source is the spec's own procgen animation — read-only here):
        //   • Click            → single-select (replace selection with this index)
        //   • Shift+click     → extend selection from anchor to here
        //   • Ctrl/Cmd+click  → toggle this index in the selection
        //   • Double-click    → append this single source frame to the cherry sequence (quick-add convenience)
        // Batch-add (multi-select) goes through the "Add selected to cherry" button that appears only when at
        // least one source tile is selected — keeping the single-click semantics non-destructive (the procgen
        // animation can't be edited, but the cherry sequence can still grow by accident on a stray click).
        void BuildCherrySourceGrid(PyrePlusSpec s)
        {
            int n = Mathf.Max(1, s.frameCount);
            float tile = Mathf.Clamp(s.previewCherryStripSize <= 0 ? CherryTileDefaultPx : s.previewCherryStripSize,
                CherryTilePxMin, CherryTilePxMax);
            for (int i = 0; i < n; i++)
            {
                int sourceIdx = i;
                var tileEl = new VisualElement();
                tileEl.AddToClassList(UssSourceTile);   // base class so USS selection rule matches even unselected tiles
                tileEl.style.width = tile;
                tileEl.style.height = tile;
                tileEl.style.marginRight = 4;
                tileEl.style.marginBottom = 4;
                tileEl.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
                if (sourceSelected.Contains(sourceIdx))
                    tileEl.AddToClassList(UssSourceTileSelected);
                var tex = (cherryStripCache != null && sourceIdx < cherryStripCache.Length) ? cherryStripCache[sourceIdx] : null;
                if (tex != null)
                {
                    tileEl.style.backgroundImage = new StyleBackground(tex);
                    tileEl.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                }
                // Frame index label in the lower-left so the user can match a tile to its baked source frame
                // (otherwise a sequence of similar frames reads as "all the same").
                var idxLabel = new Label($"{sourceIdx + 1}")
                {
                    tooltip = $"Source frame {sourceIdx + 1} of {n}. Click to select; shift/ctrl to multi-select; double-click to append to the cherry sequence.",
                };
                idxLabel.style.position = Position.Absolute;
                idxLabel.style.left = 2;
                idxLabel.style.bottom = 0;
                idxLabel.style.color = new Color(1f, 1f, 1f, 0.95f);
                idxLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                idxLabel.style.unityTextAlign = TextAnchor.LowerLeft;
                idxLabel.pickingMode = PickingMode.Ignore;
                tileEl.Add(idxLabel);
                // PickingMode.Position keeps the lower-left index label from swallowing clicks.
                tileEl.pickingMode = PickingMode.Position;
                tileEl.AddManipulator(new Clickable(() => SelectSourceOnly(s, sourceIdx)));
                // Shift / Ctrl modifiers on the same pointer event — routed via a dedicated callback so they
                // can be distinguished from a plain click without conflicting with Clickable's default handling.
                tileEl.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    if (e.modifiers.HasFlag(EventModifiers.Shift))
                        SelectSourceRange(s, sourceIdx);
                    else if (e.modifiers.HasFlag(EventModifiers.Control) || e.modifiers.HasFlag(EventModifiers.Command))
                        ToggleSourceSelection(s, sourceIdx);
                    // Plain click → Clickable fires (SelectSourceOnly).
                    e.StopPropagation();
                });
                // Double-click → batch-add THIS single tile to cherry (one-step shortcut; multi-add goes
                // through the dedicated button).
                tileEl.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.button != 0 || e.clickCount < 2) return;
                    AppendSourceFramesToCherry(s, new[] { sourceIdx });
                    e.StopPropagation();
                });
                cherrySourceGridHost.Add(tileEl);
            }

            // The batch-add row: appears ONLY when at least one source tile is selected. Sits above the source
            // tiles as a one-line row inside the Source-frames box, so it inherits the box's fold state.
            UpdateSourceBatchAddRow(s);
        }

        // The "Add N selected to cherry" row. Inserted as the first child of cherrySourceBox (above the tiles)
        // when at least one source tile is selected; otherwise removed. Self-maintaining — callers don't need
        // to track whether it's already there. The row stays inside the fold so collapsing Source frames hides
        // the affordance too.
        void UpdateSourceBatchAddRow(PyrePlusSpec s)
        {
            // Find any existing row by class (cheap, and survives RebuildCherryGrids' Clear+Add cycle).
            VisualElement existing = null;
            if (cherrySourceBox != null)
                for (int i = 0; i < cherrySourceBox.childCount; i++)
                    if (cherrySourceBox.ElementAt(i).ClassListContains("zui-cherry-source__batchrow"))
                    { existing = cherrySourceBox.ElementAt(i); break; }

            if (sourceSelected.Count == 0)
            {
                existing?.RemoveFromHierarchy();
                return;
            }
            var ordered = sourceSelected.OrderBy(i => i).ToList();
            var row = new VisualElement();
            row.AddToClassList("zui-cherry-source__batchrow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0f;
            row.style.marginBottom = 4;

            var label = new Label($"{sourceSelected.Count} source frame{(sourceSelected.Count == 1 ? "" : "s")} selected")
            {
                tooltip = $"Indices in source order: {string.Join(", ", ordered.Select(i => i + 1))}",
            };
            label.style.color = new Color(0.55f, 0.85f, 1f, 1f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);
            row.Add(Z.Flexible());

            var addBtn = Z.Button($"Add to cherry ({sourceSelected.Count})",
                $"Append the {sourceSelected.Count} selected source frame{(sourceSelected.Count == 1 ? "" : "s")} to the cherry sequence IN SOURCE ORDER. Undoable.",
                () =>
                {
                    AppendSourceFramesToCherry(s, ordered);
                });
            row.Add(addBtn);

            var clearBtn = Z.Button("Clear selection",
                "Clear the source-tile selection (the batch row disappears).",
                () =>
                {
                    ClearSourceSelection();
                    RebuildCherryGrids(s);
                });
            row.Add(clearBtn);

            if (existing != null)
            {
                // Replace in-place: capture the old index, remove the old, insert the new at the same index.
                int idx = cherrySourceBox.IndexOf(existing);
                existing.RemoveFromHierarchy();
                cherrySourceBox.Insert(idx, row);
            }
            else if (cherrySourceBox != null) cherrySourceBox.Insert(0, row);
        }

        // Append every index in `ordered` to the cherry sequence as new CherryFrames. Source-order preserved.
        // One Dirty for the whole batch → one Undo step for any size of batch add.
        void AppendSourceFramesToCherry(PyrePlusSpec s, IList<int> ordered)
        {
            if (ordered == null || ordered.Count == 0) return;
            Dirty(() =>
            {
                if (s.cherryFrames == null) s.cherryFrames = new List<CherryFrame>();
                foreach (int idx in ordered)
                {
                    if (idx < 0 || idx >= s.frameCount) continue;
                    s.cherryFrames.Add(new CherryFrame { sourceIndex = idx });
                }
                ResetCherryPlayback();
            });
            RebuildCherryGrids(s);
            RebuildTransport(s);
        }

        // Source-tile selection helpers — mirror of the cherry slot ones. Both lists share the same modifier
        // semantics (click = replace, shift = range, ctrl/cmd = toggle). Same no-rebuild rule as the cherry
        // helpers — toggling CSS classes in place keeps the tree stable across gestures.

        void SelectSourceOnly(PyrePlusSpec s, int sourceIdx)
        {
            ApplySourceSelectionDiff(sourceSelected, new HashSet<int> { sourceIdx });
            sourceSelectionAnchor = sourceIdx;
        }

        void SelectSourceRange(PyrePlusSpec s, int sourceIdx)
        {
            int anchor = sourceSelectionAnchor >= 0 ? sourceSelectionAnchor : sourceIdx;
            int lo = Mathf.Min(anchor, sourceIdx);
            int hi = Mathf.Max(anchor, sourceIdx);
            var next = new HashSet<int>();
            for (int i = lo; i <= hi; i++) next.Add(i);
            ApplySourceSelectionDiff(sourceSelected, next);
            sourceSelectionAnchor = sourceIdx;
        }

        void ToggleSourceSelection(PyrePlusSpec s, int sourceIdx)
        {
            var next = new HashSet<int>(sourceSelected);
            if (!next.Add(sourceIdx)) next.Remove(sourceIdx);
            ApplySourceSelectionDiff(sourceSelected, next);
            sourceSelectionAnchor = sourceIdx;
        }

        // Compute the diff between the current source selection and `next`, then add/remove the
        // UssSourceTileSelected class on each affected tile. Same in-place mutation pattern as the cherry
        // selection helper above — RebuildCherryGrids would destroy the tiles mid-gesture.
        void ApplySourceSelectionDiff(HashSet<int> oldSel, HashSet<int> newSel)
        {
            foreach (var i in oldSel)
                if (!newSel.Contains(i))
                    ToggleSourceSelectedClass(i, false);
            foreach (var i in newSel)
                if (!oldSel.Contains(i))
                    ToggleSourceSelectedClass(i, true);
            oldSel.Clear();
            foreach (var i in newSel) oldSel.Add(i);
        }

        void ToggleSourceSelectedClass(int sourceIdx, bool selected)
        {
            if (cherrySourceGridHost == null) return;
            if (sourceIdx < 0 || sourceIdx >= cherrySourceGridHost.childCount) return;
            var tile = cherrySourceGridHost.ElementAt(sourceIdx);
            if (selected) tile.AddToClassList(UssSourceTileSelected);
            else tile.RemoveFromClassList(UssSourceTileSelected);
        }

        // ── Cherry slots (UITK cards) ─────────────────────────────────────────────────────────────────
        // One bare VisualElement per slot (not Z.Box — the box's 6px padding + 6px margin is the "ugly space"
        // the user flagged; we want the same tight spacing as the source tiles, just marginRight/Bottom:4).
        // A card is its own thumbnail VisualElement with three optional overlays:
        //   • Lower-left slot-index label ("#3" — distinguishes cards with the same source frame).
        //   • Lower-right resolved-length badge ("2×" / "1-4×" — hidden when Static 1×, the no-op default).
        //   • Top-right fade-in delete × (revealed only on hover, half on / half off the corner).
        // Click behaviour: SELECT (not delete). Shift+click extends selection; Ctrl/Cmd+click toggles. Right-
        // click opens the inline ZuiMenu popover (Variable Length + MultiFrame). Drag the card (or the grip
        // overlay) to reorder — when the dragged card is part of a multi-selection, ALL selected move together.
        void BuildCherrySlotsGrid(PyrePlusSpec s)
        {
            int cherryN = s.cherryFrames != null ? s.cherryFrames.Count : 0;
            if (cherryN == 0)
            {
                // No slots: a small placeholder line so the box has something to show. Picking source frames above
                // adds to this list; the empty state is itself the only instruction the user needs.
                var empty = Z.Text("Empty — click source frames above to build a sequence.",
                    ZuiText.Subtle, "No cherry slots yet. Click source frames in the box above to APPEND them here. Click a cherry card to SELECT (shift / ctrl to multi-select); right-click for Variable Length / MultiFrame; drag to reorder; Delete to remove; Ctrl+D to duplicate.");
                empty.style.marginTop = 6;
                empty.style.marginBottom = 6;
                cherrySlotsHost.Add(empty);
                return;
            }
            for (int i = 0; i < cherryN; i++)
                cherrySlotsHost.Add(BuildCherrySlotCard(s, i));
        }

        // One cherry slot card. Layout:
        //   ┌──────────────────────────┐
        //   │ ┌────────────────────┐ × │   ← × (fade-in on hover, top-right corner, half off)
        //   │ │                    │   │
        //   │ │   thumbnail        │   │   ← body (the texture; backgroundImage)
        //   │ │ #3              2× │   │   ← slot-index lower-left, length badge lower-right
        //   │ └────────────────────┘   │
        //   └──────────────────────────┘
        VisualElement BuildCherrySlotCard(PyrePlusSpec s, int slotIdx)
        {
            int cherryN = s.cherryFrames.Count;
            var slot = s.cherryFrames[slotIdx];

            // Bare tile, NOT Z.Box — the box's padding/margin is the "ugly space" gap the user flagged.
            // Tiles match source-tile spacing exactly (marginRight/Bottom:4), so both grids read with the same
            // rhythm and the "click to add to cherry" affordance matches the visible card spacing.
            float tile = Mathf.Clamp(s.previewCherryStripSize <= 0 ? CherryTileDefaultPx : s.previewCherryStripSize,
                CherryTilePxMin, CherryTilePxMax);
            var card = new VisualElement();
            card.AddToClassList(UssCherryCard);
            card.style.width = tile;
            card.style.height = tile;
            card.style.marginRight = 4;
            card.style.marginBottom = 4;
            card.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            if (cherrySelected.Contains(slotIdx))
                card.AddToClassList(UssCherryCardSelected);

            // ── Thumbnail (backgroundImage) ─────────────────────────────────────────────────────────────
            int srcFrame = Mathf.Clamp(slot.sourceIndex, 0, Mathf.Max(0, s.frameCount - 1));
            var tex = (cherryStripCache != null && srcFrame < cherryStripCache.Length) ? cherryStripCache[srcFrame] : null;
            if (tex != null)
            {
                card.style.backgroundImage = new StyleBackground(tex);
                card.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }

            // ── Slot-index label (lower-left) ────────────────────────────────────────────────────────────────
            var indexLabel = new Label($"#{slotIdx + 1}")
            {
                tooltip = $"Cherry slot {slotIdx + 1} of {cherryN}, playing source frame {srcFrame + 1}. Click to select; shift/ctrl to multi-select; right-click for options.",
            };
            indexLabel.style.position = Position.Absolute;
            indexLabel.style.left = 2;
            indexLabel.style.bottom = 0;
            indexLabel.style.color = new Color(1f, 1f, 1f, 0.95f);
            indexLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexLabel.style.unityTextAlign = TextAnchor.LowerLeft;
            indexLabel.pickingMode = PickingMode.Ignore;
            card.Add(indexLabel);

            // ── Resolved-length badge (lower-right, only when non-1×) ──────────────────────────────────────────
            float len = slot.ResolveLength();
            if (!Mathf.Approximately(len, 1f) || slot.useMinMaxLength || !Mathf.Approximately(slot.lengthMultiplier, 1f))
            {
                var badge = new Label(slot.useMinMaxLength
                    ? $"{SlotLengthShort(slot.minLengthMultiplier)}-{SlotLengthShort(slot.maxLengthMultiplier)}×"
                    : $"{SlotLengthShort(slot.lengthMultiplier)}×")
                {
                    tooltip = slot.useMinMaxLength
                        ? $"Variable Length: random in [{slot.minLengthMultiplier:0.##}, {slot.maxLengthMultiplier:0.##}] beats per loop entry. Right-click to edit."
                        : $"Variable Length: this slot holds for {slot.lengthMultiplier:0.##} beats (the default is 1). Right-click to edit.",
                };
                badge.style.position = Position.Absolute;
                badge.style.right = 2;
                badge.style.bottom = 0;
                badge.style.width = CherryLengthBadgeW;
                badge.style.height = CherryLengthBadgeH;
                badge.style.color = new Color(1f, 1f, 1f, 0.95f);
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.unityTextAlign = TextAnchor.MiddleRight;
                badge.pickingMode = PickingMode.Ignore;
                card.Add(badge);
            }

            // ── Delete × button (top-right, fade-in on hover) ────────────────────────────────────────────────
            // Half on, half off the corner: position top:0 right:0 with transform translate(50%, -50%) — but UITK
            // doesn't have transform on regular elements without USS. The simpler trick: use top:-{half} right:-
            // {half} which puts the centre at the corner (the visible button is the bottom-right quadrant plus
            // the half-overflowing top-left quadrant of the same square, all inside the card's bounding rect).
            // Using marginRight/marginTop of -{halfSize} achieves the same effect with simpler math.
            var deleteBtn = new Button(() => DeleteCherrySlots(s, new[] { slotIdx }))
            {
                text = "×",
                tooltip = $"Remove cherry slot {slotIdx + 1} (undoable). For multi-select, use Delete key.",
            };
            deleteBtn.AddToClassList(UssCherryCardDelete);
            deleteBtn.style.position = Position.Absolute;
            deleteBtn.style.top = -CherryDeleteBtnSize * 0.5f;
            deleteBtn.style.right = -CherryDeleteBtnSize * 0.5f;
            deleteBtn.style.width = CherryDeleteBtnSize;
            deleteBtn.style.height = CherryDeleteBtnSize;
            deleteBtn.style.paddingTop = 0;
            deleteBtn.style.paddingBottom = 0;
            deleteBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(deleteBtn);

            // ── Click + drag (matches the Laumination Builder's sequence-strip pattern) ────────────────
            // Click on a card → select it. Drag onto ANOTHER card (mouse-down here, mouse-up there) → reorder.
            // The two-event model (PointerDown arms + PointerUp resolves) is exactly the pattern the Laumination
            // Builder uses for its sequence strip (LauminationBuilderWindow.cs:2528 HandleSeqDrag): no threshold,
            // no ghost, no live drag-line — just "click = select, drag = move". The user flagged this as the
            // pattern to match ("I don't want two solutions unless necessary") so the cherry drag intentionally
            // mirrors the Laumination Builder's UX exactly. Multi-drag is layered on top: if the pressed card
            // is part of a multi-selection, every selected slot moves together as one contiguous block.
            CherryCardPress(card, s, slotIdx);
            return card;
        }

        // ── Selection state mutators ───────────────────────────────────────────────────────────────────
        // All selection ops clear selection-then-rebuild-OR-repaint-and-rebuild (because the visual indicator is
        // the UssCherryCardSelected class on each card, and any rebuild invalidates those class bits). Cheap
        // because RebuildCherryGrids is a Clear+Add cycle on already-cached thumbnail textures.

        void ClearCherrySelection()
        {
            cherrySelected.Clear();
            cherrySelectionAnchor = -1;
            cherryPrimary = -1;
            cherryDragFrom = -1;
            cherryDragSelection = null;
        }

        void ClearSourceSelection()
        {
            sourceSelected.Clear();
            sourceSelectionAnchor = -1;
        }

        // Single-select: replace the selection with this one slot. The anchor moves here for the next shift-extend.
        // NO RebuildCherryGrids — that would destroy the cards between PointerDown and PointerUp, breaking the
        // drag gesture (the card reference held by the press handler would be orphaned, and panel.Pick on up
        // would find a freshly-rebuilt tree with no relationship to the press). Just toggle the CSS class on
        // the affected cards in place — same visual result, tree stable across the gesture.
        // Selection MATH (click=replace, shift=range, ctrl/cmd=toggle) is shared with the Laumination Builder's
        // sequence strip via Laubrary.Zui.ZuiThumbGrid — one implementation, not two per-tool copies. Each
        // helper still applies the diff as a CSS-class toggle (not a rebuild) so an in-flight drag gesture's
        // card reference stays valid.
        void SelectCherryOnly(PyrePlusSpec s, int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= s.cherryFrames.Count) return;
            var next = new HashSet<int>(cherrySelected);
            ZuiThumbGrid.SelectSingle(next, ref cherryPrimary, ref cherrySelectionAnchor, slotIdx);
            ApplyCherrySelectionDiff(cherrySelected, next);
        }

        void SelectCherryRange(PyrePlusSpec s, int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= s.cherryFrames.Count) return;
            var next = new HashSet<int>(cherrySelected);
            ZuiThumbGrid.RangeTo(next, ref cherryPrimary, ref cherrySelectionAnchor, slotIdx);
            ApplyCherrySelectionDiff(cherrySelected, next);
        }

        void ToggleCherrySelection(PyrePlusSpec s, int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= s.cherryFrames.Count) return;
            var next = new HashSet<int>(cherrySelected);
            ZuiThumbGrid.Toggle(next, ref cherryPrimary, ref cherrySelectionAnchor, slotIdx);
            ApplyCherrySelectionDiff(cherrySelected, next);
        }

        // Compute the diff between the current selection and `next`, then add/remove the UssCherryCardSelected
        // class on each affected card directly. The cherrySlotsHost tree stays intact across selection changes —
        // this is the Laumination Builder's IMGUI model (the data changes, the repaint reads the data) applied
        // to UITK by toggling CSS classes in place. Rebuilding on every selection would orphan any in-flight
        // PointerDown/PointerUp gesture's card reference and break drag-to-reorder.
        void ApplyCherrySelectionDiff(HashSet<int> oldSel, HashSet<int> newSel)
        {
            // Removed indices: in oldSel but not in newSel. Remove the class.
            foreach (var i in oldSel)
                if (!newSel.Contains(i))
                    ToggleCherrySelectedClass(i, false);
            // Added indices: in newSel but not in oldSel. Add the class.
            foreach (var i in newSel)
                if (!oldSel.Contains(i))
                    ToggleCherrySelectedClass(i, true);
            // Sync the HashSet in place — clear + add so the same reference still holds the new contents.
            oldSel.Clear();
            foreach (var i in newSel) oldSel.Add(i);
        }

        // Single-card CSS class toggle. Walks the cherrySlotsHost child at `slotIdx` and adds/removes
        // UssCherryCardSelected. Index check: cards may have been rebuilt by some other path since the index
        // was captured; bail (no-op) if the index is out of range OR if cherrySlotsHost is null (between
        // RebuildCherryPanel tear-down and rebuild).
        void ToggleCherrySelectedClass(int slotIdx, bool selected)
        {
            if (cherrySlotsHost == null) return;
            if (slotIdx < 0 || slotIdx >= cherrySlotsHost.childCount) return;
            var card = cherrySlotsHost.ElementAt(slotIdx);
            if (selected) card.AddToClassList(UssCherryCardSelected);
            else card.RemoveFromClassList(UssCherryCardSelected);
        }

        // ── Click + Drag (the Laumination Builder's sequence-strip pattern, reused not reinvented) ─────────
        // Same two-event model as LauminationBuilderWindow.HandleSeqDrag (LauminationBuilderWindow.cs:~2528):
        // PointerDown on a card selects it and arms a drag; PointerUp resolves the reorder against WHICHEVER
        // card the cursor actually ends up over. The critical piece copied from the proven IMGUI version: NO
        // pointer capture. Capturing the pointer on PointerDown (the previous version's bug) forces every later
        // event — including PointerUp — back onto the ORIGIN card regardless of where the mouse actually is,
        // which is why dragging did nothing: the up-handler needs to fire on the DESTINATION card. Leaving the
        // pointer uncaptured lets UI Toolkit's normal picking deliver PointerUp to whatever card is really under
        // the cursor — the UITK equivalent of the IMGUI version's per-tile `Rect.Contains(mousePosition)` check.
        // Drag state (dragFrom / dragSelection) therefore has to live on the WINDOW, not in a per-card closure —
        // a closure captured by BuildCherrySlotCard(s, i) only for that one card would never see a press that
        // happened on a different card.
        void CherryCardPress(VisualElement card, PyrePlusSpec s, int slotIdx)
        {
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    // Right-click → open the popover. If the click landed OUTSIDE the current selection, first
                    // narrow the selection to this single card so the popover operates on the right slot(s).
                    if (!cherrySelected.Contains(slotIdx))
                        SelectCherryOnly(s, slotIdx);
                    ShowCherryPopover(s, card);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;
                int pressedFrom = cherrySlotsHost.IndexOf(card);
                if (pressedFrom < 0) return;
                // Apply the click selection immediately (modifiers captured at press-down).
                if (e.modifiers.HasFlag(EventModifiers.Shift))
                    SelectCherryRange(s, pressedFrom);
                else if (e.modifiers.HasFlag(EventModifiers.Control) || e.modifiers.HasFlag(EventModifiers.Command))
                    ToggleCherrySelection(s, pressedFrom);
                else
                    SelectCherryOnly(s, pressedFrom);
                // Snapshot the selection to drag. If the pressed card isn't part of it (shouldn't happen after
                // the selection above, but guards a stale index), fall back to just this card.
                cherryDragFrom = pressedFrom;
                cherryDragSelection = cherrySelected.Contains(pressedFrom) && cherrySelected.Count > 1
                    ? new List<int>(cherrySelected)
                    : new List<int> { pressedFrom };
                e.StopPropagation();
            });

            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0) return;
                if (cherryDragFrom < 0 || cherryDragSelection == null || cherryDragSelection.Count == 0) return;
                var sel = cherryDragSelection;
                int from = cherryDragFrom;
                cherryDragFrom = -1;
                cherryDragSelection = null;
                // THIS card is the one that actually received the up-event — i.e. whatever the cursor is
                // really over, exactly like the IMGUI `Rect.Contains` check in the Laumination Builder.
                int target = cherrySlotsHost.IndexOf(card);
                if (target < 0) return;
                if (sel.Count == 1 && (target == from || target == from + 1)) return; // dropped back on itself
                ReorderCherrySlotsMulti(s, sel, target);
                e.StopPropagation();
            });
        }

        // Compute the insertion index under the cursor — where a horizontal line would split the card column.
        // Compute the insertion index for a drag at worldY. Two failure modes of the original implementation
        // (copied from ZuiReorder.TargetIndex) had to be patched for the wrap-row card layout:
        // Move `indices` (sorted ascending) into a contiguous block at `targetIndex` in the LIVE list — the same
        // semantics as `ZuiReorder.TargetIndex`: an insertion index where the dragged element is still present
        // (so targetIndex > from when dragging down, targetIndex <= from when dragging up). The function does
        // its own from-adjustment to match `ZuiReorder.onMoved`'s contract (post-removal `to` = target - 1 when
        // target > from, else target). Steps:
        //   1. If dragging DOWN (target > from), the post-removal insertion index is target - 1 (the dragged
        //      element itself is still counted in the live target index). If target <= from, the index is
        //      already correct for the post-removal list (no adjustment needed).
        //   2. Snapshot the selected CherryFrames (in their CURRENT order — selection order is not slot order).
        //   3. Remove them from highest index to lowest (so earlier indices stay valid).
        //   4. Insert the snapshot as a contiguous block at the post-removal index.
        // Undo is one Dirty, the transport's scrubber range is rebuilt, the cherry grid is rebuilt, and the
        // selection set is remapped to the NEW indices of the moved slots so the visual highlight follows them.
        void ReorderCherrySlotsMulti(PyrePlusSpec s, List<int> indices, int targetIndex)
        {
            if (s.cherryFrames == null || indices.Count == 0) return;
            int firstNewIdx = 0;
            int moved = indices.Count;
            Dirty(() =>
            {
                // Shared with the Laumination Builder's sequence-strip reorder via ZuiThumbGrid.MoveBlock —
                // same contiguous-block-move math, not a second hand-rolled copy.
                firstNewIdx = ZuiThumbGrid.MoveBlock(s.cherryFrames, indices, targetIndex);
                ResetCherryPlayback();
            });
            cherrySelected.Clear();
            for (int k = 0; k < moved; k++) cherrySelected.Add(firstNewIdx + k);
            cherrySelectionAnchor = firstNewIdx + moved - 1;
            cherryPrimary = cherrySelectionAnchor;
            RebuildCherryGrids(s);
            RebuildTransport(s);
        }

        // Delete the cherry slots at the given indices (descending order so earlier indices stay valid).
        // Undo-safe single Dirty. If deleting would empty the list, the cherry sequencer stops — no special
        // "at least one slot" rule here because the cherry list is allowed to be empty (the empty-cherry blank
        // preview path handles that).
        void DeleteCherrySlots(PyrePlusSpec s, IList<int> indices)
        {
            if (s.cherryFrames == null || indices.Count == 0) return;
            Dirty(() =>
            {
                var sorted = new List<int>(indices);
                sorted.Sort();
                for (int k = sorted.Count - 1; k >= 0; k--)
                    if (sorted[k] >= 0 && sorted[k] < s.cherryFrames.Count)
                        s.cherryFrames.RemoveAt(sorted[k]);
                ResetCherryPlayback();
            });
            ClearCherrySelection();
            RebuildCherryGrids(s);
            RebuildTransport(s);
        }

        // Duplicate the cherry slots at the given indices, appending the copies AFTER the last selected
        // position (or at the end if the selection is contiguous-to-end). The copies preserve Variable Length
        // and MultiFrame settings — they're cheap deep clones (CherryFrame is plain fields + a List<int>).
        void DuplicateCherrySlots(PyrePlusSpec s, IList<int> indices)
        {
            if (s.cherryFrames == null || indices.Count == 0) return;
            var sorted = new List<int>(indices);
            sorted.Sort();
            int insertAfter = sorted[sorted.Count - 1];
            Dirty(() =>
            {
                for (int k = sorted.Count - 1; k >= 0; k--)
                {
                    var copy = CloneCherryFrame(s.cherryFrames[sorted[k]]);
                    s.cherryFrames.Insert(insertAfter + 1, copy);
                }
                ResetCherryPlayback();
            });
            RebuildCherryGrids(s);
            RebuildTransport(s);
        }

        // Deep-clone a CherryFrame. MemberwiseClone copies all value fields; the multiFrameSources list is the
        // only reference field, so it needs its own new List<int>.
        static CherryFrame CloneCherryFrame(CherryFrame src)
        {
            var c = new CherryFrame
            {
                sourceIndex = src.sourceIndex,
                lengthMultiplier = src.lengthMultiplier,
                minLengthMultiplier = src.minLengthMultiplier,
                maxLengthMultiplier = src.maxLengthMultiplier,
                useMinMaxLength = src.useMinMaxLength,
                multiFrame = src.multiFrame,
                multiFrameRandomSeed = src.multiFrameRandomSeed,
            };
            if (src.multiFrameSources != null)
                c.multiFrameSources = new List<int>(src.multiFrameSources);
            return c;
        }

        // Keyboard shortcut handler — installed on the cherry grid host so it only fires when the panel has
        // focus (not when an unrelated text field in the same window is focused). Delete / Backspace removes
        // the current cherry selection; Ctrl/Cmd+D duplicates it. Both do nothing if the cherry list is empty
        // or the focus is in a TextField (so typing "delete" in a field doesn't lose the user's text).
        void CherryKeyboardShortcuts(KeyDownEvent e)
        {
            // Don't steal keys from text inputs — UITK's TextField, TextInputBaseField<T,U>, and the lower-level
            // TextElement all subclass BaseField, which is the safe "is this a typing surface" check.
            if (e.target is BaseField<char>) return;
            var s = spec;
            if (s == null || s.cherryFrames == null || s.cherryFrames.Count == 0) return;
            bool ctrl = e.modifiers.HasFlag(EventModifiers.Control) || e.modifiers.HasFlag(EventModifiers.Command);
            if (ctrl && e.keyCode == KeyCode.D)
            {
                if (cherrySelected.Count > 0)
                    DuplicateCherrySlots(s, cherrySelected.OrderBy(i => i).ToList());
                e.StopPropagation();
                return;
            }
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                if (cherrySelected.Count > 0)
                    DeleteCherrySlots(s, cherrySelected.OrderBy(i => i).ToList());
                e.StopPropagation();
            }
        }

        // Compact number format for the length badge: 2 digits max, strip trailing zeros, so "1.5×" not "1.50×".
        static string SlotLengthShort(float v)
        {
            if (Mathf.Approximately(v, Mathf.Round(v))) return ((int)Mathf.Round(v)).ToString();
            return v.ToString("0.##");
        }

        // ── Right-click popover (ZuiMenu with inline controls) ────────────────────────────────────
        // Opens a Z.Menu anchored to a tiny invisible VisualElement at the right-click screen position. The
        // menu body is a Custom body holding inline ZUI controls (Static/MinMax radio for Variable Length, a
        // MicroSlider for the static value, a MinMax range for the random bounds, a Toggle for MultiFrame, and
        // a comma-separated source-frames picker for MultiFrame sources).
        //
        // Two display modes:
        //   • Single selection → the standard popover (edits ONE slot)
        //   • Multi selection  → "Apply to N selected" popover (edits every selected slot identically, with
        //                       a small header showing the selection count and an action row for batch remove
        //                       / duplicate)
        // The same idioms SpriteFx / Pyre use for their right-click menus — pickers and sliders stay editable
        // inside the menu, no submenus.
        void ShowCherryPopover(PyrePlusSpec s, VisualElement card)
        {
            // Anchor DIRECTLY to the card that was right-clicked — the same thing every other Z.Menu call site
            // in the codebase does (Z.Menu(someButton), Z.Menu(tagPickBtn), ...). The previous version instead
            // manufactured a floating 1×1 element positioned by hand from the click's event coordinates, adding
            // `cherryPanelHost.worldBound` to `trigger.position` — but `PointerDownEvent.position` is ALREADY
            // panel-space (the same space `worldBound` reports in), so that addition double-counted the panel
            // host's own offset within the window. The anchor ended up placed far past where the click actually
            // happened — usually off the visible window entirely — so `ZuiPopover.Place()` never found room to
            // show it and the popover was invisible: right-clicking a card looked like it did nothing at all —
            // "there are no tools left" to edit a cherry frame with. Anchoring to the real, already-laid-out
            // card sidesteps the coordinate math altogether.
            var menu = Z.Menu(card).Width(280f);
            menu.Custom((body, close) =>
            {
                if (cherrySelected.Count > 1)
                {
                    var sortedSelection = cherrySelected.OrderBy(i => i).ToList();
                    BuildCherryPopoverBodyMulti(body, close, s, sortedSelection);
                }
                else
                {
                    // Either zero (shouldn't happen — right-click is on a card we just selected) or one.
                    int slotIdx = cherrySelected.Count == 1 ? cherrySelected.First() : -1;
                    if (slotIdx >= 0 && slotIdx < s.cherryFrames.Count)
                        BuildCherryPopoverBody(body, close, s, slotIdx);
                }
            });
            menu.Show();
        }

        // The single-slot popover — Variable Length + MultiFrame controls for one CherryFrame.
        void BuildCherryPopoverBody(VisualElement body, Action close, PyrePlusSpec s, int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= s.cherryFrames.Count) return;
            var slot = s.cherryFrames[slotIdx];

            // ── Variable Length mode (Static / MinMax) as a MiniRadio ────────────────────────────────
            body.Add(Z.Text("Variable Length",
                ZuiText.Small,
                "How many beats this slot occupies in the preview loop. 1 = one beat. The cherry sequencer " +
                "drains the slot's remaining beats before advancing, so a 2× slot holds for 2 beats."));
            int mode = slot.useMinMaxLength ? 1 : 0;
            body.Add(Z.MiniRadio(mode, new[] { "Static", "MinMax" },
                "Static = the slot holds for the value below on every loop entry. MinMax = the slot holds for a " +
                "fresh random value in the range below on every loop entry, so a single slot plays at varying lengths.",
                v =>
                {
                    Dirty(() =>
                    {
                        slot.useMinMaxLength = v == 1;
                        if (!slot.useMinMaxLength)
                        {
                            slot.lengthMultiplier = Mathf.Max(0.01f, slot.lengthMultiplier);
                            slot.minLengthMultiplier = slot.lengthMultiplier;
                            slot.maxLengthMultiplier = slot.lengthMultiplier;
                        }
                        else
                        {
                            float lo = Mathf.Min(slot.minLengthMultiplier, slot.lengthMultiplier);
                            float hi = Mathf.Max(slot.maxLengthMultiplier, slot.lengthMultiplier);
                            if (hi - lo < 0.01f) hi = lo + 1f;
                            slot.minLengthMultiplier = lo;
                            slot.maxLengthMultiplier = hi;
                        }
                    });
                    RebuildCherryGrids(s);
                }, wrap: true));

            if (!slot.useMinMaxLength)
            {
                body.Add(Z.MicroSlider("Static (beats)", slot.lengthMultiplier, 0.01f, 16f,
                    "How many beats this slot occupies (1 = normal, 2 = hold twice as long, etc.).",
                    v =>
                    {
                        Undo.RecordObject(s, "Edit Pyre Plus");
                        slot.lengthMultiplier = Mathf.Max(0.01f, v);
                        EditorUtility.SetDirty(s);
                        RebuildCherryGrids(s);
                    }, 240f, showValue: true, decimals: 2));
            }
            else
            {
                float lo = Mathf.Min(slot.minLengthMultiplier, slot.maxLengthMultiplier);
                float hi = Mathf.Max(slot.minLengthMultiplier, slot.maxLengthMultiplier);
                body.Add(Z.MinMax(lo, hi, 0.01f, 16f,
                    "Min and max beats the slot may hold for (each loop entry picks a fresh random value in this range).",
                    (newLo, newHi) =>
                    {
                        Undo.RecordObject(s, "Edit Pyre Plus");
                        slot.minLengthMultiplier = Mathf.Clamp(newLo, 0.01f, newHi);
                        slot.maxLengthMultiplier = Mathf.Clamp(newHi, newLo, 16f);
                        EditorUtility.SetDirty(s);
                        RebuildCherryGrids(s);
                    }, 240f));
            }

            // ── MultiFrame toggle + per-source picker ────────────────────────────────────────────
            body.Add(Z.Text("MultiFrame",
                ZuiText.Small,
                "On = the slot picks ONE source frame from the list below at random on every playback entry " +
                "(useful for grouping alternating poses). Off = the slot always plays the source frame shown on the card."));
            body.Add(Z.Toggle("MultiFrame on",
                "Randomly pick one source frame per playback entry from the source indices below (0-based, comma-separated).",
                slot.multiFrame,
                v =>
                {
                    Dirty(() => slot.multiFrame = v);
                    RebuildCherryGrids(s);
                }));
            if (slot.multiFrame)
            {
                string current = slot.multiFrameSources == null
                    ? ""
                    : string.Join(",", slot.multiFrameSources);
                body.Add(Z.TextInput(current,
                    $"Comma-separated source frame indices (0..{s.frameCount - 1}). The slot picks one at random per playback entry.",
                    v =>
                    {
                        var parsed = new List<int>();
                        foreach (var part in v.Split(','))
                        {
                            var trimmed = part.Trim();
                            if (string.IsNullOrEmpty(trimmed)) continue;
                            if (!int.TryParse(trimmed, out int n)) continue;
                            if (n < 0 || n >= s.frameCount) continue;
                            parsed.Add(n);
                        }
                        Dirty(() =>
                        {
                            slot.multiFrameSources = parsed;
                            if (parsed.Count > 0) slot.sourceIndex = parsed[0];
                        });
                        RebuildCherryGrids(s);
                    }, 240f));
                body.Add(Z.Int(slot.multiFrameRandomSeed,
                    "Seed for the per-slot picker. 0 = fully random; any other value = deterministic (same picks every playback, useful for reproducing a sequence).",
                    v =>
                    {
                        Dirty(() => slot.multiFrameRandomSeed = v);
                    }, 60f));
            }

            // ── Action items (close the menu on click) ─────────────────────────────────────────────
            body.Add(Z.VSpace(6));
            body.Add(Z.Button("Remove slot",
                "Remove this cherry slot from the sequence (undoable).",
                () =>
                {
                    Dirty(() =>
                    {
                        s.cherryFrames.RemoveAt(slotIdx);
                        ResetCherryPlayback();
                    });
                    RebuildCherryGrids(s);
                    RebuildTransport(s);
                    close?.Invoke();
                }));
        }

        // The multi-select popover — the same Variable Length / MultiFrame controls, but every change applies
        // to every slot in the selection as one Undo step. Variable Length is intentionally a SIMPLE shape here
        // (Static only, fixed value, no MinMax range): mixing a per-slot MinMax with one popover slider is more
        // confusing than it's worth — users who need per-slot MinMax edit one slot at a time. MultiFrame also
        // uses the SIMPLE shape (toggle + comma-separated sources applied identically to every selected slot).
        void BuildCherryPopoverBodyMulti(VisualElement body, Action close, PyrePlusSpec s, IList<int> slotIndices)
        {
            // Header — what and how many. The "Clear selection" affordance lets the user back out without
            // closing the popover and picking a single card (the keyboard shortcut for the same is Escape).
            var header = new Label($"{slotIndices.Count} cherry slots selected");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            body.Add(header);
            body.Add(Z.Button("Clear selection",
                "Empty the cherry selection (back to no cards highlighted). The popover stays open so you can pick again.",
                () =>
                {
                    ClearCherrySelection();
                    RebuildCherryGrids(s);
                    close?.Invoke();
                }));

            body.Add(Z.VSpace(6));
            // ── Variable Length (Static only) ──────────────────────────────────────────────────────────
            // Sample the FIRST selected slot to seed the displayed value — the user knows their own selection.
            // Pushing a value applies it to EVERY selected slot; existing MinMax / custom values get overridden
            // (the popover's purpose is "set these N slots to the same thing"; if that's not what the user
            // wants, they can escape and edit one slot at a time).
            var firstSlot = s.cherryFrames[slotIndices[0]];
            float seedStatic = Mathf.Max(0.01f, firstSlot.useMinMaxLength ? firstSlot.lengthMultiplier : firstSlot.lengthMultiplier);
            body.Add(Z.Text("Variable Length (applied to all selected)",
                ZuiText.Small,
                "Sets the Static beats value on every selected slot. Existing MinMax slots are converted to Static. " +
                "Per-slot MinMax is one-slot-at-a-time (right-click that single slot)."));
            body.Add(Z.MicroSlider("Static (beats)", seedStatic, 0.01f, 16f,
                "How many beats each selected slot holds for (1 = normal, 2 = hold twice as long, etc.). " +
                "Applies to all selected as one Undo step.",
                v =>
                {
                    Dirty(() =>
                    {
                        foreach (int i in slotIndices)
                        {
                            var slot = s.cherryFrames[i];
                            slot.useMinMaxLength = false;
                            slot.lengthMultiplier = Mathf.Max(0.01f, v);
                            slot.minLengthMultiplier = slot.lengthMultiplier;
                            slot.maxLengthMultiplier = slot.lengthMultiplier;
                        }
                        ResetCherryPlayback();
                    });
                    RebuildCherryGrids(s);
                }, 240f, showValue: true, decimals: 2));

            body.Add(Z.VSpace(4));
            // ── MultiFrame (applied to all) ───────────────────────────────────────────────────────────────
            // Read the first slot's multiFrame state to seed the toggle; same one-Undo-step apply pattern.
            body.Add(Z.Toggle("MultiFrame on",
                "Apply the MultiFrame on/off state to every selected slot.",
                firstSlot.multiFrame,
                v =>
                {
                    Dirty(() =>
                    {
                        foreach (int i in slotIndices) s.cherryFrames[i].multiFrame = v;
                        ResetCherryPlayback();
                    });
                    RebuildCherryGrids(s);
                }));

            // ── Action items (close on click) ─────────────────────────────────────────────────────────
            body.Add(Z.VSpace(6));
            body.Add(Z.Button($"Duplicate {slotIndices.Count} slots",
                $"Insert a copy of every selected slot AFTER the last selected position (in source order). The originals stay where they are; the copies are selected for you so a second click re-duplicates.",
                () =>
                {
                    DuplicateCherrySlots(s, slotIndices);
                    // After duplication, the new copies sit after the originals at the same indices; select them.
                    int firstNewIdx = slotIndices[slotIndices.Count - 1] + 1;
                    cherrySelected.Clear();
                    for (int k = 0; k < slotIndices.Count; k++) cherrySelected.Add(firstNewIdx + k);
                    RebuildCherryGrids(s);
                    close?.Invoke();
                }));
            body.Add(Z.Button($"Remove {slotIndices.Count} slots",
                $"Remove every selected cherry slot (undoable as one step).",
                () =>
                {
                    DeleteCherrySlots(s, slotIndices);
                    close?.Invoke();
                }));
        }

        // ── Cherry strip cache (one texture per SOURCE frame, separate from the filmstrip's) ─────────
        // Mirrors EnsureStripCache (PyrePlusWindow.Preview.cs). The filmstrip only builds when Strip mode is
        // on; the cherry grid needs the source frames rendered any time CherryFraming is enabled, so it owns its
        // own cache and never competes with the filmstrip.
        Texture2D[] cherryStripCache;
        int cherryStripCacheCanvas = -1;
        int cherryStripCacheFrames = -1;

        void EnsureCherryStripCache(PyrePlusSpec s)
        {
            int n = Mathf.Max(1, s.frameCount);
            bool structural = cherryStripCache == null
                || cherryStripCache.Length != n
                || cherryStripCacheCanvas != s.canvasSize
                || cherryStripCacheFrames != s.frameCount;
            if (!previewDirty && !structural) return;
            DestroyCherryStripCache();
            cherryStripCache = new Texture2D[n];
            for (int i = 0; i < n; i++) cherryStripCache[i] = PyrePlusRenderer.RenderFrameTexture(s, i);
            cherryStripCacheCanvas = s.canvasSize;
            cherryStripCacheFrames = s.frameCount;
        }

        void DestroyCherryStripCache()
        {
            if (cherryStripCache == null) return;
            for (int i = 0; i < cherryStripCache.Length; i++)
                if (cherryStripCache[i] != null) DestroyImmediate(cherryStripCache[i]);
            cherryStripCache = null;
            cherryStripCacheCanvas = -1;
            cherryStripCacheFrames = -1;
        }
    }
}
