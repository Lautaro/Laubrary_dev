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
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        // ── cosmetic view state (see the file header for why this is not on the document) ────────────────
        [SerializeField] bool previewShowFrame;
        [SerializeField] float previewZoom = 1f;

        // GIF export (T-0160, Pyre parity): the scale/dither pair rides the window for the same reason as
        // previewZoom above — they steer the ONE-TIME export call, never the document the bake reads from.
        [SerializeField] int previewGifScale = 1;
        [SerializeField] bool previewGifDither = true;

        // ── cherry playback state ────────────────────────────────────────────────────────────────────────
        // The window HOLDS the state; the engine OWNS the rule. ShaperCherry.AdvanceOneBeat is a pure
        // function of (state, document), which is exactly why T-0143 gave it an explicit state struct
        // instead of Pyre's shape, where the beat state is entangled with window fields.
        ShaperCherryState cherryState;
        bool cherryRunning;

        VisualElement backdropHost;

        // T-0188 — the transport's status line. See BuildTransportStatus for why the transport needed a
        // second voice at all.
        Label transportStatus;
        string transportStatusText;

        // ── preview chrome ───────────────────────────────────────────────────────────────────────────────

        /// The row that sits under the preview: frame border + zoom. The filmstrip contact sheet is a
        /// separate row inside BuildTransport (ShaperWindow.cs, T-0176) — it needs the transport's own
        /// currentFrame/jump plumbing, not this chrome row's cosmetic-only state.
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

            // T-0190 (PM vet of T-0188): the status line is NOT in this row any more. It shared the row with
            // Frame and Zoom, and a long cherry message ("Cherry framing: Play follows 2 slots…") ran straight
            // over the Zoom slider — a variable-width string competing for space with a control. It now has
            // its own reserved line under the transport (BuildRight), which is also where it belongs: it
            // describes playback, not the picture's cosmetics.
            return Z.HGroup(frameToggle, zoom);
        }

        /// The transport's status line (T-0188).
        ///
        /// ── Why the transport needed a second voice ───────────────────────────────────────────────────
        /// The Frame slider is a SOURCE-frame index, but under cherry framing the playhead is a (slot, beat)
        /// pair: a two-slot sequence over a sixteen-frame document parks the slider on two of its sixteen
        /// positions and holds it there, and the loop gap blanks the picture outright (a real authored
        /// state — ShaperCherry.BlankFrame, honoured by ShaperPreviewStage.Refresh). One control was being
        /// asked to mean two different things, and in the cherry meaning it looks exactly like a transport
        /// that has stopped. That is the whole of the owner's "I pressed play, the transport did not move,
        /// the preview turned empty": playback was running correctly and had no way to say so.
        ///
        /// The fix is to name the second meaning rather than to remove it. Substituting frame 0 for a blank
        /// beat, or falling back to the plain frame order when a cherry sequence is empty, would hide an
        /// authoring state the user deliberately created — swapping a confusing preview for a lying one.
        ///
        /// The same line covers the other way Play can look dead: while the pre-baker has not reached the
        /// next frame yet, PlaybackTick deliberately holds the current one (ShaperWindow.cs), so the status
        /// says it is caching instead of leaving the user with a frozen picture and no explanation.
        VisualElement BuildTransportStatus()
        {
            transportStatusText = null;
            transportStatus = Z.Text("", ZuiText.Body,
                "What playback is doing right now. Under cherry framing the Frame slider shows the SOURCE "
                + "frame the sequence chose, so it only visits the frames the sequence names and the picture "
                + "goes blank during the loop gap — this line says so instead of leaving that looking broken.");
            // Stable-workspace rule: a status line is a PERMANENTLY reserved single line whose text changes,
            // never its geometry — a line that grows from zero height would shove the preview above it every
            // time playback started, which is the exact jitter that rule exists to prevent. Truncates rather
            // than wraps for the same reason, and sits LAST in its row so nothing follows it to be pushed.
            transportStatus.style.height = 16f;
            transportStatus.style.flexShrink = 0f;
            // Its OWN row, full width, under the transport (T-0190): nothing shares the line, so a long
            // message truncates against the panel edge instead of over a neighbouring control. Left-aligned
            // now that it starts the line rather than ending someone else's.
            transportStatus.style.width = new Length(100f, LengthUnit.Percent);
            transportStatus.style.whiteSpace = WhiteSpace.NoWrap;
            transportStatus.style.overflow = Overflow.Hidden;
            transportStatus.style.textOverflow = TextOverflow.Ellipsis;
            transportStatus.style.unityTextAlign = TextAnchor.MiddleLeft;
            // Polled rather than pushed: the states it reports change on the editor's own clock (a pre-baker
            // tick, a held cherry beat) and two of them occur on exactly the paths that do NOT call
            // RefreshPreview — that is what made playback look dead in the first place. The work is one
            // string build compared against the last, so a poll is cheaper than threading a new event
            // through the transport, and it cannot go stale the way a push from one call site would.
            transportStatus.schedule.Execute(RefreshTransportStatus).Every(120);
            RefreshTransportStatus();
            return transportStatus;
        }

        void RefreshTransportStatus()
        {
            if (transportStatus == null) return;
            string t = DescribeTransport();
            if (t == transportStatusText) return;
            transportStatusText = t;
            transportStatus.text = t;
        }

        string DescribeTransport()
        {
            if (document == null) return string.Empty;
            // T-0190 — the transport is built for a still document too, so this line is what explains the
            // disabled Play button rather than leaving a dead control unexplained.
            if (document.frameCount <= 1)
                return "One frame — nothing to play. Raise Frames in the Canvas card to give this a timeline.";

            if (document.cherryEnabled)
            {
                int slots = document.cherryFrames?.Count ?? 0;
                // Cherry on with nothing to play is a dead end the engine renders as a permanent blank
                // (ShaperCherry.AdvanceOneBeat returns BlankFrame every beat). Say that, rather than let it
                // read as a broken Play button.
                if (slots == 0)
                    return "Cherry framing is on with no slots — nothing to play. Add a slot, or switch it off.";
                if (!playing)
                    return $"Cherry framing: Play follows {slots} slot{(slots == 1 ? "" : "s")}, not the frame order.";
                return cherryState.frame < 0
                    ? $"Cherry gap — blank between loops ({document.cherryLoopDelaySeconds:0.##}s)"
                    : $"Cherry slot {cherryState.slot + 1}/{slots} → frame {cherryState.frame}";
            }

            if (!playing) return string.Empty;

            // The plain loop's own gap (T-0190) — the picture is deliberately blank, exactly as it is under
            // cherry, so it gets the same explanation rather than reading as a stall.
            if (plainLoopBlankUntil > 0.0 && EditorApplication.timeSinceStartup < plainLoopBlankUntil)
                return $"Loop gap — blank between passes ({document.loopDelaySeconds:0.##}s)";

            int cached = stage != null ? stage.CountCachedFrames() : 0;
            return cached < document.frameCount
                ? $"Playing — frame {currentFrame + 1}/{document.frameCount}, caching {cached}/{document.frameCount}"
                : $"Playing — frame {currentFrame + 1}/{document.frameCount}";
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

        // ── cherry framing authoring lives in ShaperWindow.Cherry.cs (T-0176: thumbnail grid, drag,
        // multi-select, right-click editor, Delete, Zound trigger) ─────────────────────────────────────────

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
            {
                int prevSlot = cherryState.slot;
                cherryState = ShaperCherry.AdvanceOneBeat(cherryState, document);
                // T-0176 — fire on ENTRY into the named slot (a slot change, not merely "is this the slot"),
                // so a held slot with lengthMultiplier > 1 fires once per visit rather than once per beat.
                if (cherryState.slot != prevSlot) FireZoundCueIfMatch(cherryState.slot);
            }

            return cherryState.frame;
        }

        /// Preview-only Zound cue (T-0176, Pyre parity — Runtime/Pyre/Pyre.cs:1251-1252 records the same idea
        /// but Pyre never wired the fire itself, per PyreWindow.cs:281's own comment). Fires once per entry
        /// into the cherry slot document.previewZoundFrame names; never reaches ShaperBaker or
        /// ShaperDocumentRenderer, so it can never affect a bake.
        void FireZoundCueIfMatch(int enteredSlot)
        {
            if (document == null || document.previewZoundFrame < 0 || enteredSlot != document.previewZoundFrame)
                return;
            if (string.IsNullOrEmpty(document.previewZoundName)) return;
            ShaperZoundPickerHook.Preview?.Invoke(document.previewZoundName);
        }
    }
}
