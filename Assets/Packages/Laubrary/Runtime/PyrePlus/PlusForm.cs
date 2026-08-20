// PlusForm — the plug-in shape model for PyrePlus layers.
//
// A layer's look is either one of the built-in ShapeForm enum cases (the legacy if-chain in the renderer) OR a
// PlusForm object held in `PyrePlusLayer.form` ([SerializeReference]). A form is self-contained: its dials are its
// own public fields ([Range]/[Tooltip] attributes drive the editor through ZuiReflect — a form needs zero editor
// code), it is discovered by assembly scan (so a family of forms can live in its own asmdef that the core never
// references), it evaluates its animatable ZUIValues through the renderer's single deterministic Eval funnel (via
// Prepare), and it paints one frame through Render. The renderer knows only this base: no per-form names anywhere
// in the core. This is the same pattern the modifier stack already trusts (PyreModifier + ZuiReflect + assembly
// scan + reflection Clone), applied to the shape itself.
//
// Determinism contract (PYREPLUS_DESIGN.md): Render must be a pure function of (ctx, this form's fields). Use
// ctx.seed / ctx.layerSalt / PyrePlusRenderer.Hash for every random draw — never UnityEngine.Random, never Time.
//
// Threading contract: frames of one clip render concurrently (PlusFrameFill), each worker thread on its OWN deep
// clone of the spec — so per-instance scratch (planes, buffers, Prepare'd dials) is fine and needs no locking, but
// a form must never keep mutable STATIC state (use [ThreadStatic] if a static scratch is really wanted) and must
// never touch a UnityEngine.Object (Texture2D, Sprite, TMP, AssetDatabase) from Render/Prepare. Shared pre-passes go
// through PlusPrepassCache, which is thread-safe and shared between a form and its render clones.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    /// How a form consumes the layer: WholeLayer paints the whole canvas once per frame (field/closed-form
    /// effects; the swarm hands it placement instances); PerParticle is stamped once per swarm particle (reserved —
    /// the renderer does not dispatch it yet); Stateful carries frame-to-frame state behind the replay harness
    /// (reserved). Phase 1 fully supports WholeLayer; the other two are declared so a form can state its nature now.
    public enum PlusFormKind { WholeLayer, PerParticle, Stateful }

    /// Catalog metadata for the editor's form picker. `group` clusters forms under one heading; `icon` is a ZUI
    /// icon name (null = text only). A concrete PlusForm without this attribute is still discovered — it lands in
    /// the default "Forms" group under its DisplayName.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PlusFormInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; }
        public string Icon { get; }
        public PlusFormInfoAttribute(string displayName, string group = "Forms", string icon = null)
        {
            DisplayName = displayName;
            Group = group;
            Icon = icon;
        }
    }

    /// Marks a form field that only does anything when the layer's Swarm is ON (a per-blast variation dial, say).
    /// The editor hides it while the swarm is off, so the card never shows a dead control.
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PlusSwarmOnlyAttribute : Attribute { }

    /// One swarm particle as the renderer computed it for this frame — the full hand-off a whole-layer form gets
    /// when the layer's swarm is on (ctx.swarm is null when it is off ⇒ the form places itself at the centre).
    /// Every instance is included, alive or not: `own` < 0 means not born yet, > 1 means already dead — a form that
    /// stamps per instance skips those; a form that spawns its own event per instance uses spawnLife instead.
    public struct PlusSwarmInstance
    {
        public float x, y;          // canvas-pixel position, live whole-cloud spin + scale already applied (origin = buffer (0,0), y-up)
        public float own;           // the particle's own life 0..1 (die-together honoured)
        public float spawnLife;     // its spawn moment on the layer's timeline, 0..1
        public int index;           // its swarm index (the particleIndex the per-particle Eval path would use)
        public float orientDeg;     // per-particle facing in degrees (math angle, CCW from +x); 0 = none
        public float zNorm;         // pseudo-3D depth after the shape tilt, -1..1 (+1 nearest)
        public float sizeMul;       // depth size shading the particle forms apply (1 when untilted)
        public float brightMul;     // depth brightness shading the particle forms apply (1 when untilted)
    }

    /// Handed to Prepare once per frame. `Eval(value, slot)` resolves an animatable dial at the layer's life through
    /// the renderer's deterministic funnel; `slot` (0, 1, 2 …) keeps each dial's Min-Max stream independent and maps
    /// to field id `PyrePlusRenderer.FldForm - slot`. `EvalRaw(value, fieldId)` takes an explicit field id — for a
    /// form migrated from a legacy enum case that must keep its old ids so already-authored Min-Max dials keep
    /// drawing the same random; a new form has no reason to use it.
    public readonly struct PlusFormPrepareCtx
    {
        public readonly float life;        // the layer's life this frame, 0..1
        public readonly int seed;          // spec seed
        public readonly int layerSalt;     // the layer's index — the renderer's per-layer decorrelation salt
        readonly Func<ZUIValue, int, float> evalRaw;

        public PlusFormPrepareCtx(float life, int seed, int layerSalt, Func<ZUIValue, int, float> evalRaw)
        {
            this.life = life; this.seed = seed; this.layerSalt = layerSalt; this.evalRaw = evalRaw;
        }

        public float Eval(ZUIValue v, int slot) => evalRaw(v, PyrePlusRenderer.FldForm - slot);
        public float EvalRaw(ZUIValue v, int fieldId) => evalRaw(v, fieldId);
    }

    /// Everything Render needs for one frame. Built by the renderer; a form reads, never writes. Designed to grow
    /// (aux-map publishing, per-particle stamping) by adding members — existing forms keep compiling.
    public readonly struct PlusFormCtx
    {
        public readonly int W, H;
        public readonly float life;                  // the layer's life this frame, 0..1
        public readonly int seed;                    // spec seed
        public readonly int layerSalt;               // the layer's index (fold into every seeded draw)
        public readonly ZuiFill fill;                // the layer's Shape Fill — the form's colour source
        public readonly float alpha;                 // the layer's Alpha envelope at `life`, 0..1 (one overall multiplier)
        public readonly PlusSwarmInstance[] swarm;   // null when the swarm is off
        public readonly GeometryModifier[] geo;      // the layer's enabled geometry modifiers, already Prepare'd (null = none)
        public readonly PixelModifier[] pix;         // the layer's enabled pixel modifiers, already Prepare'd (null = none)
        public readonly float phase;                 // the renderer's per-frame wobble phase (modifier convention)
        public readonly int frameIndex;
        public readonly int frameCount;              // the clip's frame count (life = frameIndex/(frameCount-1))
        readonly Func<ZUIValue, int, float, float> evalAtLife;   // (value, fieldId, life) through the renderer's funnel

        public PlusFormCtx(int w, int h, float life, int seed, int layerSalt, ZuiFill fill, float alpha,
                           PlusSwarmInstance[] swarm, GeometryModifier[] geo, PixelModifier[] pix,
                           float phase, int frameIndex, int frameCount = 1, Func<ZUIValue, int, float, float> evalAtLife = null)
        {
            W = w; H = h; this.life = life; this.seed = seed; this.layerSalt = layerSalt; this.fill = fill;
            this.alpha = alpha; this.swarm = swarm; this.geo = geo; this.pix = pix; this.phase = phase;
            this.frameIndex = frameIndex; this.frameCount = Math.Max(1, frameCount); this.evalAtLife = evalAtLife;
        }

        /// The layer life of frame `i` — the same mapping the renderer uses for `life`.
        public float LifeOfFrame(int i) => frameCount > 1 ? i / (float)(frameCount - 1) : 0f;

        /// A Prepare context for an ARBITRARY life — what a clip-level pass (PlusClipStats) needs to re-resolve the
        /// form's dials at other frames. Call `form.Prepare(ctx.PrepareCtxAt(ctx.life))` afterwards to restore this
        /// frame's values (Prepare stores them on the instance). Null evalAtLife (a hand-built ctx) falls back to
        /// the dial's static value.
        public PlusFormPrepareCtx PrepareCtxAt(float atLife)
        {
            var f = evalAtLife;
            int sd = seed, salt = layerSalt;
            Func<ZUIValue, int, float> eval = f != null
                ? (v, fid) => f(v, fid, atLife)
                : (v, fid) => v != null ? v.staticValue : 0f;
            return new PlusFormPrepareCtx(atLife, sd, salt, eval);
        }

        /// A copy of this ctx at another canvas size (swarm positions scaled along) — PlusSupersample builds the
        /// k× ctx with it.
        public PlusFormCtx WithSize(int w, int h)
        {
            PlusSwarmInstance[] sw = null;
            if (swarm != null)
            {
                float kx = w / (float)Math.Max(1, W), ky = h / (float)Math.Max(1, H);
                sw = new PlusSwarmInstance[swarm.Length];
                for (int i = 0; i < sw.Length; i++) { sw[i] = swarm[i]; sw[i].x *= kx; sw[i].y *= ky; }
            }
            return new PlusFormCtx(w, h, life, seed, layerSalt, fill, alpha, sw, geo, pix, phase, frameIndex, frameCount, evalAtLife);
        }
    }

    /// Optional: a form that can hand its pre-shade float planes to a debug sink (the parity harness dumps them as
    /// `fields/NNNN_<name>.npy`). Called right after Render, and ONLY while a sink is installed — the normal render
    /// path never calls it. Use the contract's names: "H" (density/heat), "T" (secondary, e.g. soot), "ramp_t".
    public interface IPlusFieldPublisher
    {
        void PublishFields(Action<string, float[]> sink);
    }

    /// Optional: a form whose colour does not come straight from the layer Fill answers the harness's ramp probe
    /// itself. `t` is in the CONTRACT's convention: 0 = cold outer edge, 1 = hottest core. Return sRGB + alpha.
    public interface IPlusRampProbe
    {
        Color ProbeRamp(float t);
    }

    /// Debug hooks the renderer consults. `FieldSink` is null in normal operation; the parity harness installs one
    /// for the duration of a dump. Keeping it a static is what lets RenderFormLayer stay byte-identical when unused:
    /// one null check after Render, nothing else.
    public static class PlusFormDebug
    {
        public static Action<string, float[]> FieldSink;
    }

    [Serializable]
    public abstract class PlusForm
    {
        /// Label in the picker and on the form's card. Usually just returns the [PlusFormInfo] display name.
        public abstract string DisplayName { get; }

        /// What the form's card tooltip says: what it draws and how it reads the Fill / Alpha / Swarm.
        public virtual string Description => DisplayName + " form.";

        public virtual PlusFormKind Kind => PlusFormKind.WholeLayer;

        /// Whether the layer's Shape Fill is this form's colour source. A form that carries its own ramps (a port
        /// whose palette is part of the algorithm) returns false and the editor hides the Fill row instead of
        /// showing a control that does nothing — the same rule that hides the Colour row for Text.
        public virtual bool UsesFill => true;

        /// Forms get the layer's GEOMETRY modifiers for free: after Render, the renderer inverse-warps the finished
        /// buffer (and any published parity planes) through ctx.geo — see PlusFormWarp for the exact conventions.
        /// Override to true ONLY if you apply ctx.geo per sample yourself (Inferno does, so its containment follows
        /// the warp); a form that returns true and ignores ctx.geo simply gets no geometry modifiers.
        public virtual bool HandlesGeometry => false;

        /// Resolve this frame's animatable dials to plain floats (store them on the instance for Render). Called
        /// once per frame before Render. Never concurrently on the SAME instance — a parallel fill gives every
        /// worker thread its own clone, so instance fields are safe to use as scratch.
        public virtual void Prepare(in PlusFormPrepareCtx ctx) { }

        // The form whose PlusPrepassCache entries this instance shares — null for an authored form (itself). A render
        // clone made for a worker thread is linked to the form it was cloned from, so the fit / clip-stats pre-pass
        // the first frame computed is reused by every worker instead of being solved once per clone. Deliberately
        // NOT set by Clone(): a duplicated layer is a new authored form with its own layerSalt and its own entry.
        [NonSerialized] PlusForm _prepassOrigin;

        /// The identity PlusPrepassCache keys by: the origin form for a render clone, this form otherwise.
        public PlusForm PrepassIdentity => _prepassOrigin ?? this;

        /// Link this instance (a render clone) to the form whose pre-pass results it should share. Follows the
        /// origin's own link, so a clone of a clone still lands on the authored form.
        public void SharePrepassWith(PlusForm origin) => _prepassOrigin = origin != null && origin != this ? origin.PrepassIdentity : null;

        /// Paint one frame into `target` (W*H Color32, row 0 = bottom). The renderer always hands a whole-layer form
        /// an isolated transparent scratch, so a form may SET pixels over its whole silhouette; compositing, clip,
        /// matte and post modifiers happen afterwards in the renderer.
        public abstract void Render(in PlusFormCtx ctx, Color32[] target);

        /// Deep copy for the layer list's Duplicate. MemberwiseClone copies value fields; every reference field a copy
        /// must own independently (ZUIValue, ZuiFill, Gradient, AnimationCurve, lists) is cloned by reflection so a
        /// new form never has to write this by hand.
        public virtual PlusForm Clone()
        {
            var c = (PlusForm)MemberwiseClone();
            foreach (var f in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.IsNotSerialized) continue;
                object v = f.GetValue(this);
                if (v == null) continue;
                object copy = DeepCopyValue(v);
                if (!ReferenceEquals(copy, v)) f.SetValue(c, copy);
            }
            return c;
        }

        static object DeepCopyValue(object v)
        {
            switch (v)
            {
                case ZUIValue zv: return Sfx.CloneVal(zv);
                case ZuiFill fill: return PyrePlusLayer.CloneFill(fill);
                case Gradient g: return Sfx.CloneGradient(g);
                case AnimationCurve ac: return new AnimationCurve(ac.keys) { preWrapMode = ac.preWrapMode, postWrapMode = ac.postWrapMode };
                case List<ZUIEnvelopePoint> pts:
                    return pts.ConvertAll(p => new ZUIEnvelopePoint(p.time, p.value, p.exponent, p.editState));
                case PlusForm nested: return nested.Clone();
                case string _: return v;
                case IList list when v.GetType().IsGenericType:
                {
                    // A List<T>: value-type / string elements copy as-is, reference elements deep-copy recursively.
                    var copy = (IList)Activator.CreateInstance(v.GetType());
                    foreach (var e in list) copy.Add(e == null ? null : DeepCopyValue(e));
                    return copy;
                }
                default:
                    // A plain [Serializable] class the form owns (a PlusRamp, a stop, any future nested settings
                    // object) is copied field by field so a duplicate never aliases it; everything else (value
                    // types, UnityEngine.Object refs — assets are shared, not per-layer data) is returned as-is.
                    if (IsPlainSerializableClass(v.GetType()))
                    {
                        var copy = Activator.CreateInstance(v.GetType());
                        foreach (var f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (f.IsNotSerialized) continue;
                            object fv = f.GetValue(v);
                            f.SetValue(copy, fv == null ? null : DeepCopyValue(fv));
                        }
                        return copy;
                    }
                    return v;
            }
        }

        /// A non-Unity reference type marked [Serializable] with a parameterless constructor — the shape of every
        /// nested settings object a form may own.
        public static bool IsPlainSerializableClass(Type t) =>
            t.IsClass && t != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(t)
            && !typeof(Delegate).IsAssignableFrom(t) && t.IsSerializable && !t.IsArray
            && t.GetConstructor(Type.EmptyTypes) != null;

        /// A stable hash of every serialized dial, for stateful forms' replay-harness invalidation (any authoring
        /// edit changes it). Reflects over the same fields Clone copies, so it cannot go stale when a field is added.
        public virtual int ContentHash()
        {
            unchecked
            {
                int h = (int)2166136261u;
                h = Mix(h, GetType().FullName.GetHashCode());
                foreach (var f in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.IsNotSerialized) continue;
                    h = Mix(h, f.Name.GetHashCode());
                    h = MixValue(h, f.GetValue(this));
                }
                return h;
            }
        }

        static int Mix(int h, int v) { unchecked { return (h ^ v) * 16777619; } }
        static int MixF(int h, float v) => Mix(h, v.GetHashCode());

        static int MixValue(int h, object v)
        {
            unchecked
            {
                switch (v)
                {
                    case null: return Mix(h, 0);
                    case ZUIValue zv:
                        h = Mix(h, (int)zv.mode); h = MixF(h, zv.staticValue); h = MixF(h, zv.min); h = MixF(h, zv.max);
                        h = MixF(h, zv.yMin); h = MixF(h, zv.yMax);
                        if (zv.points != null) foreach (var p in zv.points) { h = MixF(h, p.time); h = MixF(h, p.value); h = MixF(h, p.exponent); }
                        if (zv.steps != null) foreach (var s in zv.steps) h = MixF(h, s);
                        return h;
                    case ZuiFill fill:
                        h = Mix(h, (int)fill.mode); h = Mix(h, fill.color.GetHashCode()); h = MixF(h, fill.angleDeg); h = MixF(h, fill.zoom);
                        h = Mix(h, fill.center.GetHashCode()); h = Mix(h, (int)fill.space); h = Mix(h, (int)fill.texture);
                        h = MixValue(h, fill.gradient); h = MixValue(h, fill.zoomAnim); h = MixValue(h, fill.centerXAnim); h = MixValue(h, fill.centerYAnim);
                        return h;
                    case Gradient g:
                        foreach (var k in g.colorKeys) { h = Mix(h, k.color.GetHashCode()); h = MixF(h, k.time); }
                        foreach (var k in g.alphaKeys) { h = MixF(h, k.alpha); h = MixF(h, k.time); }
                        return Mix(h, (int)g.mode);
                    case AnimationCurve ac:
                        foreach (var k in ac.keys) { h = MixF(h, k.time); h = MixF(h, k.value); h = MixF(h, k.inTangent); h = MixF(h, k.outTangent); }
                        return h;
                    case PlusForm nested: return Mix(h, nested.ContentHash());
                    case UnityEngine.Object uo: return Mix(h, uo ? uo.GetInstanceID() : 0);
                    case string s: return Mix(h, s.GetHashCode());
                    case IList list: foreach (var e in list) h = MixValue(h, e); return h;
                    case float f: return MixF(h, f);
                    case double d: return Mix(h, d.GetHashCode());
                    case bool b: return Mix(h, b ? 1 : 0);
                    case Enum e: return Mix(h, Convert.ToInt32(e));
                    case int i: return Mix(h, i);
                    case Color c: h = MixF(h, c.r); h = MixF(h, c.g); h = MixF(h, c.b); return MixF(h, c.a);
                    default:
                        if (IsPlainSerializableClass(v.GetType()))
                        {
                            // Nested settings object: hash its content, never its reference (a clone must hash equal).
                            foreach (var f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (f.IsNotSerialized) continue;
                                h = Mix(h, f.Name.GetHashCode());
                                h = MixValue(h, f.GetValue(v));
                            }
                            return h;
                        }
                        return Mix(h, v.GetHashCode());
                }
            }
        }
    }
}
