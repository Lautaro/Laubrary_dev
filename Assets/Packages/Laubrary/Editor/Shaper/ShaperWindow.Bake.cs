// ShaperWindow.Bake — the Bake box (T-0177). Split out of ShaperWindow.cs the same way Preview/Cherry/Lights
// already are, for the same reason: the shell owns the layout skeleton, and this furniture is a separate
// concern.
//
// ── The finding this box exists to fix (T-0154) ────────────────────────────────────────────────────────────
// A Unity AnimationClip has a FIXED keyframe order — cherry framing's per-pass variation comes from
// ShaperCherryState.loopIndex, so pass 2 legitimately differs from pass 1, and an AnimationClip has nowhere
// to put that: it freezes whatever pass 0 happened to be (ShaperBaker.cs:279-288). Only a ShaperClip carries
// the cherry DATA itself and rebuilds a playback document, so ShaperPlayer re-runs the cherry rule live. Before
// this box, the window never said so — the AnimationClip and the ShaperClip looked like interchangeable
// export formats, and cherry framing silently vanished the moment someone picked the wrong one. Every toggle
// below therefore carries a tooltip stating exactly what it preserves (labeling rule, ui-layout-rules.md
// "tooltip, not title" — the explanation lives on hover, never as an on-screen paragraph), and the Bake
// button's own tooltip goes state-dependent for the one combination that silently loses the sequence: cherry
// enabled, AnimationClip ticked, ShaperClip NOT ticked.
//
// ── Why the toggles are window state, not document state ──────────────────────────────────────────────────
// Same reasoning as previewZoom/previewGifScale (ShaperWindow.Preview.cs header): which formats you happen to
// want out of THIS bake session is a workflow preference, not something that should mark the document dirty
// and prompt a save every time it is nudged.
//
// ── Sheet is not a fourth independent toggle ───────────────────────────────────────────────────────────────
// The sprite-sheet PNG is infrastructure the other two asset outputs slice their sprites FROM (ShaperBaker.cs
// step 4 builds spriteOfFrame from it before either the clip or the ShaperClip can be written), so it cannot
// be turned off independently without breaking whichever of them is still ticked. It is shown as an always-on,
// disabled toggle rather than omitted — the brief calls for four rows, and a truthful disabled row says more
// than silently dropping the row would.
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        // ── Bake box state (window state — see file header) ────────────────────────────────────────────────
        [SerializeField] bool bakeAnimationClip = true;
        [SerializeField] bool bakeShaperClip = true;
        [SerializeField] bool bakeGif = false;

        Button bakeButton;

        /// The destination folder a bake will land in — mirrors ShaperBaker.cs:182-190 exactly (beside the
        /// document's own asset when it is saved, else "Assets"), so the readout can never disagree with
        /// where the bake actually goes.
        string BakeDestinationFolder()
        {
            if (document == null) return "Assets";
            string docAssetPath = AssetDatabase.GetAssetPath(document);
            return !string.IsNullOrEmpty(docAssetPath)
                ? Path.GetDirectoryName(docAssetPath).Replace('\\', '/')
                : "Assets";
        }

        VisualElement BuildBakeBox()
        {
            var destinationRow = Z.Field("Destination",
                "Where a bake lands: beside the document's own asset once it has been saved, or \"Assets\" "
                + "for an in-memory document that has not been saved yet. Read-only here — save the document "
                + "to move it.",
                Z.Text(BakeDestinationFolder(), ZuiText.Body,
                    "Where a bake lands: beside the document's own asset once it has been saved, or "
                    + "\"Assets\" for an in-memory document that has not been saved yet."));

            var ppuRow = Dial("Pixels per unit", "Screen pixels per world unit, baked into every sprite output "
                + "(the sheet's import settings and the ShaperClip's own sprites). The same field as the "
                + "Canvas section above — shown here too because it is a bake setting.",
                document.pixelsPerUnit, 1f, 64f,
                v => document.pixelsPerUnit = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64), decimals: 0);

            var sheetToggle = Z.Toggle("Sprite sheet PNG",
                "The sliced frame sheet every other output below reads its sprites from. Always produced "
                + "whenever AnimationClip or ShaperClip is baked, so it cannot be turned off on its own.",
                true, _ => { });
            sheetToggle.SetEnabled(false);

            var clipToggle = Z.Toggle("AnimationClip",
                "A standard Unity AnimationClip: plays the source frames in order on any Animator. Cherry "
                + "framing is NOT preserved — it freezes whichever pass was showing when baked. Use "
                + "ShaperClip for that.",
                bakeAnimationClip, v => { bakeAnimationClip = v; RefreshBakeButtonTooltip(); });

            var shaperClipToggle = Z.Toggle("ShaperClip",
                "The Laubrary ShaperClip asset: carries the authored cherry sequence itself (slots, loop "
                + "delay, seed) and replays it live via ShaperPlayer, per-pass variation included. This is "
                + "the ONLY output that preserves cherry framing.",
                bakeShaperClip, v => { bakeShaperClip = v; RefreshBakeButtonTooltip(); });

            var gifToggle = Z.Toggle("GIF",
                "Also export an animated GIF into the destination folder above, using the GIF scale/dither "
                + "settings in the transport. A GIF plays the same source frame order as the other outputs "
                + "but, like AnimationClip, has no way to carry cherry's per-pass variation across loops.",
                bakeGif, v => { bakeGif = v; RefreshBakeButtonTooltip(); });

            bakeButton = Z.Button("Bake", BakeButtonTooltip(), DoBake);

            var box = Z.Box("Bake",
                "Writes this document's outputs to disk, beside the document's own asset. Never overwrites an "
                + "existing bake — a repeat bake is versioned.",
                Z.Column(
                    destinationRow,
                    ppuRow,
                    sheetToggle,
                    clipToggle,
                    shaperClipToggle,
                    gifToggle,
                    bakeButton));
            return box;
        }

        /// The state-dependent Bake-button tooltip (T-0177 labeling rule: this warning is a TOOLTIP, never an
        /// on-screen paragraph). Only the one silently-lossy combination gets the extra sentence: cherry is
        /// enabled, AnimationClip is going to be baked, and ShaperClip — the only output that preserves it —
        /// is not.
        string BakeButtonTooltip()
        {
            const string baseTooltip = "Bake this document to disk. Ticked outputs above are written beside "
                + "the document's own asset; the sprite sheet is always included.";
            if (document != null && document.cherryEnabled && bakeAnimationClip && !bakeShaperClip)
            {
                return baseTooltip + " This document's cherry sequence will NOT be preserved by this bake — "
                    + "AnimationClip freezes one pass and ShaperClip is unticked. Tick ShaperClip to keep it.";
            }
            return baseTooltip;
        }

        void RefreshBakeButtonTooltip()
        {
            if (bakeButton != null) bakeButton.tooltip = BakeButtonTooltip();
        }

        void DoBake()
        {
            var result = ShaperBaker.Bake(document, pixelsPerUnit: document.pixelsPerUnit,
                bakeAnimationClip: bakeAnimationClip, bakeShaperClip: bakeShaperClip);
            if (!result.ok)
            {
                Debug.LogError("[Shaper] Bake failed: " + result.message, document);
                return;
            }

            Debug.Log($"[Shaper] Baked {result.sheetFrames} frame(s) → {result.sheetPath}", document);
            var sheet = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.sheetPath);
            if (sheet != null) EditorGUIUtility.PingObject(sheet);

            // The GIF toggle rides the Bake button rather than needing its own click: it shares the same
            // destination-folder convention as the other outputs (unlike the transport's own "GIF…" button,
            // which always asks via a Save dialog — this one does not, because it belongs to a batch that
            // already knows where it is going).
            if (bakeGif && document != null)
            {
                string gifPath = BakeDestinationFolder() + "/" + (document.name ?? "Shaper") + ".gif";
                ShaperGif.Export(document, gifPath, Mathf.Clamp(previewGifScale, 1, 8), previewGifDither);
            }
        }
    }
}
