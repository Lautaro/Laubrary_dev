// ShaperDocument — the tool's root authored asset: a canvas, an animation clock, one light rig and an
// ordered list of layers.
//
// It lives in its own file because it is the type every other Shaper file resolves against and the asset a
// user picks in the window; it had been declared at the bottom of ShaperLightRig.cs, which named the file
// after one of the document's members rather than after the document.
using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The document (LR-1.1). <b>The first document-level authored object in Shaper.</b>
    ///
    /// LR-0.1: design C1 says "A Shaper document has a canvas size, a frame count and a rate, a palette, one
    /// light rig, and an ordered list of layers" (<c>SHAPER_THE_DESIGN.md:299</c>) and C6 opens "The document
    /// owns the lights" — but a grep over all eighteen files in <c>Runtime/Shaper/</c> found no
    /// <c>ShaperDocument</c> type and no light symbol of any kind. <c>ShaperFillDocument</c> is not a
    /// document; it is the resolved owner list <c>Resolve</c> returns. So "put the lights on the document"
    /// reads like an edit to an existing type and is in fact an introduction, taken on here deliberately
    /// rather than smuggled onto a node.
    ///
    /// Wave-2 content is exactly what LR-1.1 lists: a canvas size, an ordered list of layers, and one rig.
    ///
    /// T-0144 then added the two C1 members that Wave 2 had deferred -- <see cref="frameCount"/> and
    /// <see cref="frameRate"/> -- so the document finally states its own animation clock rather than having a
    /// frame count handed to a transient <see cref="ShaperFrameCache"/> from outside. The palette is still
    /// C1's and still outstanding.
    /// </summary>
    /// T-0152 made this a ScriptableObject. It had been a plain [Serializable] class, which meant Shaper had
    /// no document ASSET at all -- the only ScriptableObject in the whole runtime was ShaperHeightFieldPreset.
    /// Two consequences, both found by the bake and runtime-playback work rather than by design review: an
    /// editor window had nothing to author against (Pyre's window authors a `Pyre : ScriptableObject` spec,
    /// and this is the direct analogue), and no scene could reference a Shaper document, which is why runtime
    /// playback had to take a baked clip instead of a document.
    ///
    /// The promotion is safe rather than lucky: the ONLY ShaperDocument-typed field anywhere is ShaperClip's
    /// explicitly [NonSerialized] _playbackDoc, so nothing serialized this type BY VALUE and nothing therefore
    /// silently changes from inline data to a reference. Every other use is a method parameter.
    /// The former `public string name = "Document"` field is GONE rather than shadowed with `new`: a
    /// ScriptableObject already has UnityEngine.Object.name, and a shadowing field of the same name is a
    /// genuine trap (two names serialized separately, and `doc.name` meaning different things depending on
    /// the static type at the call site). Object.name is also strictly better here -- renaming the asset in
    /// the Project window now updates it, which an authored string field could never do. The one consumer,
    /// ShaperBaker's bake filename, already falls back when the name is empty, which is exactly the state a
    /// freshly CreateInstance'd document is in. Pyre agrees: its own spec SO has no custom name field either
    /// (the `public string name` in Pyre.cs:184 is on its Layer class, not the asset).
    [CreateAssetMenu(menuName = "Laubrary/Shaper Document", fileName = "Shaper Document")]
    public class ShaperDocument : ScriptableObject, IVisualPreview
    {

        /// <summary>Canvas width in samples. B9's authorable range is 32-256.</summary>
        public int canvasWidth = 96;
        /// <summary>Canvas height in samples.</summary>
        public int canvasHeight = 64;
        /// <summary>Canvas units per sample. 1 makes a "canvas pixel" in a dial equal a sample (LR-1.5).</summary>
        public float pixelSize = 1f;

        /// <summary>
        /// T-0166 — screen pixels per world unit, the same convention <c>Pyre.pixelsPerUnit</c> carries
        /// (<c>Pyre.cs:1195</c>). NOT the same quantity as <see cref="pixelSize"/>, which is a SAMPLING
        /// density (canvas units per sample); this is a DISPLAY scale, read by <c>ShaperBaker</c> when it
        /// slices the baked sheet (<c>TextureImporter.spritePixelsPerUnit</c>) and, through the baked
        /// <c>Sprite</c>'s own import setting, by every runtime consumer (<c>ShaperPlayer</c>,
        /// <c>MirageRig.cs:289</c>) with no separate field needed on <c>ShaperClip</c>.
        ///
        /// Default 16 matches the project's own PPU convention (<c>ShaperBaker.DefaultPixelsPerUnit</c>,
        /// already 16 before this field existed) so an existing document's bake is unchanged until authored.
        /// </summary>
        [Range(1, 64)] public int pixelsPerUnit = 16;

        /// <summary>
        /// T-0166 — composited UNDER every layer by <see cref="ShaperDocumentRenderer"/>, so a bake/GIF/clip
        /// carries it. Distinct from <see cref="previewBackSplash"/>, which is preview-only and never reaches
        /// a bake (see that field's own doc). Default fully transparent, so an existing document renders
        /// bit-identically until authored.
        /// </summary>
        public Color background = new Color(0f, 0f, 0f, 0f);

        /// <summary>Ordered, bottom-most first. Order is authored data and no stage may reorder it.</summary>
        public List<ShaperLayer> layers = new List<ShaperLayer>();

        /// <summary>
        /// T-0109, HS-7.1 — canvas pixels between consecutive layers' BASE PLANES. The reference's
        /// <c>base: order*0.75</c> (<c>index.html:1362</c>) ported by value, with its unit reinterpreted from
        /// grid cells to canvas pixels per LR-1.5.
        ///
        /// Together with <see cref="ShaperLayer.zOffset"/> it gives HS-7.2's
        /// <c>base(i) = i × layerSpacing + zOffset(i)</c>, the layer's ONLY Z contributor — which is exactly
        /// what makes a Z offset a one-line addition with no new buffer: the height field IS the depth buffer,
        /// a layer's surface Z at a sample is <c>base + body·G</c>, and adding a constant to <c>base</c> moves
        /// the whole layer in Z with nothing else to update (HS-7.3).
        ///
        /// In the reference this number is the ONLY way to move a layer in Z at all — two layers there can
        /// never share a Z, and the only control is list order in fixed 0.75-cell steps.
        /// </summary>
        public float layerSpacing = 0.75f;

        /// <summary>LR-1.1 — exactly one, owned here, never by a node or a layer.</summary>
        public ShaperLightRig lightRig = new ShaperLightRig();

        /// <summary>
        /// The document phase, 0..1. LR-1.8: the rig is sampled on the DOCUMENT's clock, not any node's, and
        /// that is the one exception to FC-1.4 — a light is not attached to a node and has no window to
        /// ignore, so there is no second answer to give it and nothing to bypass.
        /// </summary>
        public float phase01 = 0f;

        /// <summary>The seed a <c>MinMax</c> dial on a light draws from (deterministically, LR-1.8 / LT-4).
        /// <see cref="ShaperCherry"/>'s own draws are seeded from here too, for the same reason.</summary>
        public uint seed = 0u;

        // ── The animation clock (T-0144, design C1) ──────────────────────────────────────────────────────
        /// <summary>
        /// How many frames this document resolves to (C1: "it resolves to a sequence of frames"). 1 means a
        /// still document, which is the default so an existing document deserialises unchanged and behaves
        /// exactly as it did before this field existed: at <c>frameCount == 1</c> every frame index maps to
        /// <c>phase01 = 0</c>, which is already <c>ShaperCompiler.Compile</c>'s own default.
        /// </summary>
        [Min(1)] public int frameCount = 1;

        /// <summary>
        /// Playback rate in frames per second.
        ///
        /// Worth saying why a rate exists at all when everything is sampled from a normalised
        /// <see cref="phase01"/> rather than from a wall clock: the phase answers "what does this document
        /// look like at this point in its cycle", which is resolution-independent and has no units. The rate
        /// answers a different question -- "how fast should those frames be shown to a human, and how fast
        /// does a baked clip play". Nothing in the render pipeline reads this; it drives playback and it is
        /// what a bake writes into the resulting animation.
        /// </summary>
        [Range(ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate)]
        public float frameRate = ShaperClock.DefaultFrameRate;

        /// <summary><see cref="phase01"/> for a given frame of THIS document -- the single conversion, shared
        /// with <see cref="ShaperFrameCache"/>. See <see cref="ShaperClock"/> for the convention.</summary>
        public float PhaseOfFrame(int frameIndex) => ShaperClock.PhaseOfFrame(frameIndex, frameCount);

        // ── Cherry framing (T-0143) ──────────────────────────────────────────────────────────────────────
        // Playback-order state only: it selects WHICH of this document's own frames play and for how long,
        // and never changes how any frame is rendered. Everything here defaults to a no-op, so a document
        // saved before cherry framing existed deserialises and plays byte-identically.
        /// <summary>When true, playback follows <see cref="cherryFrames"/> instead of the plain frame order.</summary>
        public bool cherryEnabled = false;
        /// <summary>The authored sub-sequence, in play order.</summary>
        public List<ShaperCherryFrame> cherryFrames = new List<ShaperCherryFrame>();
        /// <summary>Seconds of blank between one pass through the sequence and the next. 0 = loop with no
        /// gap.</summary>
        [Min(0f)] public float cherryLoopDelaySeconds = 0f;

        /// <summary>Preview-only Zound cue (Pyre parity, <c>Runtime/Pyre/Pyre.cs:1251-1252</c>): the cherry
        /// slot index this fires on, once per entry into that slot. -1 = never fires. Never baked -- neither
        /// <see cref="ShaperBaker"/> nor <see cref="ShaperDocumentRenderer"/> (Editor/Shaper) reads this
        /// field or the one below; it only steers the editor's own live preview.</summary>
        [HideInInspector] public int previewZoundFrame = -1;
        /// <summary>Name of the Zound <see cref="previewZoundFrame"/> fires. Always set through a picker
        /// (ShaperZoundPickerHook), never typed -- a typed name compiles, saves and looks authored, then
        /// silently plays nothing the moment it's misspelled.</summary>
        [HideInInspector] public string previewZoundName;

        // ── Preview backdrop (T-0157) ────────────────────────────────────────────────────────────────────
        /// <summary>
        /// The editor preview's backdrop — a flat colour plus one optional image, drawn BEHIND the render.
        /// Purely cosmetic and deliberately per-DOCUMENT, matching Pyre's own <c>spec.previewBackSplash</c>:
        /// which backdrop reads a given effect (a dark one for a spark, a lit interior for smoke) is a
        /// property of that effect, so it belongs with the effect rather than with whoever last opened a
        /// window.
        ///
        /// <b>It can never reach a bake.</b> Nothing in this file's render path reads it: the only consumers
        /// are the editor preview stage's own backdrop layer, and the panel that edits it.
        /// <see cref="ShaperDocumentRenderer"/> never mentions it, so a baked sheet is identical whether a
        /// backdrop is set or not — which is the property that lets a user light their preview for
        /// legibility without silently changing the shipped asset.
        ///
        /// Null until first edited; the panel creates it on demand, so an untouched document serialises
        /// exactly as it did before this field existed.
        /// </summary>
        /// Fully qualified deliberately: `Laubrary.BackSplash` is BOTH a namespace and a class inside it
        /// (BackSplash the ScriptableObject preset), so the short form `BackSplash.BackSplashSettings` is the
        /// same shadowing hazard ZUI's own header documents for `Zui.Zui`. Pyre.cs:1257 qualifies it too.
        [HideInInspector] public Laubrary.BackSplash.BackSplashSettings previewBackSplash;

        /// <summary>
        /// T-0156 — the effects applied to this document's FINISHED picture, in list order.
        ///
        /// T-0114 built the whole universal-effects system and nothing could reference it: no node and no
        /// layer held a list, so the 41-entry catalog was static classification and an effect could not be
        /// applied to anything. This is that list.
        ///
        /// It lives on the DOCUMENT rather than a node or a layer because the stage decides it:
        /// <see cref="ShaperEffectStage.PostComposite"/> runs "once on the FINISHED, folded picture", and the
        /// only fold in the shipped renderer is <see cref="ShaperDocumentRenderer"/>'s layer composite — so
        /// the finished picture IS the document's. See <c>ShaperEffects.cs</c>'s header for why a per-layer
        /// pre-composite list is deliberately not here too (it would force every layer through an 8-bit
        /// round trip inside a premultiplied-float composite).
        ///
        /// Empty by default, so every existing document renders exactly as it did.
        /// </summary>
        public List<ShaperEffectRef> effects = new List<ShaperEffectRef>();

        // ── Layer identity (T-0170) ──────────────────────────────────────────────────────────────────────
        //
        // Ids are allocated LAZILY — only when something first needs to refer to a layer — rather than being
        // stamped on every layer up front. Two reasons, both practical: a document that nobody has masked is
        // then byte-identical to one authored before ids existed, and nothing has to mutate (and dirty) the
        // asset merely because a window drew a picker over it.

        /// <summary>
        /// This layer's <see cref="ShaperLayer.id"/>, allocating one if it has none. 0 for a null layer or one
        /// that is not in this document — an id is only meaningful inside the document that hands it out.
        ///
        /// <b>Call this inside the caller's own Undo scope</b>: it can mutate the layer.
        /// </summary>
        public int IdOf(ShaperLayer layer)
        {
            if (layer == null || layers == null || !layers.Contains(layer)) return 0;
            if (layer.id != 0) return layer.id;

            // Max-plus-one over the whole list, so an id is never reused after a delete. Reuse would silently
            // re-point every mask that named the deleted layer at whatever took its place, which is a far
            // worse failure than running the counter up.
            int max = 0;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].id > max) max = layers[i].id;
            layer.id = max + 1;
            return layer.id;
        }

        /// <summary>The layer with this id, or <c>null</c> — for id 0 (no reference) and for an id whose layer
        /// has been deleted alike. Never throws; a caller distinguishes the two by testing the id itself.</summary>
        public ShaperLayer LayerById(int id)
        {
            if (id == 0 || layers == null) return null;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].id == id) return layers[i];
            return null;
        }

        /// <summary>The list POSITION of the layer with this id, or -1. The renderer needs the index, not just
        /// the layer, because <c>ShaperLightCompiler.BindLayer</c> is index-addressed.</summary>
        public int LayerIndexById(int id)
        {
            if (id == 0 || layers == null) return -1;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].id == id) return i;
            return -1;
        }

        /// <summary>The grid this document's canvas describes, canvas-centred with +Y up (LR-1.5).</summary>
        public ShaperSampleGrid Grid(float edgeSoftness = 0f)
            => ShaperSampleGrid.Centred(canvasWidth, canvasHeight, pixelSize, edgeSoftness);

        // ── IVisualPreview ───────────────────────────────────────────────────────────────────────────────
        // A document IS its picture, so any browser, chip or picker can draw one without knowing Shaper
        // exists. This goes through ShaperDocumentRenderer — the one renderer the window preview and the
        // bake already share — so a thumbnail can never disagree with what the tool shows.
        //
        // No IShaperEffectApplier is passed, because this assembly deliberately cannot see one (the applier
        // lives above Shaper in the dependency graph and ShaperEffects.cs rejects a static hook for it by
        // name). So a document whose authored effects change its look renders here WITHOUT them; a consumer
        // that has the bridge assembly — ShaperWindow's own browser does — supplies the applier itself and
        // gets the effected picture. Same renderer either way; never a second render path.

        /// <summary>A representative still: the middle frame, which reads better than frame 0 for anything
        /// that fades in.</summary>
        public Texture2D RenderPreviewTexture()
        {
            var tex = new Texture2D(Mathf.Max(1, canvasWidth), Mathf.Max(1, canvasHeight),
                                    TextureFormat.RGBA32, false)
            {
                // Point + no mips: pixel art, and the same settings the preview stage and the bake importer
                // pin — a filtered thumbnail would lie about the asset.
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            BlitFrameInto(tex, Mathf.Clamp(frameCount / 2, 0, Mathf.Max(0, frameCount - 1)));
            return tex;
        }

        /// <inheritdoc/>
        public bool CanAnimatePreview => frameCount > 1;

        /// <inheritdoc/>
        public float PreviewFps => frameRate;

        /// <inheritdoc/>
        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (tex == null || frameCount <= 1) return;
            BlitFrameInto(tex, ShaperClock.WrapFrame(
                Mathf.Abs(Mathf.FloorToInt((float)(time * Mathf.Max(1f, PreviewFps)))), frameCount));
        }

        void BlitFrameInto(Texture2D tex, int frameIndex)
        {
            var px = ShaperDocumentRenderer.RenderFrame(this, frameIndex);
            if (px == null || px.Length != tex.width * tex.height) return;
            tex.SetPixels32(px);
            tex.Apply(false);
        }
    }
}
