// PyreLayerKey — the content key of one layer's rendered buffers, for the per-layer preview cache
// (PyreWindow.FrameCache.cs, PYREPLUS_DESIGN.md "Per-layer preview cache").
//
// A layer's key is a 64-bit FNV-1a over everything its render READS:
//   • every Unity-serialized field of the layer (public, or [SerializeField]; not [NonSerialized], static,
//     readonly or const) — ZUIValues, fills, gradients, curves, the modifier stack, the sim slot, the plug-in form
//     (through PyreForm.ContentHash) — EXCEPT `enabled`, `name` and `shapeAdvanced`, which the renderer never reads;
//   • the layer's INDEX in the list: the renderer's layer salt is the index and every seeded draw folds it in, so a
//     layer that moves legitimately renders differently (and a layer that stays put while others are hidden does
//     not — hide/show never shifts an index);
//   • the spec's canvas size, frame count and seed;
//   • the spec's global GEOMETRY / PIXEL modifiers with their list positions — they wrap every layer's own stack and
//     their Eval ids depend on where they sit in the list. Global POST modifiers are not in the key: they run on the
//     composited frame, after the cache.
// Per frame, a buffer also carries a VARIANT: the background hash when the buffer is fused with the background
// (LayerPlan.fuseBackground), and for a heightmap consumer the keys of the active writers below it on its channel
// (it reads their accumulated coverage while rendering). Clip channels and the luma matte are NOT part of any key —
// they are applied from the matte layers' own cached buffers at composite time, so editing a matte layer re-renders
// that matte layer alone.
//
// Two keys are equal only when the bytes would be equal; the reverse is not promised (a cosmetic field that the
// renderer happens to ignore still changes the key — the cost is one needless re-render, never a stale buffer).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre
{
    public static class PyreLayerKey
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        const int MaxDepth = 12;

        static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();
        static readonly object _fieldsLock = new object();

        /// The part of every layer key that comes from the spec: canvas, frame count, seed, and the global
        /// geometry/pixel modifiers with their positions.
        public static ulong SpecPart(Pyre spec)
        {
            ulong h = Offset;
            if (spec == null) return h;
            Mix(ref h, spec.canvasSize); Mix(ref h, spec.frameCount); Mix(ref h, spec.seed);
            if (spec.globalModifiers != null)
                for (int i = 0; i < spec.globalModifiers.Count; i++)
                {
                    var m = spec.globalModifiers[i];
                    if (m == null || m is PostModifier) continue;
                    Mix(ref h, i);
                    MixValue(ref h, m, null, 0);
                }
            return h;
        }

        /// The background as a frame-variant: the flat clear, or the background fill.
        public static ulong BackgroundPart(Pyre spec)
        {
            ulong h = Offset;
            if (spec == null) return h;
            Mix(ref h, spec.backgroundUseFill ? 1 : 0);
            if (spec.backgroundUseFill) MixValue(ref h, spec.backgroundFill, null, 0);
            else MixValue(ref h, spec.background, null, 0);
            return h;
        }

        /// The key of layer `index` of `spec` — its content plus the spec part. Visibility and list order (beyond
        /// the index itself) are not in it.
        public static ulong LayerKey(Pyre spec, int index)
        {
            var layer = spec.layers[index];
            ulong h = SpecPart(spec);
            Mix(ref h, index);
            if (layer == null) return h;
            foreach (var f in SerializedFields(typeof(PyreLayer)))
            {
                if (f.Name == "enabled" || f.Name == "name" || f.Name == "shapeAdvanced") continue;
                MixString(ref h, f.Name);
                MixValue(ref h, f.GetValue(layer), f, 0);
            }
            return h;
        }

        /// The frame-variant of layer `li` in frame `frameIndex` under `plans`: 0 for the common case, otherwise
        /// the background (fused buffers) and/or the writer keys a heightmap consumer depends on.
        public static ulong FrameVariant(Pyre spec, PyreRenderer.LayerPlan[] plans, ulong[] layerKeys, int li)
        {
            ref var p = ref plans[li];
            if (!p.fuseBackground && !p.isHeightConsumer) return 0;
            ulong h = Offset;
            if (p.fuseBackground) Mix(ref h, BackgroundPart(spec));
            if (p.isHeightConsumer)
            {
                int channel = spec.layers[li].heightFromChannel;
                for (int w = 0; w < li; w++)
                {
                    if (!plans[w].active || !plans[w].isMatte) continue;
                    if (Mathf.Clamp(spec.layers[w].matteChannel, 0, 3) != channel) continue;
                    Mix(ref h, w); Mix(ref h, layerKeys[w]);
                }
            }
            return h == Offset ? 1 : h;
        }

        // ── the hash walk ────────────────────────────────────────────────────────────────────────────────────
        static void Mix(ref ulong h, int v) { unchecked { h = (h ^ (uint)v) * Prime; } }
        static void Mix(ref ulong h, ulong v) { unchecked { h = (h ^ v) * Prime; } }
        static void Mix(ref ulong h, float v) => Mix(ref h, BitConverter.SingleToInt32Bits(v));
        static void MixString(ref ulong h, string s)
        {
            if (s == null) { Mix(ref h, -1); return; }
            Mix(ref h, s.Length);
            for (int i = 0; i < s.Length; i++) Mix(ref h, s[i]);
        }

        // Unity's serialization rule, which is also the rule for "is this a dial or scratch": public or
        // [SerializeField], not [NonSerialized] / static / readonly / const. Cached per type.
        static FieldInfo[] SerializedFields(Type t)
        {
            lock (_fieldsLock)
            {
                if (_fields.TryGetValue(t, out var cached)) return cached;
                var list = new List<FieldInfo>();
                for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                    foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsStatic || f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                        if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), true) && !f.IsDefined(typeof(SerializeReference), true)) continue;
                        list.Add(f);
                    }
                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                cached = list.ToArray();
                _fields[t] = cached;
                return cached;
            }
        }

        // A null in a field Unity rewrites on serialization hashes as what Unity writes: an empty string, an empty
        // list, "no object" for a Unity reference (a deserialized null is a fake-null object, a fresh one a real
        // null — both hash 0), and for a plain [Serializable] class a DEFAULT INSTANCE (Unity cannot store a null
        // there; it constructs one). A [SerializeReference] null stays null through Unity's own serialization — what
        // fills those in is the OWNER's ISerializationCallbackReceiver (ZuiFill seeds its animatable companions on
        // every load), which is why MixValue runs that callback before walking an object: the key then reads the
        // state every load, undo snapshot and Instantiate produces, so a layer built in memory (a new or duplicated
        // layer, a test spec) keys exactly like the asset it becomes. The callbacks are idempotent by Unity's
        // contract (they run on every deserialization) and touch no Unity API. One residue remains: a Gradient /
        // AnimationCurve field that is null in memory (a Solid fill made by `new ZuiFill(color)`) is materialised by
        // Unity on the first serialization and the owner's callback then seeds from it — not reproducible here
        // without re-implementing the owner, so a brand-new layer re-keys ONCE at its first undo or reload (one
        // extra render of that layer). Assets loaded from disk are past that point from the start.
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
                if (!_defaults.TryGetValue(t, out var d)) { try { d = Activator.CreateInstance(t); } catch { d = null; } _defaults[t] = d; }
                return d;
            }
        }

        static void MixValue(ref ulong h, object v, FieldInfo field, int depth)
        {
            if (v == null) v = NullAsSerialized(field);
            if (v == null)
            {
                if (field != null && typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) { Mix(ref h, 0); return; }
                Mix(ref h, 0x7f); return;
            }
            if (depth > MaxDepth) { Mix(ref h, 0x7e); return; }
            switch (v)
            {
                case float f: Mix(ref h, f); return;
                case int i: Mix(ref h, i); return;
                case bool b: Mix(ref h, b ? 1 : 2); return;
                case Enum e: Mix(ref h, Convert.ToInt32(e)); return;
                case double d: Mix(ref h, (ulong)BitConverter.DoubleToInt64Bits(d)); return;
                case long l: Mix(ref h, (ulong)l); return;
                case byte by: Mix(ref h, by); return;
                case string s: MixString(ref h, s); return;
                case PyreForm form: Mix(ref h, form.ContentHash()); MixString(ref h, form.GetType().FullName); return;
                case Gradient g:
                    foreach (var k in g.colorKeys) { MixValue(ref h, k.color, null, depth + 1); Mix(ref h, k.time); }
                    foreach (var k in g.alphaKeys) { Mix(ref h, k.alpha); Mix(ref h, k.time); }
                    Mix(ref h, (int)g.mode);
                    return;
                case AnimationCurve ac:
                    foreach (var k in ac.keys) { Mix(ref h, k.time); Mix(ref h, k.value); Mix(ref h, k.inTangent); Mix(ref h, k.outTangent); Mix(ref h, k.inWeight); Mix(ref h, k.outWeight); Mix(ref h, (int)k.weightedMode); }
                    Mix(ref h, (int)ac.preWrapMode); Mix(ref h, (int)ac.postWrapMode);
                    return;
                case Color c: Mix(ref h, c.r); Mix(ref h, c.g); Mix(ref h, c.b); Mix(ref h, c.a); return;
                case Color32 c32: Mix(ref h, c32.r | (c32.g << 8) | (c32.b << 16) | (c32.a << 24)); return;
                case Vector2 v2: Mix(ref h, v2.x); Mix(ref h, v2.y); return;
                case Vector3 v3: Mix(ref h, v3.x); Mix(ref h, v3.y); Mix(ref h, v3.z); return;
                case Vector4 v4: Mix(ref h, v4.x); Mix(ref h, v4.y); Mix(ref h, v4.z); Mix(ref h, v4.w); return;
                case UnityEngine.Object uo: Mix(ref h, uo ? uo.GetInstanceID() : 0); return;
                case IList list:
                    Mix(ref h, list.Count);
                    foreach (var e in list) MixValue(ref h, e, null, depth + 1);
                    return;
            }
            var t = v.GetType();
            if (t.IsPrimitive) { Mix(ref h, v.GetHashCode()); return; }
            if (v is ISerializationCallbackReceiver r) r.OnAfterDeserialize();
            // A plain [Serializable] class/struct (ZUIValue, ZuiFill, a modifier, a nested settings object, a
            // point struct): its content, never its reference — a clone must hash equal, an edited field must not.
            MixString(ref h, t.FullName);
            foreach (var f in SerializedFields(t))
            {
                MixString(ref h, f.Name);
                MixValue(ref h, f.GetValue(v), f, depth + 1);
            }
        }
    }
}
