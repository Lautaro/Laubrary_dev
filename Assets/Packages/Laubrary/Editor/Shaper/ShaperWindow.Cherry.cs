// ShaperWindow.Cherry — the cherry-framing AUTHORING panel: a thumbnail grid with drag-reorder, multi-select,
// a right-click slot editor and Delete, plus the preview-only Zound trigger (T-0176, Pyre parity —
// pyre-inventory rows 100-105, PyreWindow.CherryFraming.cs).
//
// Ported from Pyre's own cherry-SLOT grid (Pyre also draws a separate "source frames" grid to pick FROM;
// Shaper's existing "+ Add slot" button already covers that role by appending whatever frame is on screen, so
// this file keeps the one-grid shape rather than adding a second grid that would just duplicate the scrubber).
//
// Selection + block-reorder MATH is ZuiThumbGrid (Laubrary.Zui) — the exact class Pyre's own grid and the
// Laumination Builder's sequence strip already share; this is its third caller, not a fourth reimplementation.
// The actual pointer gesture is a direct port of PyreWindow.CherryFraming's proven shape (see its own file
// header for the two load-bearing rules this inherits): NO PointerCapture on drag (capture would route
// PointerUp back to the origin card regardless of where the mouse released, which breaks drop-target
// resolution entirely — UI Toolkit's normal picking already delivers PointerUp to whatever card is under the
// cursor, and that card's index IS the drop target), and the right-click popover anchors to the CARD element
// (PointerDownEvent.position is already panel-space; adding a parent's worldBound on top double-counts it).
//
// Task brief named ZuiReorder for the drag — that helper is a grip-driven VERTICAL LIST (its own TargetIndex
// compares Y only), which is the right fit for a single-column list but not for a wrapping thumbnail grid
// where several cards share a row. Pyre's own proven card-grid gesture (ZuiThumbGrid.MoveBlock + press/
// release, no capture) is the one built for exactly this shape, and is what this file uses — noted here so
// the deviation from the literal brief is a considered one, not a missed instruction.
//
// The slot editor is a Z.Popover (not Z.Menu) for the same reason PyreWindow.CherryFraming uses Popover, not
// GenericMenu, for its own ShowCherryPopover: this is a settings PANEL (live sliders/toggles a user tunes
// while watching them), not a list of one-shot actions — Z.Menu's Item() semantics (click → run → dismiss)
// are the wrong shape for a field the user drags open-ended.
using System.Collections.Generic;
using System.Linq;
using Laubrary.PyreShaper;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        // ── thumbnail cache — keyed by SOURCE FRAME index (several slots can share one), independent of the
        // preview stage's and the filmstrip's own caches (both cache by PLAYBACK position, this one by which
        // frame a slot names). Lazily filled: a document with many frames but few authored slots only ever
        // pays to render the frames actually referenced. ────────────────────────────────────────────────────
        readonly Dictionary<int, Texture2D> cherryThumbCache = new Dictionary<int, Texture2D>();

        void DisposeCherryThumbs()
        {
            foreach (var tex in cherryThumbCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            cherryThumbCache.Clear();
        }

        /// Called alongside the frame cache / filmstrip invalidation on every authored edit — a slot's
        /// thumbnail can change for the same reasons any other rendered frame can (a layer, fill, effect...).
        internal void InvalidateCherryThumbs() => DisposeCherryThumbs();

        Texture2D CherryThumb(int sourceIndex)
        {
            if (document == null) return null;
            sourceIndex = Mathf.Clamp(sourceIndex, 0, Mathf.Max(0, document.frameCount - 1));
            if (cherryThumbCache.TryGetValue(sourceIndex, out var hit) && hit != null) return hit;

            int w = Mathf.Max(1, document.canvasWidth), h = Mathf.Max(1, document.canvasHeight);
            var px = ShaperDocumentRenderer.RenderFrame(document, sourceIndex, ShaperEffectApplier.Instance);
            if (px == null || px.Length != w * h) return null;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false);
            cherryThumbCache[sourceIndex] = tex;
            return tex;
        }

        // ── panel scaffolding ────────────────────────────────────────────────────────────────────────────
        ZuiSection cherrySection;
        VisualElement cherryGridHost;
        VisualElement cherryFlex;   // the flex-wrap row inside cherryGridHost, children in slot-index order

        VisualElement BuildCherryPanel()
        {
            cherrySection = Z.Section("Cherry Framing",
                "Play a sub-sequence of this document's own frames instead of the plain frame order: pick "
                + "which frames play, in what order, and how long each is held.",
                "shaper.window.cherry", icon: "shuffle");

            cherrySection.SetHeaderToggle(document.cherryEnabled,
                "Play the cherry sequence in the preview instead of the plain frame order. The bake follows "
                + "this too — a cherry-enabled document bakes the sub-sequence.",
                v =>
                {
                    Change(() => document.cherryEnabled = v);
                    ResetCherryPlayback();
                    RebuildCherryPanel();
                });

            RebuildCherryPanel();
            return cherrySection;
        }

        void RebuildCherryPanel()
        {
            if (cherrySection == null || document == null) return;
            // The header toggle is the first child SetHeaderToggle already added; clear everything else and
            // refill, matching the section's own "content is everything after the header" contract.
            cherryGridHost = null;
            cherryFlex = null;
            for (int i = cherrySection.contentContainer.childCount - 1; i >= 0; i--)
                cherrySection.contentContainer.RemoveAt(i);

            // While cherry is off the whole grid is pointless — don't even build it (the absence rule this
            // window follows everywhere else).
            if (!document.cherryEnabled) return;

            document.cherryFrames ??= new List<ShaperCherryFrame>();

            var box = Z.BoxKeyed("Cherry slots",
                "The cherry sub-sequence, in play order. Click to select (Shift = range, Ctrl/Cmd = toggle); "
                + "drag a slot onto another to reorder (a multi-selection moves together); right-click to "
                + "edit length/multi-frame; Delete removes the selection.",
                "shaper.window.cherry.slots");
            cherryGridHost = new VisualElement();
            box.Add(cherryGridHost);

            box.Add(Z.HGroup(
                Z.Button("+ Add slot",
                    "Append a slot playing the frame currently shown in the preview.", () =>
                    {
                        int at = -1;
                        Change(() =>
                        {
                            document.cherryFrames.Add(new ShaperCherryFrame
                            {
                                sourceIndex = Mathf.Clamp(Mathf.Max(0, currentFrame), 0,
                                                          Mathf.Max(0, document.frameCount - 1)),
                            });
                            at = document.cherryFrames.Count - 1;
                        });
                        ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, at);
                        ResetCherryPlayback();
                        // Full panel rebuild, not just the grid: a new slot count changes the Zound cue
                        // slider's own range below.
                        RebuildCherryPanel();
                    }),
                Z.MicroSlider("Loop gap", document.cherryLoopDelaySeconds, 0f, 4f,
                    "Seconds of blank between one pass through the sequence and the next. 0 loops with no "
                    + "gap. A gap plays as nothing on screen, not as a held frame.",
                    v => Change(() => document.cherryLoopDelaySeconds = Mathf.Max(0f, v)), 150f, decimals: 2)));

            cherrySection.contentContainer.Add(box);
            cherrySection.contentContainer.Add(BuildZoundCueRow());

            cherrySection.contentContainer.focusable = true;
            cherrySection.contentContainer.RegisterCallback<KeyDownEvent>(CherryKeyboardShortcuts);

            RebuildCherrySlotGrid();
        }

        // ── selection state (non-serialized — reset is not needed across documents beyond what
        // OnAssetChanged already implies, since a rebuild always calls RebuildCherryPanel fresh) ────────────
        readonly HashSet<int> cherrySelected = new HashSet<int>();
        int cherryPrimary = -1, cherryAnchor = -1;

        // ── drag-in-progress state — WINDOW-level, not per-card: with no pointer capture, the press (on card
        // A) and the release (on card B) are two different UITK event targets, so a per-card closure could
        // never see a press that started on a different card. Armed on PointerDown, consumed on PointerUp.
        int cherryDragFrom = -1;
        List<int> cherryDragBlock;

        static void StyleCherryCardSelection(VisualElement card, bool sel)
        {
            var col = sel ? new Color(0.35f, 0.75f, 0.95f, 0.9f) : new Color(0f, 0f, 0f, 0.25f);
            card.style.borderTopColor = col; card.style.borderBottomColor = col;
            card.style.borderLeftColor = col; card.style.borderRightColor = col;
        }

        void RefreshCherrySelectionVisuals()
        {
            if (cherryFlex == null) return;
            for (int i = 0; i < cherryFlex.childCount; i++)
                StyleCherryCardSelection(cherryFlex[i], cherrySelected.Contains(i));
        }

        void RebuildCherrySlotGrid()
        {
            if (cherryGridHost == null) return;
            cherryGridHost.Clear();

            cherryFlex = new VisualElement();
            cherryFlex.style.flexDirection = FlexDirection.Row;
            cherryFlex.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < document.cherryFrames.Count; i++)
                cherryFlex.Add(BuildCherrySlotCard(i));
            cherryGridHost.Add(cherryFlex);
        }

        const float CherryTileSize = 64f;

        VisualElement BuildCherrySlotCard(int i)
        {
            var slot = document.cherryFrames[i];
            var card = new VisualElement
            {
                tooltip = "Click to select (Shift = range, Ctrl/Cmd = toggle); drag onto another slot to "
                    + "reorder; right-click to edit."
            };
            card.style.width = CherryTileSize;
            card.style.marginRight = 2f; card.style.marginBottom = 2f;
            card.style.borderTopWidth = card.style.borderBottomWidth = 2f;
            card.style.borderLeftWidth = card.style.borderRightWidth = 2f;
            StyleCherryCardSelection(card, cherrySelected.Contains(i));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.paddingLeft = 2f; header.style.paddingRight = 2f;
            header.Add(Z.Text((i + 1).ToString(), ZuiText.Small, "This slot's play order."));
            if (slot.multiFrame)
                header.Add(Z.Text("M", ZuiText.Small,
                    "MultiFrame — a random source frame is picked each time this slot plays."));
            if (document.previewZoundFrame == i)
                header.Add(Z.Text("♪", ZuiText.Small, "The preview-only Zound cue fires when this slot plays."));
            var del = Z.Button("×", "Remove this slot (or the whole selection, if this slot is part of one).",
                () => DeleteCherrySlotOrSelection(i)).W(16f);
            del.style.height = 16f;
            del.style.marginTop = 0f; del.style.marginBottom = 0f; del.style.marginLeft = 0f; del.style.marginRight = 0f;
            header.Add(del);
            card.Add(header);

            var body = new VisualElement();
            body.style.width = CherryTileSize; body.style.height = CherryTileSize;
            body.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            body.pickingMode = PickingMode.Ignore;
            var thumb = CherryThumb(slot.sourceIndex);
            if (thumb != null) body.style.backgroundImage = Background.FromTexture2D(thumb);
            card.Add(body);

            string lenText = slot.useMinMaxLength
                ? $"{slot.minLengthMultiplier:0.#}–{slot.maxLengthMultiplier:0.#}×"
                : (Mathf.Approximately(slot.lengthMultiplier, 1f) ? "" : $"{slot.lengthMultiplier:0.#}×");
            if (!string.IsNullOrEmpty(lenText))
                card.Add(Z.Text(lenText, ZuiText.Small, "How many beats this slot holds."));

            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) { CherryCardRightClick(i, card); e.StopPropagation(); return; }
                if (e.button != 0) return;
                CherryCardPress(i, e);
            });
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || cherryDragFrom < 0) return;
                int from = cherryDragFrom;
                var block = cherryDragBlock;
                cherryDragFrom = -1; cherryDragBlock = null;
                if (i != from) CherryDropOnto(i, block);
            });
            return card;
        }

        // PointerDown on a card: select it (per the click modifier) AND arm a potential drag — the drag
        // itself resolves on WHICHEVER card's PointerUp fires next (see the file header; no CapturePointer).
        void CherryCardPress(int i, PointerDownEvent e)
        {
            if (e.shiftKey) ZuiThumbGrid.RangeTo(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            else if (e.ctrlKey || e.commandKey) ZuiThumbGrid.Toggle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            else if (!cherrySelected.Contains(i)) ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            // else: i is already part of a multi-selection — keep it as-is so the whole block drags together.
            cherryDragFrom = i;
            cherryDragBlock = new List<int>(cherrySelected.Contains(i) ? cherrySelected : new HashSet<int> { i });
            RefreshCherrySelectionVisuals();
            cherrySection?.contentContainer.Focus();
        }

        void CherryDropOnto(int targetIndex, List<int> block)
        {
            if (block == null || block.Count == 0) return;
            int firstNew = 0;
            Change(() =>
            {
                firstNew = ZuiThumbGrid.MoveBlock(document.cherryFrames, block, targetIndex);
                cherrySelected.Clear();
                for (int k = 0; k < block.Count; k++) cherrySelected.Add(firstNew + k);
                cherryPrimary = firstNew; cherryAnchor = firstNew;
            });
            ResetCherryPlayback();
            RebuildCherrySlotGrid();
        }

        void CherryCardRightClick(int i, VisualElement card)
        {
            if (!cherrySelected.Contains(i))
            {
                ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
                RefreshCherrySelectionVisuals();
            }
            ShowCherrySlotEditor(card, i);
        }

        /// The ONLY way to edit a slot's length/min-max/multi-frame. Anchored directly to the card that was
        /// right-clicked (never a hand-computed position — PointerDownEvent.position is already panel-space).
        /// Field edits apply to every selected slot when more than one is selected (uniform batch edit);
        /// rebuilding the grid is deferred to onClosed so the anchor stays valid while the popover is open.
        void ShowCherrySlotEditor(VisualElement card, int i)
        {
            bool multi = cherrySelected.Count > 1 && cherrySelected.Contains(i);
            var indices = multi ? cherrySelected.OrderBy(x => x).ToList() : new List<int> { i };
            var first = document.cherryFrames[i];
            int maxFrame = Mathf.Max(0, document.frameCount - 1);

            Z.Popover(card, panel =>
            {
                panel.Add(Z.Text(multi ? $"{indices.Count} slots selected" : $"Slot {i + 1}", ZuiText.Body, ""));

                if (!multi)
                    panel.Add(Z.MicroSlider("Source frame", Mathf.Clamp(first.sourceIndex, 0, maxFrame), 0, maxFrame,
                        "Which of this document's own frames this slot plays.",
                        v => { Change(() => document.cherryFrames[i].sourceIndex = Mathf.RoundToInt(v)); ResetCherryPlayback(); },
                        160f, decimals: 0));

                panel.Add(Z.Toggle("Randomise length",
                    "Draw a new hold length on every pass through the sequence instead of always using the "
                    + "fixed one. The draw is seeded from the document, so a given pass is reproducible.",
                    first.useMinMaxLength,
                    v => Change(() => { foreach (var idx in indices) document.cherryFrames[idx].useMinMaxLength = v; })));

                panel.Add(Z.MicroSlider("Length ×", first.lengthMultiplier, 0.25f, 8f,
                    "Fixed hold, in beats. 1 = one frame's worth of time at the playback rate. Ignored while "
                    + "Randomise length is on.",
                    v => Change(() => { foreach (var idx in indices) document.cherryFrames[idx].lengthMultiplier = v; }),
                    180f, decimals: 2));

                panel.Add(Z.HGroup(
                    Z.MicroSlider("Min ×", first.minLengthMultiplier, 0.25f, 8f, "Shortest hold this slot can draw.",
                        v => Change(() => { foreach (var idx in indices) document.cherryFrames[idx].minLengthMultiplier = v; }),
                        100f, decimals: 2),
                    Z.MicroSlider("Max ×", first.maxLengthMultiplier, 0.25f, 8f, "Longest hold this slot can draw.",
                        v => Change(() => { foreach (var idx in indices) document.cherryFrames[idx].maxLengthMultiplier = v; }),
                        100f, decimals: 2)));

                panel.Add(Z.Toggle("MultiFrame",
                    "Pick a random source frame from this slot's own list below, each time it plays, instead "
                    + "of the fixed Source frame above.",
                    first.multiFrame,
                    v => Change(() => { foreach (var idx in indices) document.cherryFrames[idx].multiFrame = v; })));

                if (!multi && first.multiFrame)
                    panel.Add(BuildMultiFrameSourcesEditor(i));

                panel.Add(Z.HGroup(
                    Z.Button("Duplicate", "Duplicate the selected slot(s) right after themselves.",
                        () => DuplicateCherrySlots(indices)),
                    Z.Button("Delete", "Delete the selected slot(s).",
                        () => DeleteCherrySlots(indices))));
            }, new ZuiPopover.Options { minWidth = 220f, onClosed = RebuildCherrySlotGrid });
        }

        /// The multi-frame candidate list + its own seed — only shown for a single-selected, MultiFrame slot
        /// (a batch edit across several slots' own independent lists has no single coherent "add"/"remove").
        VisualElement BuildMultiFrameSourcesEditor(int slotIndex)
        {
            int maxFrame = Mathf.Max(0, document.frameCount - 1);
            var host = new VisualElement();
            var listRow = new VisualElement();
            listRow.style.flexDirection = FlexDirection.Row;
            listRow.style.flexWrap = Wrap.Wrap;

            void Refill()
            {
                listRow.Clear();
                var slot = document.cherryFrames[slotIndex];
                slot.multiFrameSources ??= new List<int>();
                for (int k = 0; k < slot.multiFrameSources.Count; k++)
                {
                    int frame = slot.multiFrameSources[k];
                    var chip = Z.HGroup(
                        Z.Text(frame.ToString(), ZuiText.Small, "A candidate source frame for this slot."),
                        Z.Button("×", "Remove this candidate frame.", () =>
                        {
                            Change(() => document.cherryFrames[slotIndex].multiFrameSources.RemoveAt(k));
                            ResetCherryPlayback();
                            Refill();
                        }).W(14f));
                    chip.style.marginRight = 3f; chip.style.marginBottom = 2f;
                    listRow.Add(chip);
                }
            }
            Refill();

            host.Add(Z.Text("Candidate frames", ZuiText.Small,
                "The frames this slot can draw from while MultiFrame is on."));
            host.Add(listRow);
            host.Add(Z.HGroup(
                Z.Button("+ Add current frame",
                    "Add the frame currently shown in the preview as a candidate.", () =>
                    {
                        int f = Mathf.Clamp(Mathf.Max(0, currentFrame), 0, maxFrame);
                        Change(() =>
                        {
                            var s = document.cherryFrames[slotIndex];
                            s.multiFrameSources ??= new List<int>();
                            s.multiFrameSources.Add(f);
                        });
                        ResetCherryPlayback();
                        Refill();
                    }),
                Z.MicroSlider("Seed", document.cherryFrames[slotIndex].multiFrameRandomSeed, 0f, 9999f,
                    "Per-slot salt for the multi-frame draw. Non-zero pins this slot's picks independently "
                    + "of the document seed, so one slot can be re-rolled without disturbing the others.",
                    v => Change(() => document.cherryFrames[slotIndex].multiFrameRandomSeed = Mathf.RoundToInt(v)),
                    110f, decimals: 0)));
            return host;
        }

        void DeleteCherrySlotOrSelection(int i)
        {
            var indices = cherrySelected.Contains(i) && cherrySelected.Count > 1
                ? cherrySelected.OrderBy(x => x).ToList()
                : new List<int> { i };
            DeleteCherrySlots(indices);
        }

        void DeleteCherrySlots(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderByDescending(x => x).ToList();
            Change(() =>
            {
                foreach (var idx in sorted)
                    if (idx >= 0 && idx < document.cherryFrames.Count) document.cherryFrames.RemoveAt(idx);
                // A deleted slot can be the one the Zound cue named — clamp back into range rather than
                // leaving it pointing past the end of a now-shorter list.
                document.previewZoundFrame = Mathf.Min(document.previewZoundFrame, document.cherryFrames.Count - 1);
            });
            cherrySelected.Clear(); cherryPrimary = -1; cherryAnchor = -1;
            ResetCherryPlayback();
            // Full panel rebuild, not just the grid: a slot-count change moves the Zound cue slider's range.
            RebuildCherryPanel();
        }

        void DuplicateCherrySlots(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderBy(x => x).ToList();
            int insertAt = sorted[sorted.Count - 1] + 1;
            Change(() =>
            {
                var copies = new List<ShaperCherryFrame>();
                foreach (var idx in sorted)
                {
                    var src = document.cherryFrames[idx];
                    copies.Add(new ShaperCherryFrame
                    {
                        sourceIndex = src.sourceIndex,
                        lengthMultiplier = src.lengthMultiplier,
                        minLengthMultiplier = src.minLengthMultiplier,
                        maxLengthMultiplier = src.maxLengthMultiplier,
                        useMinMaxLength = src.useMinMaxLength,
                        multiFrame = src.multiFrame,
                        multiFrameSources = new List<int>(src.multiFrameSources ?? new List<int>()),
                        multiFrameRandomSeed = src.multiFrameRandomSeed,
                    });
                }
                for (int k = 0; k < copies.Count; k++) document.cherryFrames.Insert(insertAt + k, copies[k]);
                cherrySelected.Clear();
                for (int k = 0; k < copies.Count; k++) cherrySelected.Add(insertAt + k);
                cherryPrimary = insertAt; cherryAnchor = insertAt;
            });
            ResetCherryPlayback();
            // Full panel rebuild, not just the grid: a slot-count change moves the Zound cue slider's range.
            RebuildCherryPanel();
        }

        void CherryKeyboardShortcuts(KeyDownEvent e)
        {
            if (cherrySelected.Count == 0) return;
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteCherrySlots(cherrySelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
            else if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.D)
            {
                DuplicateCherrySlots(cherrySelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
        }

        // ── Zound trigger (T-0176, Pyre parity — Runtime/Pyre/Pyre.cs:1251-1252) ────────────────────────────
        // A single preview-only cue: which cherry slot it fires on (-1 = never) and which Zound plays. Storage
        // mirrors Pyre's own previewZoundFrame/previewZoundName exactly (see ShaperDocument.cs); the fire
        // itself is wired in ShaperWindow.Preview.cs's AdvanceCherry/FireZoundCueIfMatch, which Pyre's own
        // version never was (PyreWindow.cs:281's comment says so explicitly).
        VisualElement BuildZoundCueRow()
        {
            // -1 when there are no slots at all — "never" is then the only legal value, matching the field's
            // own -1-means-never contract rather than aliasing an empty sequence onto a slot that doesn't exist.
            int maxSlot = document.cherryFrames.Count - 1;
            var slider = Z.MicroSlider("Fires on slot", document.previewZoundFrame, -1f, maxSlot,
                "Which cherry slot (by play order) fires the Zound below, once per entry into it. -1 = never. "
                + "Preview-only — this can never reach a bake.",
                v =>
                {
                    Change(() => document.previewZoundFrame = Mathf.Clamp(Mathf.RoundToInt(v), -1, maxSlot));
                    RebuildCherrySlotGrid();
                }, 150f, decimals: 0);

            return Z.Field("Zound cue", "A preview-only sound cue tied to one cherry slot. Never baked.",
                Z.HGroup(slider, BuildZoundPickerButton()));
        }

        VisualElement BuildZoundPickerButton()
        {
            if (!ShaperZoundPickerHook.Available)
            {
                var unavailable = Z.Button("Zound picker unavailable",
                    "No audio tool is registered in this project, so there is nothing to pick from. With "
                    + "Zounds present the ShaperZounds bridge registers the picker automatically and this "
                    + "becomes a browser.", null).W(170f);
                unavailable.SetEnabled(false);
                return unavailable;
            }

            string Current() => string.IsNullOrEmpty(document.previewZoundName) ? "(none)" : document.previewZoundName;
            var button = Z.Button(Current(),
                "Click to pick a Zound. Right-click to hear the current one.", null).W(170f);

            button.clicked += () =>
            {
                var screen = GUIUtility.GUIToScreenPoint(button.worldBound.position);
                ShaperZoundPickerHook.Show(screen, picked =>
                {
                    Change(() => document.previewZoundName = picked);
                    button.text = Current();
                    ShaperZoundPickerHook.Preview?.Invoke(picked);   // hear what you just chose, immediately
                });
            };

            // Right-click auditions it. A sound field you cannot hear from is a name you have to trust, and
            // the whole reason this is picked rather than typed is that trusting a name does not work.
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || string.IsNullOrEmpty(document.previewZoundName)) return;
                ShaperZoundPickerHook.Preview?.Invoke(document.previewZoundName);
                e.StopPropagation();
            });

            return button;
        }
    }
}
