// ShaperWindow.Preview — the preview CHROME, the backdrop panel, and the cherry-framing panel.
//
// Split out of ShaperWindow.cs the same way PyreWindow splits .Preview.cs and .CherryFraming.cs off its own
// shell, and for the same reason: the shell owns the document binding and the layout skeleton, and the
// preview's own furniture is a separate concern that would otherwise double the size of the file that has to
// stay readable.
//
// ── Three things here are COSMETIC and must never reach a bake ────────────────────────────────────────────
// The backdrop, the frame border and the zoom. That is guaranteed structurally rather than by discipline:
//   • The backdrop is painted by the preview stage's own IMGUI layer, BEHIND the render.
//     ShaperDocumentRenderer never reads previewBackSplash — nothing in Runtime does — so a baked sheet is
//     byte-identical whether a backdrop is set or not.
//   • The frame border is drawn in that same editor-only IMGUI pass, over the picture, after it exists.
//   • Zoom is a layout inset on the image element. The same Color32[] is displayed larger; it is never
//     re-rendered at another size, so no pixel the baker would produce changes.
// This matters because the whole point of a backdrop is to let a user light their preview for legibility
// (a dark spark on a dark panel is unreadable) WITHOUT that choice silently altering the shipped asset.
//
// ── Where each piece of state lives, and why they differ ──────────────────────────────────────────────────
// The backdrop is on the DOCUMENT (matching Pyre's spec.previewBackSplash): which backdrop reads a given
// effect is a property of that effect, and you want it back next time you open it.
// The frame border and zoom are WINDOW state, deliberately NOT on the document — Pyre keeps its equivalents
// on the spec, but Pyre's spec is also its window-state holder. Here the document is a saved asset, and
// putting a transient view preference on it would mark that asset dirty (and prompt a save) every time
// somebody nudged a zoom slider to look at something. A view preference that dirties authored data is worse
// than one that resets, so these ride on the window.
using System.Collections.Generic;
using Laubrary.BackSplash.Editor;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        // ── cosmetic view state (see the file header for why this is not on the document) ────────────────
        [SerializeField] bool previewShowFrame;
        [SerializeField] float previewZoom = 1f;

        // ── cherry playback state ────────────────────────────────────────────────────────────────────────
        // The window HOLDS the state; the engine OWNS the rule. ShaperCherry.AdvanceOneBeat is a pure
        // function of (state, document), which is exactly why T-0143 gave it an explicit state struct
        // instead of Pyre's shape, where the beat state is entangled with window fields.
        ShaperCherryState cherryState;
        bool cherryRunning;

        VisualElement backdropHost;
        VisualElement cherryListHost;
        ZuiSection cherrySection;

        // ── preview chrome ───────────────────────────────────────────────────────────────────────────────

        /// The row that sits under the preview: frame border + zoom. Deliberately NOT a filmstrip/contact
        /// sheet — see BuildCherryPanel's note on why a thumbnail strip is its own task here.
        VisualElement BuildPreviewChrome()
        {
            var frameToggle = Z.Toggle("Frame",
                "Draw a thin outline around the canvas edge in the preview. Cosmetic only — it is drawn by "
                + "the editor over the picture and never reaches a bake.",
                previewShowFrame, v =>
                {
                    previewShowFrame = v;
                    if (stage != null) { stage.ShowFrameBorder = v; stage.MarkDirtyRepaint(); }
                    RefreshPreview();
                });

            var zoom = Z.MicroSlider("Zoom", previewZoom, 1f, 8f,
                "Magnify the preview. Cosmetic only: the same rendered frame is displayed larger, never "
                + "re-rendered at another size, so the bake is unaffected.",
                v =>
                {
                    previewZoom = Mathf.Max(1f, v);
                    if (stage != null) { stage.Zoom = previewZoom; }
                    RefreshPreview();
                }, 140f, decimals: 1);

            return Z.HGroup(frameToggle, zoom);
        }

        /// Push the window's cosmetic state onto a freshly built stage. Called right after the stage is
        /// constructed, because the stage is recreated on every window rebuild while this state persists.
        void ApplyPreviewChromeToStage()
        {
            if (stage == null) return;
            stage.ShowFrameBorder = previewShowFrame;
            stage.Zoom = previewZoom;
        }

        // ── backdrop panel ───────────────────────────────────────────────────────────────────────────────

        /// The shared BackSplash panel, exactly as Pyre builds it (BuildBackdropPanel/FillBackdropPanel).
        /// `onStructureChanged` refills the host because picking or clearing the image ADDS or REMOVES
        /// controls, which a retained-mode panel cannot express by repainting alone.
        void BuildBackdropPanel(VisualElement root)
        {
            backdropHost = new VisualElement();
            root.Add(backdropHost);
            FillBackdropPanel();
        }

        void FillBackdropPanel()
        {
            if (backdropHost == null || document == null) return;
            backdropHost.Clear();

            // Created on demand so an untouched document keeps serialising exactly as it did before the
            // field existed.
            document.previewBackSplash ??= new Laubrary.BackSplash.BackSplashSettings();

            // NOTE: no Change(...) wrapper here. BackSplashZui records its own Undo and SetDirty against
            // `owner`, which is why it takes one — wrapping it too would double-record and make a single
            // edit take two Ctrl+Z presses to undo.
            backdropHost.Add(BackSplashZui.Build(document.previewBackSplash, "Preview backdrop",
                "A colour and one optional image drawn BEHIND the preview. Preview-only: nothing in the "
                + "renderer reads it, so it can never appear in a bake.",
                onChanged: RefreshPreview,
                onStructureChanged: () => { RefreshPreview(); FillBackdropPanel(); },
                icon: "eye", owner: document));
        }

        // ── cherry framing ───────────────────────────────────────────────────────────────────────────────

        /// Cherry framing authoring. The engine half (ShaperCherry) and the bake half (ShaperBaker) already
        /// existed and shipped; this is the authoring surface they never had.
        ///
        /// <b>Slot rows, not Pyre's thumbnail card grid — a deliberate scope call.</b> Pyre draws a grid of
        /// rendered frame thumbnails and reorders by dragging cards, backed by its own `cherryStripCache`.
        /// Shaper has no such cache, and T-0115 measured this renderer at roughly 30 ms per uncached frame,
        /// so a live thumbnail strip would stall the main thread in proportion to frameCount every time the
        /// panel rebuilt. Building it properly means building a thumbnail cache first, which is its own
        /// task. Rows with the ZuiReorder grip already used by every other list in this window give the
        /// same authoring power (pick a source frame, hold it, reorder, randomise) without that cost.
        ///
        /// Consequence worth stating: Pyre's two documented gotchas do NOT apply here. The
        /// pointer-capture-breaks-drop-targets rule and the anchor-the-popover-to-the-card rule are both
        /// properties of its card-grid gesture; ZuiReorder already solves reordering, and there is no
        /// popover. The one Pyre lesson that DID carry over is its blit order, honoured structurally in
        /// ShaperPreviewStage.
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
                    RebuildCherryList();
                });

            cherryListHost = new VisualElement();
            cherrySection.Add(cherryListHost);
            RebuildCherryList();
            return cherrySection;
        }

        void RebuildCherryList()
        {
            if (cherryListHost == null || document == null) return;
            cherryListHost.Clear();

            // While cherry is off the slot list is inert, so building it would be showing controls that
            // change nothing — the absence rule this window follows everywhere else.
            if (!document.cherryEnabled) return;

            document.cherryFrames ??= new List<ShaperCherryFrame>();
            for (int i = 0; i < document.cherryFrames.Count; i++)
                cherryListHost.Add(BuildCherrySlotRow(document.cherryFrames[i]));

            cherryListHost.Add(Z.HGroup(
                Z.Button("+ Add slot",
                    "Append a slot playing the frame currently shown in the preview.", () =>
                    {
                        Change(() => document.cherryFrames.Add(new ShaperCherryFrame
                        {
                            sourceIndex = Mathf.Clamp(Mathf.Max(0, currentFrame), 0,
                                                      Mathf.Max(0, document.frameCount - 1)),
                        }));
                        ResetCherryPlayback();
                        RebuildCherryList();
                    }),
                Z.MicroSlider("Loop gap", document.cherryLoopDelaySeconds, 0f, 4f,
                    "Seconds of blank between one pass through the sequence and the next. 0 loops with no "
                    + "gap. A gap plays as nothing on screen, not as a held frame.",
                    v => Change(() => document.cherryLoopDelaySeconds = Mathf.Max(0f, v)), 150f, decimals: 2)));
        }

        VisualElement BuildCherrySlotRow(ShaperCherryFrame slot)
        {
            var box = Z.Box(null, null);
            int maxFrame = Mathf.Max(0, document.frameCount - 1);

            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder — a slot's position is its play order.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, box, cherryListHost, (from, to) =>
            {
                // Guard the indices: the grip reports positions among the CONTAINER's children, and the
                // container also holds the trailing add-row, so a drop past the last slot would otherwise
                // index outside the data list.
                if (from < 0 || from >= document.cherryFrames.Count) { RebuildCherryList(); return; }
                to = Mathf.Clamp(to, 0, document.cherryFrames.Count - 1);
                Change(() =>
                {
                    var f = document.cherryFrames[from];
                    document.cherryFrames.RemoveAt(from);
                    document.cherryFrames.Insert(to, f);
                });
                ResetCherryPlayback();
                RebuildCherryList();
            });
            header.Add(grip);

            header.Add(Z.Field("Source frame",
                "Which of this document's own frames this slot plays.",
                Z.SliderInt(Mathf.Clamp(slot.sourceIndex, 0, maxFrame), 0, maxFrame,
                    "Which of this document's own frames this slot plays.",
                    v => { Change(() => slot.sourceIndex = v); ResetCherryPlayback(); }, 130f)));

            header.Add(Z.Flexible());
            header.Add(Z.Button("×", "Remove this slot.", () =>
            {
                Change(() => document.cherryFrames.Remove(slot));
                ResetCherryPlayback();
                RebuildCherryList();
            }).W(22f));
            box.Add(header);

            // Length: one fixed multiplier, or a randomised min/max re-drawn on each pass. The toggle
            // rebuilds the row because the two modes need different controls — showing both at once would
            // leave whichever is inactive lying about what it does.
            var lengthRow = new List<VisualElement>
            {
                Z.Toggle("Randomise length",
                    "Draw a new hold length on every pass through the sequence instead of always using the "
                    + "fixed one. The draw is seeded from the document, so a given pass is reproducible.",
                    slot.useMinMaxLength, v =>
                    {
                        Change(() => slot.useMinMaxLength = v);
                        ResetCherryPlayback();
                        RebuildCherryList();
                    }),
            };

            if (slot.useMinMaxLength)
            {
                lengthRow.Add(Z.MicroSlider("Min ×", slot.minLengthMultiplier, 0.25f, 8f,
                    "Shortest hold, in beats, this slot can draw.",
                    v => { Change(() => slot.minLengthMultiplier = v); ResetCherryPlayback(); }, 120f, decimals: 2));
                lengthRow.Add(Z.MicroSlider("Max ×", slot.maxLengthMultiplier, 0.25f, 8f,
                    "Longest hold, in beats, this slot can draw.",
                    v => { Change(() => slot.maxLengthMultiplier = v); ResetCherryPlayback(); }, 120f, decimals: 2));
            }
            else
            {
                lengthRow.Add(Z.MicroSlider("Length ×", slot.lengthMultiplier, 0.25f, 8f,
                    "How long this slot holds, in beats. 1 = one frame's worth of time at the playback rate.",
                    v => { Change(() => slot.lengthMultiplier = v); ResetCherryPlayback(); }, 120f, decimals: 2));
            }

            box.Add(Z.HGroup(lengthRow.ToArray()));
            return box;
        }

        // ── cherry playback ──────────────────────────────────────────────────────────────────────────────

        /// Drop the beat state so the next tick starts the sequence from its first slot. Called whenever the
        /// sequence's meaning changes (toggled, edited, reordered, removed) or playback (re)starts —
        /// mirroring Pyre's own ResetCherryPlayback. Without it, an edit mid-loop would be interpreted
        /// against a slot index that no longer refers to the same slot.
        void ResetCherryPlayback()
        {
            cherryRunning = false;
            if (document != null) cherryState = ShaperCherry.Begin(document);
        }

        /// One playback step under cherry framing. Returns the frame to show, which may be
        /// ShaperCherry.BlankFrame — the preview stage renders that as nothing rather than frame 0.
        int AdvanceCherry(int steps)
        {
            if (!cherryRunning)
            {
                cherryState = ShaperCherry.Begin(document);
                cherryRunning = true;
            }

            // The engine owns the rule; this loop only decides HOW MANY beats elapsed. Stepping one beat at
            // a time (rather than scaling) is what keeps a slot's authored hold honest when the editor drops
            // frames: three beats' worth of real time advances three beats, not one long one.
            for (int i = 0; i < steps; i++)
                cherryState = ShaperCherry.AdvanceOneBeat(cherryState, document);

            return cherryState.frame;
        }
    }
}
