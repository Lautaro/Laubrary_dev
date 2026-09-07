// ShaperLayerKey — the content key of ONE layer's painted buffers, and of a whole frame's composite.
//
// T-0194. Shaper's preview cached FINISHED FRAMES and nothing smaller, so every authored edit dropped every
// frame and every frame re-resolved every layer: hiding a layer, nudging a Z offset or recolouring the
// background each cost a full re-resolve of the whole document. Pyre has not behaved that way since it grew
// PyreLayerKey/PyreLayerCache — a layer is keyed by what its render READS, so editing one layer re-renders
// one layer and everything else is re-composited from buffers already in hand. This file is that key for
// Shaper, written against Shaper's own document shape rather than shared with Pyre's (the two have no common
// layer type, and a shared reflection walker across two packages would couple their asmdefs for no gain).
//
// ── What is in a layer's PAINT key, and what is deliberately not ────────────────────────────────────────────
// IN: every Unity-serialized field of the layer that its own paint pass reads — the node tree (SerializeReference
// ShaperNode, walked whole), the light response, the height stage, the mask settings, the per-layer pre-composite
// effect list; plus the layer INDEX (ShaperLightCompiler.BindLayer is index-addressed), the layer's BASE PLANE
// (HS-7.2's i×layerSpacing + zOffset, which the height and light stages compile against —
// ShaperLightCompiler.cs:492), the phase, and the document parts a layer's paint reads: canvas size, pixelSize,
// seed and the whole light rig.
//
// OUT, because the paint pass never reads them: `enabled`, `name`, `contributesToPicture`, `startFrame`/
// `endFrame` and `id`. Those decide WHETHER a layer is painted into a given frame, never what its buffer
// contains — which is exactly what makes a layer toggle a composite-only change instead of a re-resolve.
// The document background and the document's POST-composite effect list are out for the same reason: they run
// on the folded picture, after every layer buffer exists.
//
// A masked layer additionally folds in its mask SOURCE's own paint key (and whether that source is alive at
// this frame), because ShaperDocumentRenderer.BuildMaskField resolves that source and cuts this layer's buffer
// with it. The source is read UNMASKED (ShaperLayerMask.SourceIsReadUnmasked), so the fold is one level deep by
// construction and no cycle is representable.
//
// Two keys are equal only when the painted bytes would be equal; the reverse is not promised — a field the
// paint pass happens to ignore still changes the key, which costs one needless re-render and never a stale
// buffer. That asymmetry is the only safe direction for a render cache.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Laubrary.Shaper
{
    public static class ShaperLayerKey
    {
        const int MaxDepth = 12;

        static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();
        static readonly object _fieldsLock = new object();

        /// <summary>The part of every layer key that comes from the DOCUMENT: the canvas the layer is sampled
        /// on, the seed every draw hashes from, and the light rig every layer is bound against.</summary>
        public static ShaperCacheKey DocumentPart(ShaperDocument doc)
        {
            var m = ShaperCacheMixer.Begin("shaper.doc");
            if (doc == null) return m.Key;
            m.MixInt(Mathf.Max(1, doc.canvasWidth));
            m.MixInt(Mathf.Max(1, doc.canvasHeight));
            m.MixFloat(doc.pixelSize);
            m.MixUInt(doc.seed);
            MixValue(ref m, doc.lightRig, null, 0);
            return m.Key;
        }

        /// <summary>
        /// The FRAME-INDEPENDENT half of a layer's key: the document part, the layer's index, and every
        /// authored field of the layer its paint pass reads. Split out from <see cref="ShaperLayerKeys.PaintKey"/>
        /// because this is the expensive half — a reflection walk of a whole ShaperNode tree, measured at
        /// ~0.8 ms per layer — and it is identical for every frame of the document. Folding it per frame made an
        /// invalidation of a 4-layer, 16-frame document cost 52 ms of hashing, which is more than the
        /// re-composite it was deciding about. <see cref="ShaperLayerKeys"/> is what memoizes it.
        /// </summary>
        public static ShaperCacheKey ContentKey(ShaperDocument doc, int layerIndex, ShaperCacheKey documentPart)
        {
            var m = ShaperCacheMixer.Begin("shaper.layer");
            m.MixKey(documentPart);
            m.MixInt(layerIndex);
            var layer = LayerAt(doc, layerIndex);
            if (layer == null) { m.MixInt(-1); return m.Key; }
            MixLayerContent(ref m, layer);
            return m.Key;
        }

        /// <summary>The layers that actually paint into <paramref name="frameIndex"/>, in list order — the
        /// same four tests <c>ShaperDocumentRenderer.RenderPhaseInto</c>'s walk applies, kept here so the
        /// preview cache asks the question exactly once and cannot drift from the renderer.</summary>
        public static void ContributingLayers(ShaperDocument doc, int frameIndex, List<int> into)
        {
            into.Clear();
            if (doc?.layers == null) return;
            int fc = Mathf.Max(1, doc.frameCount);
            for (int li = 0; li < doc.layers.Count; li++)
                if (Contributes(doc.layers[li], frameIndex, fc)) into.Add(li);
        }

        internal static bool Contributes(ShaperLayer lay, int frameIndex, int frameCount)
        {
            if (lay == null || !lay.enabled || lay.root == null || !lay.contributesToPicture) return false;
            int end = lay.endFrame < 0 ? frameCount - 1 : lay.endFrame;
            return frameIndex >= lay.startFrame && frameIndex <= end;
        }

        internal static ShaperLayer LayerAt(ShaperDocument doc, int index)
            => doc?.layers != null && index >= 0 && index < doc.layers.Count ? doc.layers[index] : null;

        /// The layer's own authored content, minus the five fields that decide only WHETHER it paints.
        static void MixLayerContent(ref ShaperCacheMixer m, ShaperLayer layer)
        {
            foreach (var f in SerializedFields(typeof(ShaperLayer)))
            {
                if (f.Name == "enabled" || f.Name == "name" || f.Name == "contributesToPicture"
                    || f.Name == "startFrame" || f.Name == "endFrame" || f.Name == "id") continue;
                m.MixString(f.Name);
                MixValue(ref m, f.GetValue(layer), f, 0);
            }
        }

        // ── the hash walk ───────────────────────────────────────────────────────────────────────────────────
        // Deliberately the same shape as PyreLayerKey's walk (Runtime/Pyre/PyreLayerKey.cs:108-222), because the
        // question is the same one — "what would Unity serialize for this object graph" — and answering it twice
        // in two different ways is how two caches start disagreeing about what an edit means.

        /// Unity's serialization rule, which is also the rule for "is this a dial or scratch": public or
        /// [SerializeField]/[SerializeReference], not [NonSerialized] / static / readonly / const. Cached per type,
        /// because reflecting a ShaperNode tree per layer per frame is otherwise the cost this file exists to save.
        static FieldInfo[] SerializedFields(Type t)
        {
            lock (_fieldsLock)
            {
                if (_fields.TryGetValue(t, out var cached)) return cached;
                var list = new List<FieldInfo>();
                for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                    foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsStatic || f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                        if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), true)
                            && !f.IsDefined(typeof(SerializeReference), true)) continue;
                        list.Add(f);
                    }
                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                cached = list.ToArray();
                _fields[t] = cached;
                return cached;
            }
        }

        /// A null in a field Unity rewrites on serialization hashes as what Unity writes: an empty string, an
        /// empty list, "no object" for a Unity reference, and for a plain [Serializable] class a DEFAULT
        /// INSTANCE (Unity cannot store a null there; it constructs one). A [SerializeReference] null stays
        /// null through Unity's own serialization, so it hashes as null.
        static readonly Dictionary<Type, object> _defaults = new Dictionary<Type, object>();
        static object NullAsSerialized(FieldInfo f)
        {
            if (f == null) return null;
            var t = f.FieldType;
            if (t == typeof(string)) return "";
            if (typeof(UnityEngine.Object).IsAssignableFrom(t) || typeof(Delegate).IsAssignableFrom(t)) return null;
            if (t.IsArray || typeof(IList).IsAssignableFrom(t)) return Array.Empty<object>();
            if (f.IsDefined(typeof(SerializeReference), true)) return null;
            if (!t.IsClass || t.IsAbstract || !t.IsSerializable || t.GetConstructor(Type.EmptyTypes) == null) return null;
            lock (_defaults)
            {
                if (!_defaults.TryGetValue(t, out var d))
                {
                    try { d = Activator.CreateInstance(t); } catch { d = null; }
                    _defaults[t] = d;
                }
                return d;
            }
        }

        internal static void MixDocumentComposite(ref ShaperCacheMixer m, ShaperDocument doc, bool effectsEnabled)
        {
            MixValue(ref m, doc.background, null, 0);
            if (effectsEnabled) MixValue(ref m, doc.effects, null, 0);
        }

        static void MixValue(ref ShaperCacheMixer m, object v, FieldInfo field, int depth)
        {
            if (v == null) v = NullAsSerialized(field);
            if (v == null)
            {
                if (field != null && typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) m.MixInt(0);
                else m.MixInt(0x7f);
                return;
            }
            // A depth cap rather than a visited set: Unity's own serializer has one too, so a graph deeper than
            // this is a graph Unity would already have truncated when writing the asset.
            if (depth > MaxDepth) { m.MixInt(0x7e); return; }

            switch (v)
            {
                case float f: m.MixFloat(f); return;
                case int i: m.MixInt(i); return;
                case uint u: m.MixUInt(u); return;
                case bool b: m.MixBool(b); return;
                case Enum e: m.MixInt(Convert.ToInt32(e)); return;
                case double d: m.MixFloat((float)d); m.MixInt(unchecked((int)BitConverter.DoubleToInt64Bits(d))); return;
                case long l: m.MixInt(unchecked((int)l)); m.MixInt(unchecked((int)(l >> 32))); return;
                case byte by: m.MixInt(by); return;
                case string s: m.MixString(s); return;
                case Gradient g:
                    foreach (var k in g.colorKeys) { MixValue(ref m, k.color, null, depth + 1); m.MixFloat(k.time); }
                    foreach (var k in g.alphaKeys) { m.MixFloat(k.alpha); m.MixFloat(k.time); }
                    m.MixInt((int)g.mode);
                    return;
                case AnimationCurve ac:
                    foreach (var k in ac.keys)
                    {
                        m.MixFloat(k.time); m.MixFloat(k.value);
                        m.MixFloat(k.inTangent); m.MixFloat(k.outTangent);
                        m.MixFloat(k.inWeight); m.MixFloat(k.outWeight);
                        m.MixInt((int)k.weightedMode);
                    }
                    m.MixInt((int)ac.preWrapMode); m.MixInt((int)ac.postWrapMode);
                    return;
                case Color c: m.MixFloat(c.r); m.MixFloat(c.g); m.MixFloat(c.b); m.MixFloat(c.a); return;
                case Color32 c32: m.MixInt(c32.r | (c32.g << 8) | (c32.b << 16) | (c32.a << 24)); return;
                case Vector2 v2: m.MixVector2(v2); return;
                case Vector3 v3: m.MixFloat(v3.x); m.MixFloat(v3.y); m.MixFloat(v3.z); return;
                case Vector4 v4: m.MixFloat(v4.x); m.MixFloat(v4.y); m.MixFloat(v4.z); m.MixFloat(v4.w); return;
                // An asset reference is identity, not content: a Sprite or a font the compiler rasterises is
                // keyed by WHICH asset it is. Re-importing the same asset in place is the one edit this misses,
                // and it already forces a domain reload, which empties the cache anyway.
                case UnityEngine.Object uo: m.MixInt(uo ? uo.GetInstanceID() : 0); return;
                case IList list:
                    m.MixInt(list.Count);
                    foreach (var e in list) MixValue(ref m, e, null, depth + 1);
                    return;
            }

            var t = v.GetType();
            if (t.IsPrimitive) { m.MixInt(v.GetHashCode()); return; }
            // Materialise null dials first, so the key reads the same state every load, undo snapshot and
            // Instantiate produces — a layer built in memory keys exactly like the asset it becomes. Only the
            // null-filling half of the owner's callback runs here: its migration half rewrites the dials from
            // the legacy floats whenever the promoted flag is false, and on an object constructed in code that
            // flag IS false, so hashing a freshly seeded layer used to erase its seeded curves (the new-document
            // growth animation rendered as a static full-canvas rectangle).
            if (v is IShaperDialOwner owner) owner.EnsureDials();
            else if (v is ISerializationCallbackReceiver r) r.OnAfterDeserialize();
            // A plain [Serializable] class/struct (ZUIValue, a ShaperNode, a fill, a height def): its CONTENT,
            // never its reference — a clone must hash equal, an edited field must not. The type name is folded in
            // so two SerializeReference subclasses with identical field values still key apart.
            m.MixString(t.FullName);
            foreach (var f in SerializedFields(t))
            {
                m.MixString(f.Name);
                MixValue(ref m, f.GetValue(v), f, depth + 1);
            }
        }
    }

    /// <summary>
    /// <b>The memo that makes the layer cache affordable (T-0194).</b> Holds the document part and every
    /// layer's frame-INDEPENDENT content key, so a pass that touches many frames of one document pays the
    /// reflection walk once per layer instead of once per layer per frame. Measured on a 4-layer, 16-frame
    /// document: 3.3 ms per frame of hashing becomes 3.3 ms for the whole pass.
    ///
    /// <b>The memo is never carried ACROSS a render.</b> <see cref="ShaperDocumentRenderer.RenderPhaseInto"/>
    /// rebuilds it on entry, every call, so a render can never read a key built before an edit. That was
    /// measured rather than assumed: an earlier version reused the memo whenever the document object and layer
    /// count were unchanged, and a probe that edited a dial and re-rendered scored 64 cache HITS where it
    /// should have scored 48 — the stale key had made a changed layer look unchanged, which for a render cache
    /// is the one failure that matters. A rebuild costs one reflection walk per layer (0.22 ms for four layers)
    /// against a resolve of 20 ms, so the safe order is not a trade.
    ///
    /// What the memo still buys is the SCAN: <c>ShaperPreviewFrameCache.Invalidate</c> re-keys every resident
    /// frame without rendering any of them, and <see cref="Rebuild"/> once (via
    /// <see cref="ShaperLayerBufferCache.BeginPass"/>) turns 3.3 ms of hashing per frame into 3.3 ms for the
    /// whole invalidation.
    /// </summary>
    public sealed class ShaperLayerKeys
    {
        ShaperDocument built;
        ShaperCacheKey[] content = System.Array.Empty<ShaperCacheKey>();

        /// <summary>The document half of every key — canvas, pixel size, seed, light rig.</summary>
        public ShaperCacheKey DocumentPart { get; private set; }

        public int LayerCount => content.Length;

        /// <summary>Re-walk every layer. The expensive call; make it once per pass.</summary>
        public void Rebuild(ShaperDocument doc)
        {
            built = doc;
            DocumentPart = ShaperLayerKey.DocumentPart(doc);
            int n = doc?.layers?.Count ?? 0;
            if (content.Length != n) content = new ShaperCacheKey[n];
            for (int li = 0; li < n; li++) content[li] = ShaperLayerKey.ContentKey(doc, li, DocumentPart);
        }

        /// <summary>True when this memo was built for <paramref name="doc"/> at its current layer count — a
        /// necessary condition for reading it, never a sufficient one (the content may still have changed).</summary>
        public bool BuiltFor(ShaperDocument doc)
            => ReferenceEquals(built, doc) && content.Length == (doc?.layers?.Count ?? 0);

        public ShaperCacheKey Content(int layerIndex)
            => layerIndex >= 0 && layerIndex < content.Length ? content[layerIndex] : default;

        /// <summary>
        /// The key of layer <paramref name="layerIndex"/>'s painted buffer at <paramref name="phase01"/> — the
        /// memoized content key folded with the three things that DO vary by frame: the phase, the layer's base
        /// plane (<see cref="ShaperHeightCompiler.LayerBase"/>, which its height and light stages compile
        /// against), and whether an effect applier was supplied at all. A masked layer additionally folds in its
        /// source's content key and whether that source is alive at this frame.
        /// </summary>
        public ShaperCacheKey PaintKey(ShaperDocument doc, int layerIndex, float phase01, float baseZ,
                                       int frameIndex, bool effectsEnabled)
        {
            var m = ShaperCacheMixer.Begin("shaper.paint");
            m.MixKey(Content(layerIndex));
            m.MixFloat(phase01);
            m.MixFloat(baseZ);
            m.MixBool(effectsEnabled);

            var layer = ShaperLayerKey.LayerAt(doc, layerIndex);
            if (layer?.mask == null || !layer.mask.IsSet || doc == null) return m.Key;

            int si = doc.LayerIndexById(layer.mask.sourceLayerId);
            var src = si >= 0 && si != layerIndex ? ShaperLayerKey.LayerAt(doc, si) : null;
            if (src == null) { m.MixInt(-1); return m.Key; }
            int fc = Mathf.Max(1, doc.frameCount);
            m.MixInt(si);
            m.MixBool(src.enabled && src.root != null);
            // A mask SOURCE is read whether or not it contributes to the picture (that is the whole point of
            // contributesToPicture), so only its lifetime window gates it — the same test BuildMaskField applies.
            int srcEnd = src.endFrame < 0 ? fc - 1 : src.endFrame;
            m.MixBool(frameIndex >= src.startFrame && frameIndex <= srcEnd);
            m.MixFloat(ShaperHeightCompiler.LayerBase(doc, si, phase01, doc.seed));
            m.MixKey(Content(si));
            return m.Key;
        }

        /// <summary>
        /// The signature of one FRAME's finished pixels: the document part, the background, the document's
        /// post-composite effect list, and — in list order — the paint key of every layer that actually
        /// contributes at that frame. Two frames with equal signatures encode equal pixels.
        ///
        /// A layer toggle changes this (the picture really does change) while changing NO layer's paint key,
        /// which is the distinction the preview cache uses to re-composite in place instead of flashing the
        /// tick strip: see <c>ShaperPreviewFrameCache.Invalidate</c>.
        /// </summary>
        public ShaperCacheKey FrameSignature(ShaperDocument doc, int frameIndex, bool effectsEnabled)
        {
            var m = ShaperCacheMixer.Begin("shaper.frame");
            if (doc == null) return m.Key;
            m.MixKey(DocumentPart);
            m.MixBool(effectsEnabled);
            ShaperLayerKey.MixDocumentComposite(ref m, doc, effectsEnabled);

            if (doc.layers == null) return m.Key;
            float phase01 = doc.PhaseOfFrame(frameIndex);
            int fc = Mathf.Max(1, doc.frameCount);
            for (int li = 0; li < doc.layers.Count; li++)
            {
                if (!ShaperLayerKey.Contributes(doc.layers[li], frameIndex, fc)) continue;
                m.MixInt(li);
                float baseZ = ShaperHeightCompiler.LayerBase(doc, li, phase01, doc.seed);
                m.MixKey(PaintKey(doc, li, phase01, baseZ, frameIndex, effectsEnabled));
            }
            return m.Key;
        }
    }
}
