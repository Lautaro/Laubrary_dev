using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Zoetrope
{
    /// Makes a <see cref="Zoe"/> itself pickable wherever an <see cref="IChunkAnimation"/> is asked for —
    /// a Chunks spec's Animation Source, an AmmoDef's Visual, anything with
    /// <c>[RequireInterface(typeof(IChunkAnimation))]</c>. No wrapper asset, no second thing to author and
    /// keep in sync: the character asset the user already has IS the pickable visual.
    ///
    /// WHY THIS LIVES IN ZOETROPE, not in Chunks: the dependency direction is fixed and one-way. Zoetrope's
    /// asmdef already references Chunks (AmmoDef, the Zoe FX bridges), so Chunks can never reference Zoetrope
    /// back without an assembly cycle that simply will not compile. Every cross-tool adaptation therefore
    /// happens on THIS side of the arrow, implementing the small interface Chunks publishes. Identical
    /// reasoning and identical shape to <c>Laubrary.Pyre.PyreChunkAnimation</c> — see IChunkAnimation's own
    /// doc comment, which names exactly this case.
    ///
    /// WHAT A CONSUMER GETS: the character's idle frames, in order. Chunks uses them two ways — frame 0 is the
    /// still a fracture is cut from (the Fragment Slicer needs a Sprite, and this is how it gets one from a Zoe
    /// without Chunks ever learning what a Zoe is), and the whole array is what each flung chunk cycles while
    /// it flies. So "fracture the Floating Disc" is: pick the Floating Disc Zoe into the Chunks picker, done.
    ///
    /// A separate partial file rather than more lines on Zoe.cs, because this is a BRIDGE concern: Zoe.cs stays
    /// the character recipe, and deleting Chunks from a consumer project means deleting one file here, not
    /// surgery on the core asset.
    public partial class Zoe : IChunkAnimation
    {
        // Which clip is "the character, standing there"? Two places legitimately answer that, and they are not
        // the same field:
        //
        //   1. `locomotion.idleClip` — Zoe-level (Runtime/Zoetrope/Locomotion.cs). The character's own choice of
        //      resting animation, authored on this asset. Usually empty.
        //   2. The VIEW's own idle — e.g. ZonedLauminaryView.idleClip / LauminaryView.idleClip
        //      (Runtime/ZoetropeLaunimator/), which is what those views ALREADY resolve inside their no-argument
        //      IPreviewableView.PreviewFrames(). Core Zoetrope cannot see that field (it lives in the bridge
        //      assembly) and must not try to.
        //
        // So: honour (1) when it is authored AND the view can preview a named clip, otherwise let the view
        // answer for itself via (2). That ordering matters — a Zoe that explicitly names an idle clip should win
        // over the view's default, but the common case (empty locomotion.idleClip) must NOT collapse to "no
        // clip name, so nothing to show"; it has to fall through to the view, which knows its own idle and, via
        // LauminaryPreview.Pick, further falls back to the first animation that actually has frames.
        string ChunkIdleClip => locomotion != null ? locomotion.idleClip : null;

        /// The idle frames, in order. Never null, never contains a null element, never throws.
        ///
        /// A Zoe with no view, no art, or a view that cannot preview itself is a legitimate mid-authoring state
        /// — the user picked the character before drawing it — so every one of those degrades to "nothing to
        /// show" and lets Chunk.Setup fall back to whatever sprite the emitter assigned. Previewing is an
        /// OPTIONAL view capability by design (see ICharacterView.cs): a view that does not implement it is
        /// answering the question honestly, not failing.
        public Sprite[] GetFrames()
        {
            Sprite[] frames = null;

            string idle = ChunkIdleClip;
            if (!string.IsNullOrEmpty(idle) && view is IClipPreviewableView clipView)
                frames = clipView.PreviewFrames(idle);

            // Fall through on an empty result too, not just on a missing capability: IClipPreviewableView is
            // allowed to return nothing for an unknown clip name, and the view's own default is a better answer
            // than a blank chunk.
            if ((frames == null || frames.Length == 0) && view is IPreviewableView previewView)
                frames = previewView.PreviewFrames();

            return Compact(frames);
        }

        /// Playback rate for exactly the frames GetFrames returned — resolved down the SAME branch, so the
        /// character never plays one clip at another clip's speed.
        ///
        /// The 12 fps floor matches this asset's IVisualPreview.PreviewFps: a view reports 0 to mean "no
        /// meaningful animation speed", and handing 0 to Chunk's playback clock would freeze a multi-frame
        /// character on frame 0 (it clamps to 0.01 fps — 100 seconds a frame, indistinguishable from stuck).
        public float Fps
        {
            get
            {
                string idle = ChunkIdleClip;
                float fps = 0f;

                if (!string.IsNullOrEmpty(idle) && view is IClipPreviewableView clipView)
                    fps = clipView.PreviewFpsOf(idle);

                if (fps <= 0f && view is IPreviewableView previewView)
                    fps = previewView.PreviewFps;

                return fps > 0f ? fps : 12f;
            }
        }

        /// Always true: this is an IDLE cycle, and a character standing still stands still for as long as the
        /// chunk lives. Unlike a Pyre blast (a one-shot that freezes on its last frame, hence PyreChunkAnimation's
        /// authorable `loop` toggle), an idle has no meaningful "finished" state to hold on.
        public bool Loop => true;

        /// Strips nulls and normalises null/empty to a shared empty array. IChunkAnimation's contract says
        /// callers must not mutate what they get, and Chunk.cs indexes straight into it — one null element
        /// would blank a frame mid-flight for no visible reason, which is far harder to diagnose than a
        /// character that simply shows fewer frames.
        static Sprite[] Compact(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) return System.Array.Empty<Sprite>();

            int n = 0;
            for (int i = 0; i < frames.Length; i++) if (frames[i] != null) n++;
            if (n == frames.Length) return frames;          // the overwhelmingly common case — no copy
            if (n == 0) return System.Array.Empty<Sprite>();

            var kept = new Sprite[n];
            int w = 0;
            for (int i = 0; i < frames.Length; i++) if (frames[i] != null) kept[w++] = frames[i];
            return kept;
        }
    }
}
