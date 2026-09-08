// ShaperWindow.Cherry — Shaper's half of the cherry panel: the source-frame thumbnail cache, the host
// adapter that lets the SHARED PyreCherryPanel draw over this document, and the rows Shaper has that Pyre
// does not.
//
// T-0199. The panel used to be a partial hand-port of PyreWindow.CherryFraming that kept the slot grid and
// dropped the whole source-frame half — no frame grid, no click/shift/ctrl selection over source frames, no
// double-click-to-add, no "+ Add selected", no tile size. That is exactly the drift two hand-kept copies
// produce, so there is now ONE panel (Editor/Pyre/PyreCherryPanel.cs) and two hosts, the same move
// PyreShapeCards made for the Shape cards.
//
// The data types stay separate on purpose. ShaperCherryFrame and Pyre's CherryFrame carry the same six
// authored scalars but resolve them differently: Pyre draws with UnityEngine.Random / System.Random, both
// of which are banned from a Shaper generator path (BC-1.3), so ShaperCherryFrame hashes from the document
// seed instead. Unifying the types would drag a banned draw into Shaper's runtime, so the panel exchanges
// the six scalars (PyreCherrySlotView) and each host keeps its own type entirely on its own side.
//
// SHAPER'S OWN ROWS, drawn through the panel's host hooks because Pyre has no equivalent: the loop gap
// (Pyre keeps its own in the transport row), the preview-only Zound cue and its ♪ badge (T-0176), the
// MultiFrame candidate-frame list and its per-slot seed (Shaper authors the list Pyre only ever read), and
// the "+ Add slot" button that appends whatever frame the preview is showing.
using System.Collections.Generic;
using Laubrary.Pyre.Editor;
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
        // frame a slot names). Lazily filled: a frame nothing on screen asks for is never rendered. ───────
        readonly Dictionary<int, Texture2D> cherryThumbCache = new Dictionary<int, Texture2D>();

        void DisposeCherryThumbs()
        {
            foreach (var tex in cherryThumbCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            cherryThumbCache.Clear();
        }

        /// Called alongside the frame cache / filmstrip invalidation on every authored edit — a slot's
        /// thumbnail can change for the same reasons any other rendered frame can (a layer, fill, effect...).
        /// The grids are repainted too: their tiles hold the textures just destroyed, so an invalidation that
        /// did not repaint left every thumbnail blank until something else happened to rebuild the panel.
        internal void InvalidateCherryThumbs()
        {
            DisposeCherryThumbs();
            if (cherryPanel == null || rootVisualElement == null) return;
            // Deferred and coalesced: Change() fires on every drag delta of every dial in the window, and
            // repainting the grids re-renders one document frame per tile. Scheduling off rootVisualElement
            // (which survives a window rebuild) and pausing the prior item collapses a whole drag into one
            // repaint on the next frame instead of one per delta.
            cherryRepaintPending?.Pause();
            cherryRepaintPending = rootVisualElement.schedule.Execute(() =>
            {
                cherryPanel?.RebuildSourceGrid();
                cherryPanel?.RebuildSlotGrid();
            }).StartingIn(0);
        }

        IVisualElementScheduledItem cherryRepaintPending;

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

        // ── panel ────────────────────────────────────────────────────────────────────────────────────────
        PyreCherryPanel cherryPanel;

        VisualElement BuildCherryPanel()
        {
            cherryPanel = new PyreCherryPanel(new CherryHost(this));
            cherryPanel.Rebuild();
            return cherryPanel.Root;
        }

        /// Rebuild the whole cherry panel — for a change made outside it that the panel's own edits do not
        /// cover (a new document, a frame-count change).
        void RebuildCherryPanel() => cherryPanel?.Rebuild();

        // ── tile size ────────────────────────────────────────────────────────────────────────────────────
        // Machine-local view state, not authored data: how big the thumbnails are drawn says nothing about
        // what the document plays, and putting it on the asset would put a view preference in the document's
        // undo history and its diff. (Pyre keeps its own on the spec; that is Pyre's existing storage and is
        // left alone.) Default 96 matches Pyre's, so both windows open at the same tile size.
        const string CherryTileSizePrefKey = "Laubrary.Shaper.Cherry.TileSize";

        // ── host adapter ─────────────────────────────────────────────────────────────────────────────────
        sealed class CherryHost : IPyreCherryHost
        {
            readonly ShaperWindow w;

            readonly PyreCherryChrome chrome = new PyreCherryChrome
            {
                sectionKey = "shaper.window.cherry",
                sectionIcon = "shuffle",
                sectionTooltip =
                    "Play a sub-sequence of this document's own frames instead of the plain frame order: pick "
                    + "which frames play, in what order, and how long each is held.",
                enableTooltip =
                    "Play the cherry sequence in the preview instead of the plain frame order. The bake follows "
                    + "this too — a cherry-enabled document bakes the sub-sequence.",

                sourceBoxKey = "shaper.window.cherry.source",
                sourceBoxTooltip =
                    "This document's own frames. Click to select (Shift = range, Ctrl/Cmd = toggle); "
                    + "double-click a frame, or \"+ Add selected\", to append it to Cherry slots.",

                slotBoxKey = "shaper.window.cherry.slots",

                tileSizeTooltip =
                    "Thumbnail size in the source/cherry grids (32–256px). Only changes layout — frames aren't "
                    + "re-rendered.",
                sourceFrameTooltip = "Which of this document's own frames this slot plays.",

                // A Shaper canvas is authored width × height and is often not square, so a thumbnail drawn
                // unscaled would be stretched to the square tile.
                scaleThumbsToFit = true,
            };

            public CherryHost(ShaperWindow window) { w = window; }

            ShaperDocument Doc => w.document;

            public PyreCherryChrome Chrome => chrome;

            public int CherryFrameCount => Doc != null ? Doc.frameCount : 0;

            public bool CherryEnabled => Doc != null && Doc.cherryEnabled;
            public void SetCherryEnabled(bool value) => w.Change(() => Doc.cherryEnabled = value);

            public float CherryTileSize => EditorPrefs.GetFloat(CherryTileSizePrefKey, 96f);
            public void SetCherryTileSize(float px) => EditorPrefs.SetFloat(CherryTileSizePrefKey,
                Mathf.Clamp(px, PyreCherryPanel.MinTileSize, PyreCherryPanel.MaxTileSize));

            List<ShaperCherryFrame> Slots
            {
                get
                {
                    if (Doc == null) return null;
                    return Doc.cherryFrames ??= new List<ShaperCherryFrame>();
                }
            }

            public int CherrySlotCount => Slots?.Count ?? 0;

            public PyreCherrySlotView ReadCherrySlot(int index)
            {
                var f = Slots[index];
                return new PyreCherrySlotView
                {
                    sourceIndex = f.sourceIndex,
                    lengthMultiplier = f.lengthMultiplier,
                    minLengthMultiplier = f.minLengthMultiplier,
                    maxLengthMultiplier = f.maxLengthMultiplier,
                    useMinMaxLength = f.useMinMaxLength,
                    multiFrame = f.multiFrame,
                };
            }

            public void WriteCherrySlot(int index, PyreCherrySlotView v)
            {
                var f = Slots[index];
                f.sourceIndex = v.sourceIndex;
                f.lengthMultiplier = v.lengthMultiplier;
                f.minLengthMultiplier = v.minLengthMultiplier;
                f.maxLengthMultiplier = v.maxLengthMultiplier;
                f.useMinMaxLength = v.useMinMaxLength;
                f.multiFrame = v.multiFrame;
            }

            public void AppendCherrySlot(int sourceIndex) => Slots.Add(new ShaperCherryFrame
            {
                sourceIndex = Mathf.Clamp(sourceIndex, 0, Mathf.Max(0, CherryFrameCount - 1)),
            });

            public void DuplicateCherrySlots(List<int> indices, int insertAt)
            {
                var copies = new List<ShaperCherryFrame>();
                foreach (var idx in indices)
                {
                    var src = Slots[idx];
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
                for (int k = 0; k < copies.Count; k++) Slots.Insert(insertAt + k, copies[k]);
            }

            public void RemoveCherrySlotAt(int index) => Slots.RemoveAt(index);

            public int MoveCherryBlock(List<int> block, int targetIndex) =>
                ZuiThumbGrid.MoveBlock(Slots, block, targetIndex);

            public Texture2D CherrySourceThumb(int frameIndex) => w.CherryThumb(frameIndex);

            public void CherryEdit(System.Action apply) => w.Change(apply);

            public void ResetCherryPlayback() => w.ResetCherryPlayback();

            /// A deleted slot can be the one the Zound cue named — clamp back into range rather than leaving
            /// it pointing past the end of a now-shorter list.
            public void CherrySlotCountChanged()
            {
                if (Doc == null) return;
                Doc.previewZoundFrame = Mathf.Min(Doc.previewZoundFrame, CherrySlotCount - 1);
            }

            // ── Shaper's own rows ────────────────────────────────────────────────────────────────────────
            public void DecorateCherrySlotHeader(int slotIndex, VisualElement header)
            {
                if (Doc != null && Doc.previewZoundFrame == slotIndex)
                    header.Add(Z.Text("♪", ZuiText.Small, "The preview-only Zound cue fires when this slot plays."));
            }

            public void BuildExtraCherryPopoverRows(int slotIndex, bool multi, VisualElement panel)
            {
                // The candidate list + its own seed only make sense for ONE MultiFrame slot: a batch edit
                // across several slots' independent lists has no single coherent "add" or "remove".
                if (multi || Doc == null) return;
                if (!Slots[slotIndex].multiFrame) return;
                panel.Add(w.BuildMultiFrameSourcesEditor(slotIndex));
            }

            public void BuildExtraCherrySlotBoxRows(VisualElement slotBox)
            {
                if (Doc == null) return;
                slotBox.Add(Z.HGroup(
                    Z.Button("+ Add slot",
                        "Append a slot playing the frame currently shown in the preview.", () =>
                        {
                            w.Change(() =>
                            {
                                AppendCherrySlot(Mathf.Max(0, w.currentFrame));
                                CherrySlotCountChanged();
                            });
                            w.ResetCherryPlayback();
                            w.RebuildCherryPanel();
                        }),
                    // T-0257 — this and the transport's own gap were BOTH labelled "Loop gap" and are two
                    // different fields (cherryLoopDelaySeconds here, loopDelaySeconds there): one is the gap
                    // between passes through the SEQUENCE, the other between passes through the FRAMES.
                    // T-0276 — and they must STAY differently named: this panel and the transport are both in
                    // the right pane, so the two captions are on screen together. A previous pass shortened
                    // this one back to "Loop gap" for a caption-length rule and re-created the collision; the
                    // name says which pass it gaps, and is 12 characters.
                    Z.MicroSlider("Sequence gap", Doc.cherryLoopDelaySeconds, 0f, 4f,
                        "Seconds of blank between one pass through the CHERRY SEQUENCE and the next — a "
                        + "different gap from the transport's, which is between passes through the frames. 0 "
                        + "loops with no gap. A gap plays as nothing on screen, not as a held frame.",
                        v =>
                        {
                            w.Change(() => Doc.cherryLoopDelaySeconds = Mathf.Max(0f, v));
                            w.ResetCherryPlayback();
                        }, 185f, decimals: 2)));
            }

            public void BuildExtraCherrySectionRows(VisualElement section) => section.Add(w.BuildZoundCueRow());
        }

        /// The multi-frame candidate list + its own seed — only shown for a single-selected, MultiFrame slot.
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
                    int at = k;
                    var chip = Z.HGroup(
                        Z.Text(frame.ToString(), ZuiText.Small, "A candidate source frame for this slot."),
                        Z.Button("×", "Remove this candidate frame.", () =>
                        {
                            Change(() => document.cherryFrames[slotIndex].multiFrameSources.RemoveAt(at));
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
                // T-0257 — one of three unrelated "Seed"s (document, swarm, this one), now each qualified.
                Z.MicroSlider("Cherry seed", document.cherryFrames[slotIndex].multiFrameRandomSeed, 0f, 9999f,
                    "Per-slot salt for the multi-frame draw. Non-zero pins this slot's picks independently "
                    + "of the document seed, so one slot can be re-rolled without disturbing the others.",
                    v => Change(() => document.cherryFrames[slotIndex].multiFrameRandomSeed = Mathf.RoundToInt(v)),
                    140f, decimals: 0)));
            return host;
        }

        // ── Zound trigger (T-0176, Pyre parity — Runtime/Pyre/Pyre.cs:1251-1252) ─────────────────────────
        // A single preview-only cue: which cherry slot it fires on (-1 = never) and which Zound plays.
        // Storage mirrors Pyre's own previewZoundFrame/previewZoundName exactly (see ShaperDocument.cs); the
        // fire itself is wired in ShaperWindow.Preview.cs's AdvanceCherry/FireZoundCueIfMatch, which Pyre's
        // own version never was (PyreWindow.cs:281's comment says so explicitly).
        VisualElement BuildZoundCueRow()
        {
            // -1 when there are no slots at all — "never" is then the only legal value, matching the field's
            // own -1-means-never contract rather than aliasing an empty sequence onto a slot that doesn't
            // exist.
            int maxSlot = (document.cherryFrames?.Count ?? 0) - 1;
            var slider = Z.MicroSlider("Plays on slot", document.previewZoundFrame, -1f, maxSlot,
                "Which cherry slot (by play order) fires the Zound below, once per entry into it. -1 = never. "
                + "Preview-only — this can never reach a bake.",
                v =>
                {
                    Change(() => document.previewZoundFrame = Mathf.Clamp(Mathf.RoundToInt(v), -1, maxSlot));
                    cherryPanel?.RebuildSlotGrid();   // the ♪ badge moved to a different card
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
