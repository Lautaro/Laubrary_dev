using System;
using Laubrary.Pyre;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0183 — a whole Pyre LAYER as one Shaper shape source, so every animation the owner already authored in
    /// Pyre can be rebuilt inside a Shaper document.
    ///
    /// <b>What was missing.</b> Shaper hosted only the nine <c>PyreForm</c> plug-ins
    /// (<see cref="PyreFormCompositeSource"/>). Pyre's SIXTEEN built-in <see cref="ShapeForm"/> cases — Disc, Gem,
    /// Box, Pyramid, Can, Orb, Ring, Crescent, Star, Polygon, Text, Sprite, Streak, Sparkle, Fire, Fireball — are
    /// not form objects at all; they are an enum dispatched inside <c>PyreRenderer</c>, and they read their dials
    /// off the LAYER (<c>Pyre.cs:180</c>). Shaper's own primitives and solids re-implement some of those silhouettes
    /// with different dials, but Pyre's per-layer life envelopes (Alpha, Size, Edge, Turn/Tilt/Roll or Spin, the
    /// Offset path, the Life-window) had no equivalent anywhere, so a Pyre animation could not be reproduced.
    ///
    /// <b>Why hosting a layer, rather than porting the shapes.</b> Porting would mean a second implementation of
    /// sixteen renderers that must stay pixel-identical to Pyre's forever. This hosts the real thing: the layer is
    /// a real <see cref="PyreLayer"/>, and it is drawn by Pyre's own renderer through its public entry point
    /// (<c>PyreRenderer.RenderFrame</c>). Nothing about Pyre's rendering is reimplemented, reached into, or
    /// changed — a synthetic one-layer spec is exactly the case Pyre calls "byte-identical to the pre-R3 single
    /// layer renderer" (<c>Pyre.cs:1205-1207</c>), so the picture is Pyre's own.
    ///
    /// <b>This does NOT make Shaper into Pyre.</b> A hosted layer is one shape SOURCE among many, entering through
    /// the same <see cref="IShaperCompositeSource"/> contract the Fire and Fireball simulations already use:
    /// publish coverage, publish nothing else. Shaper's primitives, solids, fills, borders, height field and light
    /// rig are untouched and unaffected, and the node's own transform, swarm, fill and effects still apply on top
    /// of whatever this draws.
    ///
    /// <b>The one shape deliberately left out</b> is <see cref="ShapeForm.Playback3D"/>: it has no runtime bake in
    /// Pyre either (<c>PyreRenderer.cs:262-269</c>), only an editor preview driven by a PreviewRenderUtility scene,
    /// so hosting it would render nothing. The two <c>[Obsolete]</c> slots (Inferno, ForkBlast) are out for the
    /// same reason they are retired in Pyre — they live on as <c>PyreForm</c> plug-ins, which Shaper already hosts.
    /// </summary>
    [Serializable]
    public sealed class PyreLayerCompositeSource : IShaperCompositeSource, IShaperCacheableSource
    {
        /// <summary>
        /// The hosted layer — the real Pyre type, with its full dial set: the form's own geometry, the shared
        /// Fill / Alpha / Size envelopes, Edge softness, the Turn / Tilt / Roll (or Spin) rotation trio, the
        /// per-particle Offset path, the Border, the swarm and the modifier stack.
        ///
        /// <c>matteEnabled = false</c> matches the default a fresh Pyre asset's single layer carries
        /// (<c>Pyre.cs:1207</c>): a lone layer has nothing to matte against, and a WriteMatte layer would draw
        /// nothing at all, which as this node's only source would read as a broken generator rather than a
        /// deliberate one.
        /// </summary>
        public PyreLayer layer = new PyreLayer { matteEnabled = false };

        /// <summary>
        /// How many frames the hosted layer thinks its animation has.
        ///
        /// Shaper hands a composite source a continuous <c>phase01</c>, but a Pyre layer's Life-window
        /// (<c>startFrame</c>/<c>endFrame</c>) is authored in FRAMES, and the envelopes' frame markers are drawn
        /// against a frame count. So the phase is mapped onto this many frames and the layer is rendered at that
        /// frame — the same "the sim spans this many steps" dial <see cref="FireCompositeSource.simFrames"/>
        /// already carries.
        ///
        /// <b>T-0204 — no longer an authored dial.</b> This used to be a user-facing "Layer frames" slider that
        /// could disagree with the document's own Frames, which is exactly what let a hosted layer run a SECOND,
        /// independent lifetime alongside the Shaper layer's own Lifetime ("Pyre Box has its own Life (frames)?!
        /// ... This shouldn't be more complex than in Pyre.", owner). The editor card
        /// (<c>Editor/PyreShaper/PyreLayerShaperUI.cs</c>'s Drawer) now silently keeps it equal to the document's
        /// own <c>frameCount</c> on every rebuild, which is what makes <c>phase · (frames − 1)</c> land on the
        /// document's own frame index — the field survives only because <see cref="Render"/> still needs a
        /// frame count to convert phase into a frame index and a headless bake (no editor window ever opened)
        /// must still see something sane here.
        /// </summary>
        [Min(1)]
        [Tooltip("How many frames this layer's own animation spans — kept equal to the document's own Frames "
               + "automatically; no longer authored here.")]
        public int frames = 16;

        // The synthetic host spec. Pyre's renderer takes a whole Pyre asset, so hosting one layer means handing it
        // a one-layer spec — the case Pyre itself documents as byte-identical to its pre-layers renderer. Private
        // and never serialized: it carries no authored data (every field on it is overwritten from the node's own
        // render arguments before each frame), so persisting it would only be a second, stale copy of the canvas
        // size and seed the node already owns. Per-instance rather than one shared static, so two hosted layers in
        // one document can never race each other's canvas size mid-render.
        [NonSerialized] Laubrary.Pyre.Pyre _spec;

        /// <summary>What the picker and the card header call this — the Pyre form's own name.</summary>
        public string SourceLabel => "Pyre " + layer.shapeForm;

        public void Render(int width, int height, float phase01, uint seed, Color32[] target)
        {
            if (target == null || layer == null || width <= 0 || height <= 0) return;
            if (target.Length < width * height) return;

            int n = Mathf.Max(1, frames);
            // The SAME frame→phase mapping Shaper fixes everywhere else, read backwards: ShaperClock maps frame i
            // to i/(N-1), so phase p is frame round(p·(N-1)). Deriving it here rather than inventing a second
            // conversion is what keeps a hosted layer's frame 3 the document's frame 3.
            int frame = n <= 1 ? 0 : Mathf.RoundToInt(Mathf.Clamp01(phase01) * (n - 1));

            // Pyre's canvas is SQUARE (Pyre.cs:1259-1260 — Width and Height are both canvasSize), so a non-square
            // bake box renders at the larger edge and is read out of the middle. Clamping to the smaller edge
            // instead would crop the layer's own reach; this way a wide bake box shows the whole square picture
            // with the top and bottom outside the box discarded, which is a visible framing choice rather than a
            // silently shrunken shape.
            int canvas = Mathf.Max(width, height);

            var spec = _spec;
            if (spec == null)
            {
                spec = _spec = ScriptableObject.CreateInstance<Laubrary.Pyre.Pyre>();
                // Never saved, never shown in the hierarchy or a scene: it exists only for the duration of a
                // render call chain. Without this it would be destroyed on the next scene change mid-bake.
                spec.hideFlags = HideFlags.HideAndDontSave;
            }
            spec.canvasSize = canvas;
            spec.frameCount = n;
            spec.seed = unchecked((int)seed);
            // background stays the asset default (transparent, Pyre.cs:1194): this is a node's silhouette, not a
            // finished picture, so anything but transparent would hand Shaper a filled square as coverage.
            if (spec.layers == null || spec.layers.Count != 1) spec.layers = new System.Collections.Generic.List<PyreLayer> { layer };
            else spec.layers[0] = layer;

            var px = PyreRenderer.RenderFrame(spec, frame);
            if (px == null) { Array.Clear(target, 0, width * height); return; }

            // Centre the square render in the node's bake box. Row 0 is the bottom in both conventions
            // (ShaperCompositeDef.cs:49-52 states Shaper's, and it is Pyre's own), so the copy is a straight
            // row-for-row blit with no flip.
            int dx = (canvas - width) / 2, dy = (canvas - height) / 2;
            for (int y = 0; y < height; y++)
            {
                int sy = y + dy;
                if (sy < 0 || sy >= canvas) { Array.Clear(target, y * width, width); continue; }
                for (int x = 0; x < width; x++)
                {
                    int sx = x + dx;
                    target[y * width + x] = (sx < 0 || sx >= canvas)
                        ? default
                        : px[sy * canvas + sx];
                }
            }
        }

        /// <summary>
        /// T-0115's cache capability. Without it a hosted layer falls back to reference identity, so dragging a
        /// curve point on the Alpha envelope would repaint nothing until the phase happened to change. Every
        /// authored field on the layer is folded in reflectively (through Pyre's own value mixer, so a field added
        /// to <see cref="PyreLayer"/> later is covered without editing this) plus the frame count, which is
        /// authored here rather than on the layer. Phase and seed are deliberately absent: the node's identity
        /// already folds both in before this is consulted (<c>ShaperNodeIdentity.cs:77-78</c>).
        /// </summary>
        public ShaperCacheKey ContentHash()
        {
            var m = ShaperCacheMixer.Begin("shaper.pyreshaper.pyrelayercompositesource.v1");
            m.MixInt(frames);
            // The same FNV offset basis PyreForm.ContentHash seeds with, so the two hashes are the same family.
            m.MixInt(layer != null ? PyreForm.MixValue(unchecked((int)2166136261u), layer) : 0);
            return m.Key;
        }
    }
}
