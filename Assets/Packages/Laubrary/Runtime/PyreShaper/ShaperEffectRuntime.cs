// ShaperEffectRuntime — the bridge half of T-0163: what an authored Shaper effect actually IS, and whether it
// can run where the author put it.
//
// It lives here, in Laubrary.PyreShaper, for the same reason ShaperEffectApplier does: this is the only
// assembly that can see BOTH the authored document (Laubrary.Shaper) and the concrete effect classes
// (Laubrary.SpriteFx) plus the catalog that classifies them. Runtime/Shaper declares the abstract
// ShaperEffectInstance and nothing else about effects; the one concrete subclass is here, and Unity's managed
// references carry it across the line without Shaper ever naming SpriteFx.
using System;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Shaper;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0163 — one authored effect's real settings: a live <see cref="PyreModifier"/>, serialized with the
    /// document, so its dials are the author's and not the class defaults every render used to reconstruct.
    ///
    /// A wrapper rather than putting the modifier straight on <see cref="ShaperEffectRef"/> because
    /// <c>ShaperEffectRef</c> lives in <c>Laubrary.Shaper</c>, which must not name a SpriteFx type. The nesting
    /// costs one managed reference per effect and buys the assembly boundary the applier seam already defends.
    /// </summary>
    [Serializable]
    public sealed class ShaperModifierEffect : ShaperEffectInstance
    {
        /// <summary>The effect itself. <c>[SerializeReference]</c> because the concrete class is what carries
        /// the dials, and a document must round-trip the subclass the author chose, not its base.</summary>
        [SerializeReference] public PyreModifier modifier;

        public ShaperModifierEffect() { }
        public ShaperModifierEffect(PyreModifier modifier) { this.modifier = modifier; }

        /// <summary>
        /// The settings object a reflection drawer should draw, typed as <c>object</c> on purpose: the Shaper
        /// editor assembly does not reference <c>Laubrary.SpriteFx</c> and should not have to start, since all
        /// it does with the modifier is hand it to <c>ZuiReflect</c>, which takes an <c>object</c> anyway. The
        /// concretely-typed <see cref="modifier"/> field stays public for anything that legitimately can see
        /// the type.
        /// </summary>
        public object Settings => modifier;

        /// <inheritdoc/>
        public override string TypeName => modifier != null ? modifier.GetType().Name : null;

        /// <inheritdoc/>
        public override string DisplayName
        {
            get
            {
                if (modifier == null) return "(missing effect)";
                // DisplayName is abstract on PyreModifier, so a subclass always has one; the guard is for a
                // subclass that throws building it, which must not take a whole window's rebuild down.
                try { return modifier.DisplayName; }
                catch { return modifier.GetType().Name; }
            }
        }
    }

    /// <summary>
    /// The classification, construction and availability rules a Shaper effect list needs — the one place that
    /// answers "can this entry run HERE, and if not, why not".
    ///
    /// <b>Availability is three gates, not one, and each one is a real property of the shipped code rather than
    /// a policy invented here.</b>
    /// <list type="number">
    /// <item>The catalog's own <see cref="ShaperEffectCatalog.IsAvailable"/>, asked with the sheets the host
    /// actually publishes. A Shaper effect list runs over a FOLDED <see cref="Color32"/> picture, which
    /// publishes no per-pixel sheets at all — so the 13 sheet-needing effects are unavailable with the
    /// catalog's own wording, and Edge Warp is unavailable as Stuck.</item>
    /// <item>The stage the list runs at. An entry whose catalog row says <c>bothStagesPossible == false</c>
    /// runs only at its <c>defaultStage</c>.</item>
    /// <item>What <see cref="SpriteFxStack.RunStack"/> — the one kernel this bridge runs everything through —
    /// actually dispatches. It handles Geometry, Pixel and Post; a <see cref="SimulationModifier"/> keeps
    /// frame-to-frame state and is advanced through an <c>internal</c> hook only the Pyre assembly may call, so
    /// Shaper has no way to drive one and says so instead of adding a row that silently does nothing.</item>
    /// </list>
    /// </summary>
    public static class ShaperEffectRuntime
    {
        /// <summary>What a folded Shaper picture publishes to an effect: nothing. It is a rectangle of RGBA
        /// pixels with no coverage or edge-distance sheet attached, which is precisely the gate the catalog's
        /// sheet-needing bucket exists to express.</summary>
        public const ShaperQuantitySet PublishedSheets = default;

        static Dictionary<string, Type> _byName;

        /// <summary>
        /// Resolve a catalog <c>typeName</c> to its concrete <see cref="PyreModifier"/> type.
        ///
        /// Built by scanning the assembly <see cref="PyreModifier"/> lives in, rather than by
        /// <c>Type.GetType</c> on an assembly-qualified string: the catalog stores SHORT names, which keeps a
        /// document free of assembly identity — it must not stop resolving because an assembly was renamed or a
        /// type moved namespace.
        /// </summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (_byName == null)
            {
                _byName = new Dictionary<string, Type>(StringComparer.Ordinal);
                foreach (var t in typeof(PyreModifier).Assembly.GetTypes())
                {
                    if (t.IsAbstract || !typeof(PyreModifier).IsAssignableFrom(t)) continue;
                    _byName[t.Name] = t;
                }
            }
            return _byName.TryGetValue(typeName, out var found) ? found : null;
        }

        /// <summary>A fresh, default-constructed effect instance for a catalog type name, or null when the type
        /// is gone. The caller owns it: it is the authored object from here on, not a scratch copy.</summary>
        public static ShaperModifierEffect Create(string typeName)
        {
            var t = Resolve(typeName);
            if (t == null || t.GetConstructor(Type.EmptyTypes) == null) return null;
            return new ShaperModifierEffect((PyreModifier)Activator.CreateInstance(t));
        }

        /// <summary>The catalog row for a type name, or false when the catalog does not list it.</summary>
        public static bool TryEntry(string typeName, out ShaperEffectCatalogEntry entry)
        {
            var all = ShaperEffectCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (!string.Equals(all[i].typeName, typeName, StringComparison.Ordinal)) continue;
                entry = all[i];
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Whether an effect can run at <paramref name="stage"/> over a folded Shaper picture, and the reason
        /// when it cannot — the text the UI greys the row with, and the test the applier skips on. See the class
        /// doc for the three gates.
        /// </summary>
        public static bool CanRun(string typeName, ShaperEffectStage stage, out string reason)
        {
            if (!TryEntry(typeName, out var e))
            {
                reason = "Not in the effect catalog — its type may have been removed or renamed.";
                return false;
            }
            return CanRun(in e, stage, out reason);
        }

        /// <inheritdoc cref="CanRun(string, ShaperEffectStage, out string)"/>
        public static bool CanRun(in ShaperEffectCatalogEntry e, ShaperEffectStage stage, out string reason)
        {
            if (!ShaperEffectCatalog.IsAvailable(in e, PublishedSheets, out reason)) return false;

            if (string.Equals(e.stageKind, "Simulation", StringComparison.Ordinal))
            {
                reason = "Stateful simulation — it must be stepped frame by frame through a hook only the Pyre "
                       + "renderer can reach, so Shaper cannot drive it.";
                return false;
            }

            if (e.defaultStage != stage && !e.bothStagesPossible)
            {
                reason = "Only runs " + (e.defaultStage == ShaperEffectStage.PreComposite
                                            ? "on a layer, before compositing."
                                            : "on the finished picture, after compositing.");
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Fold an authored effect list into a cache key — the piece <see cref="ShaperEffectCacheKey"/>'s own
        /// doc names as "a caller-supplied effect state hash" and deliberately did not derive, because
        /// <see cref="PyreModifier"/> has no shared contract for enumerating the fields that change its output.
        ///
        /// Now that a list holds real INSTANCES, reflection over their serialized fields is the honest answer
        /// rather than the fragile one: the same fields Unity persists are the ones a render reads, so a hash
        /// over them changes exactly when the picture does. Field VALUES are folded through
        /// <c>ToString</c> — coarse, but it is a key, and a key that is too sensitive costs a cache miss while
        /// one that is too coarse shows a stale frame.
        /// </summary>
        public static ShaperCacheKey StateHash(IReadOnlyList<ShaperEffectRef> effects, ShaperEffectStage stage)
        {
            var m = ShaperCacheMixer.Begin("shaper.effect.list.v1");
            m.MixInt((int)stage);
            if (effects == null) return m.Key;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || !e.enabled) continue;
                m.MixString(e.typeName);
                var mod = (e.instance as ShaperModifierEffect)?.modifier;
                if (mod == null) continue;
                foreach (var f in mod.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    m.MixString(f.Name);
                    MixFieldValue(ref m, f.GetValue(mod));
                }
            }
            return m.Key;
        }

        /// <summary>
        /// Fold one authored field value. <see cref="ZUIValue"/> and <see cref="Gradient"/> get explicit
        /// treatment because neither overrides <c>ToString</c> — hashing them as objects would print a type
        /// name, so every curve edit and every gradient edit would produce the SAME key and a cache would show
        /// a stale picture. That is precisely the failure a coarse key causes, so the two types that would hit
        /// it are folded by their real content instead.
        /// </summary>
        static void MixFieldValue(ref ShaperCacheMixer m, object v)
        {
            switch (v)
            {
                case null:
                    m.MixString("\0");
                    return;

                case ZUIValue zv:
                    // Every authored member of the value, not just the ones the current mode reads: switching
                    // mode must change the key, and so must editing a curve that is momentarily switched away
                    // from. Oscillation's three envelopes are included for the same reason.
                    m.MixInt((int)zv.mode);
                    m.MixFloat(zv.staticValue); m.MixFloat(zv.min); m.MixFloat(zv.max);
                    m.MixFloat(zv.yMin); m.MixFloat(zv.yMax); m.MixFloat(zv.smoothness);
                    m.MixFloat(zv.oscRateMax);
                    m.MixString(zv.multiplierId);
                    MixPoints(ref m, zv.points);
                    MixPoints(ref m, zv.oscMin);
                    MixPoints(ref m, zv.oscMax);
                    MixPoints(ref m, zv.oscRate);
                    if (zv.steps != null) foreach (var s in zv.steps) m.MixFloat(s);
                    return;

                case Gradient g:
                    foreach (var k in g.colorKeys)
                    { m.MixFloat(k.time); m.MixFloat(k.color.r); m.MixFloat(k.color.g); m.MixFloat(k.color.b); }
                    foreach (var k in g.alphaKeys) { m.MixFloat(k.time); m.MixFloat(k.alpha); }
                    return;

                case Color c:
                    m.MixFloat(c.r); m.MixFloat(c.g); m.MixFloat(c.b); m.MixFloat(c.a);
                    return;

                default:
                    m.MixString(v.ToString());
                    return;
            }
        }

        static void MixPoints(ref ShaperCacheMixer m, List<ZUIEnvelopePoint> pts)
        {
            if (pts == null) { m.MixInt(-1); return; }
            m.MixInt(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            { m.MixFloat(pts[i].time); m.MixFloat(pts[i].value); m.MixFloat(pts[i].exponent); }
        }
    }
}
