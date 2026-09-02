using System.Collections.Generic;
using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0150 -- a BAKED Shaper animation, and the only thing runtime playback ever consumes.
    ///
    /// <b>Why the runtime plays a bake rather than the document.</b> Shaper's player cannot mirror
    /// <c>PyreBlastPlayer</c>'s "hold the spec, call the renderer" shape, for two measured reasons:
    ///
    /// <list type="number">
    /// <item><b>There is no document renderer to call.</b> Pyre has <c>PyreRenderer.GetFrames(spec)</c>.
    /// Shaper has no equivalent at any level: <see cref="ShaperFrameCache.ComputeFrame"/> returns a
    /// <see cref="ShaperFieldBuffer"/> -- a canvas-sized <c>float[]</c> DISTANCE field for ONE
    /// <see cref="ShaperNode"/> tree, not colour and not a document; <see cref="ShaperResolve"/> is a ray
    /// query, not a renderer; and <see cref="ShaperFillResolver.PaintTile"/> is the colour path but needs a
    /// driver -- a caller that walks <see cref="ShaperDocument.layers"/>, evaluates each, paints each and
    /// composites the results -- which does not exist anywhere in Runtime or Editor. T-0115's own spec says
    /// so outright: "There is no document/window/canvas-grid-renderer layer in Laubrary's own Shaper yet."</item>
    ///
    /// <item><b>Live evaluation is far over a game frame budget anyway.</b> T-0115 CT9 measured the real
    /// cost model at the design's own worked scale (61 nodes, the default 96x64 grid): ~725-746 ms for 24
    /// uncached frames, i.e. <b>~30 ms per frame</b>, against a 16.7 ms budget for an ENTIRE 60 fps frame --
    /// roughly 2x over budget for one effect before the game draws anything else. Caching collapses the
    /// REPEAT cost (~34-70 ms total for the same 24) but the first pass still computes, so a live-evaluating
    /// spawn would hitch for tens of milliseconds exactly when the effect appears.</item>
    /// </list>
    ///
    /// So this asset is the seam: an editor-side baker fills it, and the runtime cycles a
    /// <see cref="SpriteRenderer"/> through <see cref="frames"/>. Runtime never references the editor
    /// assembly -- it consumes the produced ASSET, exactly as a baked sprite sheet is meant to be consumed.
    ///
    /// <b>No <c>CreateAssetMenu</c> on purpose.</b> A hand-created empty clip has no meaning -- a clip is an
    /// output of baking, not something authored from scratch -- so it is deliberately not offered in the
    /// Create menu, matching the project rule against menu entries nobody asked for.
    /// </summary>
    public class ShaperClip : ScriptableObject, Laubrary.PreviewKit.IVisualPreview, IChunkAnimation, IChunkEffectSpawner
    {
        /// <summary>The baked frames in document order, frame 0 first. A clip with none is not playable.</summary>
        public Sprite[] frames;

        /// <summary>Playback rate the document was authored at (<see cref="ShaperDocument.frameRate"/>),
        /// carried so a player defaults to the speed the author actually saw.</summary>
        [Range(ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate)]
        public float frameRate = ShaperClock.DefaultFrameRate;

        /// <summary>The document's seed, copied at bake time. Feeds <see cref="ShaperCherry"/>'s deterministic
        /// draws so a baked clip's cherry sequence is reproducible per LT-4, not re-rolled per playback.</summary>
        public uint seed;

        // ── cherry framing, copied from the document at bake time ────────────────────────────────────────
        // Copied rather than flattened into the frame order on purpose. Flattening would freeze the
        // per-pass variation T-0143 deliberately built: ShaperCherryFrame's min/max length and multi-frame
        // pick are drawn from (seed, slot, loopIndex), so pass 2 legitimately differs from pass 1. Baking the
        // sequence flat would collapse every pass onto the first one's draws and quietly delete that feature.

        /// <summary>Whether this clip plays its cherry sub-sequence instead of plain frame order.</summary>
        public bool cherryEnabled;

        /// <summary>The authored cherry slots. Only read when <see cref="cherryEnabled"/>.</summary>
        public List<ShaperCherryFrame> cherryFrames = new List<ShaperCherryFrame>();

        /// <summary>Blank gap between cherry loop iterations, seconds.</summary>
        public float cherryLoopDelaySeconds;

        /// <summary>Which document this was baked from -- provenance for a human reading the asset, never parsed.</summary>
        public string sourceDocumentName;

        /// <summary>How many baked frames this clip carries.</summary>
        public int FrameCount => frames != null ? frames.Length : 0;

        /// <summary>False when there is nothing to show -- a player must check before starting.</summary>
        public bool IsPlayable => FrameCount > 0;

        // [NonSerialized] is load-bearing, not decoration: ShaperDocument is [Serializable], so without this
        // Unity would serialise a second, redundant copy of the playback header into every clip asset -- and
        // then deserialise THAT instead of rebuilding it from the fields above, so an edited frameRate would
        // be silently ignored by playback while the inspector showed the new value.
        [System.NonSerialized] ShaperDocument _playbackDoc;

        /// <summary>
        /// A minimal <see cref="ShaperDocument"/> carrying only this clip's PLAYBACK facts (frame count, rate,
        /// seed and the cherry settings), built so <see cref="ShaperCherry.AdvanceOneBeat"/> can be reused
        /// verbatim at runtime instead of a second copy of the cherry rule living here. That duplication is
        /// exactly what T-0143/T-0144 consolidated away, and it would be worse here than anywhere: two cherry
        /// implementations would drift into the editor preview and the game showing different sequences.
        ///
        /// Deliberately does NOT carry layers, the light rig or any node -- none of it is read during playback
        /// of an already-baked clip, and serialising a node tree onto every clip would be dead weight.
        ///
        /// The instance is allocated once and REFRESHED on every read rather than cached-and-returned, so a
        /// clip edited or re-baked mid-session can never hand playback a stale frame count or rate.
        /// </summary>
        public ShaperDocument PlaybackDocument
        {
            get
            {
                // CreateInstance, not `new`: T-0152 promoted ShaperDocument to a ScriptableObject so it could
                // be an authorable asset, and `new` on a ScriptableObject yields an object Unity never
                // initialises. hideFlags keeps this throwaway playback header out of the scene and out of
                // any save -- it is rebuilt from the clip on every read and owns no authored state.
                if (_playbackDoc == null)
                {
                    _playbackDoc = ScriptableObject.CreateInstance<ShaperDocument>();
                    _playbackDoc.hideFlags = HideFlags.HideAndDontSave;
                }

                _playbackDoc.frameCount = Mathf.Max(1, FrameCount);
                _playbackDoc.frameRate = frameRate;
                _playbackDoc.seed = seed;
                _playbackDoc.cherryEnabled = cherryEnabled;
                _playbackDoc.cherryFrames = cherryFrames;
                _playbackDoc.cherryLoopDelaySeconds = cherryLoopDelaySeconds;
                return _playbackDoc;
            }
        }

        // ── IVisualPreview (T-0159) ──────────────────────────────────────────────────────────────────────
        // Self-contained block: a clip is a visual asset, so every picker and browser can show it without a
        // per-window renderer. It reads the ALREADY BAKED sprites rather than re-rendering the document —
        // the frames are the clip's whole content, and re-rendering would both cost ~30 ms a frame and risk
        // disagreeing with what was actually baked.

        /// <inheritdoc/>
        public Texture2D RenderPreviewTexture()
            => IsPlayable ? Laubrary.PreviewKit.PreviewTex.CropSprite(frames[FrameCount / 2]) : null;

        /// <inheritdoc/>
        public bool CanAnimatePreview => FrameCount > 1;

        /// <inheritdoc/>
        public float PreviewFps => frameRate > 0f ? frameRate : ShaperClock.DefaultFrameRate;

        /// <inheritdoc/>
        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (tex == null || FrameCount <= 1) return;
            // Plain frame order, deliberately: the cherry sequence is playback state that needs a running
            // beat sequencer, and a browser thumbnail has no session to keep one in.
            int i = Mathf.Abs(Mathf.FloorToInt((float)(time * PreviewFps))) % FrameCount;
            Laubrary.PreviewKit.PreviewTex.BlitInto(tex, frames[i]);
        }

        // ── IChunkAnimation / IChunkEffectSpawner (T-0161) ───────────────────────────────────────────────
        // Mirrors Runtime/Pyre/PyreChunksDirect.cs's `Pyre : IChunkEffectSpawner, IChunkAnimation` exactly, so
        // any [RequireInterface(typeof(IChunkAnimation))] field (Chunks, Zoetrope's AmmoDef/WeaponDef, etc.)
        // can pick a baked ShaperClip as a visual the same way it already picks a Pyre. GetFrames hands over
        // the ALREADY BAKED sprites -- never a re-render, for the same cost/consistency reasons the class doc
        // above gives for playback. Implicit (not explicit) interface implementation is safe: ShaperClip
        // declares no member literally named Fps, Loop or GetFrames -- frameRate is the nearest name, and Fps
        // below is exactly what it reports.

        /// <see cref="IChunkAnimation.GetFrames"/> -- the clip's own baked frames, verbatim. Callers must not
        /// mutate the returned array (same contract PyreRenderer.GetFrames' cached result carries).
        public Sprite[] GetFrames() => frames;

        /// <see cref="IChunkAnimation.Fps"/> -- the document's authored playback rate.
        public float Fps => frameRate;

        /// True: same reasoning as <c>Pyre.Loop</c> -- a chunk lives until its own lifetime ends, and freezing
        /// a handed-over animation partway through reads as broken rather than finished. A one-shot-and-freeze
        /// visual is the override case a caller builds by driving <see cref="ShaperPlayer"/> directly with
        /// loop=false, not something this hand-over contract needs to express.
        public bool Loop => true;

        /// <see cref="IChunkEffectSpawner.SpawnEffect"/> -- spawn one pooled, one-shot <see cref="ShaperPlayer"/>
        /// at worldPos, angled by rotationDeg, scaled uniformly. Returns the spawned transform so a caller
        /// (Chunks' Pyre-Movement-style module) can fly it. The body is not re-derived here -- it is the
        /// shared <see cref="ShaperDirectSpawn.Spawn"/> helper, mirroring PyreDirectSpawn, so the
        /// pooling/Finished/sorting-layer/loop-leak discipline has exactly one implementation to keep right.
        public Transform SpawnEffect(Vector3 worldPos, float rotationDeg, float scale,
                                     string sortingLayerName, int sortingOrder)
            => ShaperDirectSpawn.Spawn(this, Fps, false, 0f,
                                       worldPos, rotationDeg, scale, sortingLayerName, sortingOrder);
    }
}
