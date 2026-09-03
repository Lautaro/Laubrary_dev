// ZuiGradient.cs
// A colour ramp with UNLIMITED stops, plus the things a raw ramp lacks: NON-DESTRUCTIVE transform knobs applied
// at sample time (reverse, hue shift, saturation, brightness, contrast), a QUANTISE (fewer discrete bands), and a
// PHASE (the hook for colour cycling). Every knob is a dial you can turn back — the stops are never rewritten.
// ToLut() bakes the result to a 1-D texture for the palette-cycle shader.
//
// STORAGE IS A STOP LIST, NOT A UnityEngine.Gradient (T-0221). It used to be backed by a `Gradient`, whose 8-key
// cap was the ONLY reason a Pyre ramp could not be edited with this control (PyreRampPresets.Ember() ships 10
// stops) — so a 10-colour ramp had to be authored stop-by-stop in a second, different control. The stop list has
// no cap and carries its own interpolation SPACE (linear-light or sRGB), which is what a PyreRamp needed too, so
// one control now edits both and the library round-trips either exactly.
//
// The legacy `Gradient` stays as the frozen serialized SOURCE and the migration input — the same idiom the
// animatable colour transforms below already use. An asset authored before this loads with an EMPTY stop list and
// therefore renders through that legacy Gradient, byte-for-byte as it always did; EnsureStops() (run by the editor,
// or on demand) converts it into stops at the union of its colour and alpha key positions, where linear
// interpolation reproduces a Blend gradient exactly. `gradient` survives as a PROPERTY over that conversion, so
// every existing caller that reads or assigns one keeps working — a Gradient is now an import/export FORMAT at the
// boundary (subsampled to 8 keys on the way out, never in storage), not the storage itself.
//
// The four COLOUR transforms (hue / saturation / brightness / contrast) are ANIMATABLE (ZUIValue / MultiCont):
// each can be a Static constant, a Min-Max spread, or a Curve over the 0..1 life clock — so e.g. brightness can
// pulse or hue can drift over a particle's life. Position transforms (reverse / quantise / phase) stay plain
// fields — they shape the sample POSITION, and an animated band count / reverse reads as flicker, not motion.
//
// Runtime-safe, deterministic, global-namespace [Serializable] — the same idiom as ZuiFill / ZUIValue. It
// implements IZuiRamp so the ZUI toolkit's stop editor (ZuiRampControl) draws it with no extra adapter.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// One ramp stop: a position along the ramp and its colour — the alpha channel IS the opacity there, not a
/// separate key (the same contract IZuiRamp and Pyre's PyreRampStop state).
[Serializable]
public class ZuiGradientStop
{
    [Range(0f, 1f), Tooltip("Where on the ramp this stop sits, 0 (start) to 1 (end).")]
    public float pos;
    [Tooltip("The colour at this stop. Its alpha is the ramp's opacity there.")]
    public Color color = Color.white;

    public ZuiGradientStop() { }
    public ZuiGradientStop(float pos, Color color) { this.pos = pos; this.color = color; }
}

/// How two neighbouring stops are mixed. The ORDER matches Pyre's own PyreRampSpace so a ramp and a gradient can
/// exchange the choice as one index (ZuiRampGradientBridge relies on that).
public enum ZuiGradientSpace
{
    /// Decode sRGB before blending and re-encode after — mid-tones stay bright instead of going brown.
    LinearLight = 0,
    /// Blend the stored values directly, the way UnityEngine.Gradient does. The DEFAULT, so every gradient
    /// authored before the stop list existed keeps rendering exactly as it did.
    Srgb = 1,
}

[Serializable]
public class ZuiGradient : ISerializationCallbackReceiver, IZuiRamp
{
    // The FROZEN migration source. Private since T-0221 — the `gradient` property below is the compatibility
    // surface, and a caller that mutated the returned Gradient in place would be editing a temporary.
    [SerializeField, FormerlySerializedAs("gradient")]
    [Tooltip("Legacy storage: the Gradient this ramp was authored as before it owned its own stops. Frozen — the "
           + "stops below are what renders once they exist.")]
    Gradient legacyGradient = DefaultGradient();

    [Tooltip("The colour stops, start (0) to end (1). No upper limit — this is what replaced the 8-key cap.")]
    public List<ZuiGradientStop> stops = new List<ZuiGradientStop>();

    [Tooltip("How neighbouring stops are mixed: Linear Light decodes sRGB before blending (mid-tones stay bright), "
           + "sRGB blends the stored values directly the way Unity's Gradient does.")]
    public ZuiGradientSpace space = ZuiGradientSpace.Srgb;

    // False on an asset written before the stop list existed: the stops are empty and the legacy Gradient still
    // renders it, byte-identically. EnsureStops() flips it once the conversion has run, so a deliberately EMPTY
    // ramp (a legal "no colour here" state) is never re-seeded from the legacy source on every load.
    [SerializeField] bool stopsAuthored;

    // ── position transforms (shape the sample POSITION; plain fields — not animated) ──
    [Tooltip("Sample the gradient backwards (1-t).")]
    public bool reverse = false;
    [Min(0), Tooltip("Snap the sample position to N discrete bands (0 = smooth). The gradient equivalent of Posterize.")]
    public int quantiseSteps = 0;
    // Set from CODE (a form's field initializer via LockToBands), never by the author directly: this instance is a
    // cel/band palette, not a free smooth ramp, so quantising is mandatory. Evaluate/ToLut floor the EFFECTIVE step
    // count at 1 regardless of quantiseSteps' stored value (defensive — covers an old asset, a paste, a reset), and
    // the editor (ZuiGradientEditor) floors the Quantise slider's own range at 1 and relabels it "Bands" so there is
    // no reachable "0 = smooth" state to confuse a banded palette with. Everything else about the gradient (the base
    // ramp, hue/sat/brightness/contrast/phase/cycle/reverse) stays exactly as versatile as an unlocked one.
    [Tooltip("Internal: true for a gradient a form declared as an always-banded palette (set via LockToBands, not authored directly).")]
    public bool bandLocked = false;

    /// Mark this gradient as an always-banded palette: quantising can never go smooth again, in code or in the
    /// editor. Idempotent — safe to call every time a form builds its default (a fresh instance) or migrates an old
    /// one. Only raises quantiseSteps to `defaultCount` when it isn't already a valid band count, so re-calling it
    /// (e.g. on deserialize) never disturbs an author's chosen count.
    public void LockToBands(int defaultCount = 7)
    {
        bandLocked = true;
        if (quantiseSteps <= 0) quantiseSteps = Mathf.Clamp(defaultCount, 1, 16);
    }

    // ── colour transforms — LEGACY floats: the frozen serialized SOURCE + the fallback for a null companion ──
    [Range(-1f, 1f), Tooltip("Rotate the hue of the whole ramp. ±1 = ±180°.")]
    public float hueShift = 0f;
    [Range(0f, 2f), Tooltip("Multiply saturation across the whole ramp (1 = unchanged).")]
    public float saturation = 1f;
    [Range(0f, 2f), Tooltip("Multiply brightness (value) across the whole ramp (1 = unchanged).")]
    public float brightness = 1f;
    [Range(0f, 2f), Tooltip("Contrast around mid-grey (1 = unchanged).")]
    public float contrast = 1f;

    // ── animatable colour-transform companions (ZUIValue / MultiCont) — the values Evaluate actually reads ──
    // [SerializeReference] so NULL is a real "not yet migrated" sentinel Unity preserves; an old asset loads them
    // null and OnAfterDeserialize seeds each Static(legacy) ⇒ byte-identical. EvalTransform falls back to the legacy
    // float when a companion is still null, so a pristine in-memory gradient renders correctly before seeding runs.
    [SerializeReference] public ZUIValue hueShiftAnim;
    [SerializeReference] public ZUIValue saturationAnim;
    [SerializeReference] public ZUIValue brightnessAnim;
    [SerializeReference] public ZUIValue contrastAnim;
    // PHASE — a manual SCROLL of the ramp (0..1), animatable over life: the basic "move the gradient" control that
    // works with no shader. 0 = no offset (byte-identical default). A rising Curve over life scrolls the ramp
    // through once; the shader/cycle driver's own phase (Evaluate's `phase` arg) ADDS on top of this authored one.
    [SerializeReference] public ZUIValue phaseAnim;

    // ── cycling (authored default cadence; a driver on the target turns time into phase) ──
    [Tooltip("This ramp wants to colour-cycle. A ZuiPaletteCycle driver advances the phase; Evaluate/ToLut take it as a parameter.")]
    public bool cycle = false;
    [Tooltip("Cycle speed (phase units per second) the driver should use by default.")]
    public float cycleSpeed = 0.5f;

    // ── the Gradient compatibility surface ───────────────────────────────────────────────────────────────

    // A Gradient EXPORT is rebuilt only when the stops actually changed; every mutator bumps _rev. Without this
    // the property would allocate a fresh Gradient on each read, and callers do read it in loops.
    [NonSerialized] Gradient _export;
    [NonSerialized] int _exportRev = -1;
    [NonSerialized] int _rev;

    /// <summary>The ramp as a UnityEngine.Gradient — an import/export FORMAT, not the storage. Reading builds one
    /// from the stops (exact up to Gradient's own 8-key cap, evenly subsampled beyond it, endpoints always kept);
    /// assigning REPLACES every stop from that Gradient's keys, which is always exact in that direction. Kept as a
    /// property so every call site written against the old field still compiles and behaves. Prefer the stop list
    /// itself (or <see cref="HasRamp"/> for a "is anything authored" check) in new code.</summary>
    public Gradient gradient
    {
        get
        {
            EnsureStops();
            if (_export != null && _exportRev == _rev) return _export;
            _export = BuildGradient();
            _exportRev = _rev;
            return _export;
        }
        set => SetGradient(value);
    }

    /// <summary>True when this gradient has something to sample — stops, or (before the conversion has run) a
    /// legacy Gradient. The cheap replacement for the old `g.gradient != null` test, which now builds an export.</summary>
    public bool HasRamp => (stops != null && stops.Count > 0) || legacyGradient != null;

    /// <summary>Replace every stop from <paramref name="g"/>'s keys (colour and alpha keys are folded together at
    /// the union of their positions, so an alpha key that sits between two colour keys still lands as a stop).
    /// Null clears the ramp.</summary>
    public void SetGradient(Gradient g)
    {
        stops ??= new List<ZuiGradientStop>();
        stops.Clear();
        legacyGradient = g;
        if (g != null) BuildStops(g, stops);
        stopsAuthored = true;
        _rev++;
    }

    /// <summary>Convert the legacy Gradient into stops if that has not happened yet. Idempotent, and a no-op once
    /// stops exist. NOT called from Evaluate: an un-converted gradient renders through its legacy Gradient instead,
    /// so nothing on a render thread ever triggers a conversion mid-frame.</summary>
    public void EnsureStops()
    {
        if (stopsAuthored) return;
        stops ??= new List<ZuiGradientStop>();
        if (stops.Count == 0 && legacyGradient != null) BuildStops(legacyGradient, stops);
        stopsAuthored = true;
        _rev++;
    }

    /// The stop list, guaranteed non-null and converted. For a host that wants to read the real storage.
    public List<ZuiGradientStop> Stops { get { EnsureStops(); return stops; } }

    /// Mark the stops as edited from outside (a host that mutated the list directly), so the cached Gradient
    /// export and any evaluation cache are rebuilt.
    public void MarkStopsChanged() { stopsAuthored = true; _rev++; }

    /// <summary>A deep copy — stops, space and every transform. Nothing is shared with the original.</summary>
    public ZuiGradient Clone()
    {
        var c = new ZuiGradient
        {
            legacyGradient = CloneGradient(legacyGradient),
            space = space,
            stopsAuthored = stopsAuthored,
            reverse = reverse, quantiseSteps = quantiseSteps, bandLocked = bandLocked,
            hueShift = hueShift, saturation = saturation, brightness = brightness, contrast = contrast,
            cycle = cycle, cycleSpeed = cycleSpeed,
        };
        c.stops.Clear();
        if (stops != null) foreach (var s in stops) c.stops.Add(new ZuiGradientStop(s.pos, s.color));
        // The animatable companions too, or a clone silently flattens an authored Curve back to the legacy
        // constant its Static seed would produce.
        c.hueShiftAnim   = CloneVal(hueShiftAnim);
        c.saturationAnim = CloneVal(saturationAnim);
        c.brightnessAnim = CloneVal(brightnessAnim);
        c.contrastAnim   = CloneVal(contrastAnim);
        c.phaseAnim      = CloneVal(phaseAnim);
        return c;
    }

    static ZUIValue CloneVal(ZUIValue v)
    {
        if (v == null) return null;
        var c = new ZUIValue();
        c.CopyFrom(v);
        return c;
    }

    static Gradient CloneGradient(Gradient g)
    {
        if (g == null) return null;
        var clone = new Gradient();
        clone.SetKeys(g.colorKeys, g.alphaKeys);
        clone.mode = g.mode;
        return clone;
    }

    /// <summary>The transformed colour at position <paramref name="t"/> (0..1), offset by <paramref name="phase"/>
    /// (the cycle), with the colour transforms sampled at 0..1 <paramref name="life"/>. Non-destructive: reads the
    /// ramp and applies the knobs. A Static/default gradient at life 0 is byte-identical to the pre-migration
    /// path.</summary>
    public Color Evaluate(float t, float phase = 0f, float life = 0f)
    {
        float u = reverse ? 1f - t : t;
        // The authored scroll (animatable over life) plus any external cycle phase the caller passed.
        float ph = phase + EvalTransform(phaseAnim, life, 0f);
        // With a scroll, MIRROR the ramp (append its reverse: 0→1→0) via PingPong so it loops SEAMLESSLY — the
        // colour at the wrap matches instead of jumping from the end back to the start. No offset ⇒ Clamp01 (a
        // plain Evaluate(1) must stay at the ramp END; PingPong/Repeat(1,1) would fold/wrap it to 0), so the
        // default (ph==0) is byte-identical to the pre-phase path.
        u = ph != 0f ? Mathf.PingPong(u + ph, 1f) : Mathf.Clamp01(u);
        int steps = bandLocked ? Mathf.Max(1, quantiseSteps) : quantiseSteps;
        if (steps > 0)
            u = Mathf.Floor(u * steps) / Mathf.Max(1, steps - 1);
        u = Mathf.Clamp01(u);

        Color c = EvalRamp(u);

        // The animatable colour transforms, evaluated at THIS life. Static ⇒ the exact legacy constant (byte-identical);
        // Curve ⇒ animates over life. A null companion falls back to the legacy float — same value again.
        float hueShiftV    = EvalTransform(hueShiftAnim,    life, hueShift);
        float saturationV  = EvalTransform(saturationAnim,  life, saturation);
        float brightnessV  = EvalTransform(brightnessAnim,  life, brightness);
        float contrastV    = EvalTransform(contrastAnim,    life, contrast);

        if (hueShiftV != 0f || saturationV != 1f || brightnessV != 1f)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShiftV * 0.5f, 1f);   // ±1 → ±180°
            s = Mathf.Clamp01(s * saturationV);
            v = Mathf.Clamp01(v * brightnessV);
            float a = c.a;
            c = Color.HSVToRGB(h, s, v); c.a = a;
        }
        if (contrastV != 1f)
        {
            c.r = Mathf.Clamp01((c.r - 0.5f) * contrastV + 0.5f);
            c.g = Mathf.Clamp01((c.g - 0.5f) * contrastV + 0.5f);
            c.b = Mathf.Clamp01((c.b - 0.5f) * contrastV + 0.5f);
        }
        return c;
    }

    /// <summary>The RAW ramp at t — the stops blended in this gradient's own space, with NO transform knobs
    /// applied. This is the ramp the stop editor paints (the source you edit), while Evaluate is what renders.
    /// Before the stop conversion has run it reads the legacy Gradient directly, so an un-migrated asset is
    /// byte-identical either way. Allocation-free and thread-safe.</summary>
    public Color EvalRamp(float t)
    {
        var s = stops;
        int n = s?.Count ?? 0;
        if (n == 0) return legacyGradient != null ? legacyGradient.Evaluate(Mathf.Clamp01(t)) : Color.white;
        if (n == 1) return s[0].color;

        float u = Mathf.Clamp01(t);
        if (u <= s[0].pos) return s[0].color;
        int last = n - 1;
        if (u >= s[last].pos) return s[last].color;
        int i = 0;
        while (i < last - 1 && u > s[i + 1].pos) i++;
        var a = s[i]; var b = s[i + 1];
        float span = b.pos - a.pos;
        float k = span > 1e-6f ? (u - a.pos) / span : 1f;
        return Blend(a.color, b.color, k, space);
    }

    /// <summary>Blend two stop colours by k in <paramref name="sp"/>; alpha is linear in both spaces (an opacity
    /// ramp has no gamma). sRGB is a plain lerp — the exact operation UnityEngine.Gradient performs, which is why
    /// it is the default and why a converted gradient renders unchanged.</summary>
    public static Color Blend(Color a, Color b, float k, ZuiGradientSpace sp)
    {
        if (sp == ZuiGradientSpace.Srgb) return Color.LerpUnclamped(a, b, k);
        var la = ToLinear(a); var lb = ToLinear(b);
        var l = new Color(la.r + (lb.r - la.r) * k, la.g + (lb.g - la.g) * k, la.b + (lb.b - la.b) * k,
                          a.a + (b.a - a.a) * k);
        return ToSrgb(l);
    }

    static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
    static Color ToLinear(Color c) => new Color(SrgbToLinear(c.r), SrgbToLinear(c.g), SrgbToLinear(c.b), c.a);
    static Color ToSrgb(Color c) => new Color(LinearToSrgb(c.r), LinearToSrgb(c.g), LinearToSrgb(c.b), c.a);

    // ── IZuiRamp (the stop editor's view of this gradient) ───────────────────────────────────────────────
    // Every member is a thin forwarder over `stops`/`space` — no extra state, no second algorithm. Eval is the
    // RAW ramp (EvalRamp), because the editor's strip is the SOURCE ramp; the transform knobs are shown by the
    // separate objective preview the control paints from ToLut().

    static readonly string[] s_spaceNames = { "Linear Light", "sRGB" };

    public int Count { get { EnsureStops(); return stops.Count; } }
    public float GetPos(int i) => Stops[i].pos;
    public void SetPos(int i, float pos) { Stops[i].pos = Mathf.Clamp01(pos); _rev++; }
    public Color GetColor(int i) => Stops[i].color;
    public void SetColor(int i, Color c) { Stops[i].color = c; _rev++; }

    public int Insert(float pos, Color c)
    {
        EnsureStops();
        pos = Mathf.Clamp01(pos);
        int idx = stops.Count;
        for (int i = 0; i < stops.Count; i++) { if (stops[i].pos > pos) { idx = i; break; } }
        stops.Insert(idx, new ZuiGradientStop(pos, c));
        _rev++;
        return idx;
    }

    public void RemoveAt(int i)
    {
        EnsureStops();
        if (i < 0 || i >= stops.Count) return;
        stops.RemoveAt(i);
        _rev++;
    }

    public Color Eval(float t) => EvalRamp(t);

    public string[] BlendModeNames => s_spaceNames;

    public int BlendMode
    {
        get => (int)space;
        set { space = (ZuiGradientSpace)Mathf.Clamp(value, 0, s_spaceNames.Length - 1); _rev++; }
    }

    /// <summary>Bake the transformed ramp to a 1-D LUT texture (for the palette-cycle shader, and for the editor's
    /// objective preview). Repeat-wrap + bilinear so a shader's frac(t+phase) cycles smoothly. Bake at phase 0; the
    /// shader scrolls the phase. The colour transforms are sampled at <paramref name="life"/> (0 = a life-0
    /// snapshot, the default) — UNLESS <paramref name="lifeFollowsPosition"/> is set, in which case each texel's
    /// `life` tracks its own ramp position instead of the fixed <paramref name="life"/>. That matches an OverLife
    /// ZuiFill, where the ramp position IS the life (see ZuiFill.EvalGrad) — without this, a texel-independent life
    /// would sample every colour-transform (hue/sat/BRIGHTNESS/contrast) at the SAME instant, so an authored Curve
    /// would bake as one flat multiplier smeared across the whole strip instead of the per-position value it will
    /// actually render — reading as "the transform does nothing, it just recolours the gradient" (task T-0030). The
    /// caller owns the returned texture.</summary>
    public Texture2D ToLut(int width = 256, float life = 0f, bool lifeFollowsPosition = false)
    {
        width = Mathf.Max(2, width);
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "ZuiGradient LUT",
        };
        var px = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = i / (float)(width - 1);
            px[i] = Evaluate(t, 0f, lifeFollowsPosition ? t : life);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Seed the animatable colour-transform companions from the legacy float fields when missing (the
    /// migration). A Static seed reproduces the legacy constant EXACTLY ⇒ byte-identical. Idempotent; never
    /// overwrites an authored companion. Called from OnAfterDeserialize and by the editor before it binds them;
    /// Evaluate does NOT depend on it — EvalTransform falls back to the legacy float when a companion is null.</summary>
    public void EnsureTransformAnim()
    {
        if (hueShiftAnim == null)   hueShiftAnim   = new ZUIValue(hueShift);
        if (saturationAnim == null) saturationAnim = new ZUIValue(saturation);
        if (brightnessAnim == null) brightnessAnim = new ZUIValue(brightness);
        if (contrastAnim == null)   contrastAnim   = new ZUIValue(contrast);
        if (phaseAnim == null)      phaseAnim      = new ZUIValue(0f);   // no scroll by default
    }

    // ISerializationCallbackReceiver: seed the companions on load (the migration). No Unity API is touched here,
    // so it is safe on Unity's deserialize thread — which is also why the STOP conversion is NOT done here: it
    // reads the legacy Gradient's keys, a native call. EnsureStops runs from the editor / an explicit caller
    // instead, and until it does, EvalRamp reads the legacy Gradient directly.
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize() { EnsureTransformAnim(); _rev++; }

    /// <summary>Evaluate an animatable colour transform over the 0..1 <paramref name="life"/> clock. Mirrors
    /// ZuiFill.EvalCompanion: Static ⇒ its constant (a migrated transform is byte-identical), Curve ⇒ its points
    /// sampled directly at clamped life, MinMax ⇒ its stable midpoint (a per-eval random would shimmer a shared
    /// gradient), null ⇒ <paramref name="fallback"/> (the legacy float).</summary>
    static float EvalTransform(ZUIValue v, float life, float fallback)
    {
        if (v == null) return fallback;
        switch (v.mode)
        {
            case ZUIValue.Mode.Static: return v.staticValue;
            case ZUIValue.Mode.Curve:  return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
            case ZUIValue.Mode.MinMax: return (v.min + v.max) * 0.5f;
            default:                   return v.staticValue;
        }
    }

    // ── Gradient ⇄ stops conversion (the boundary, never the storage) ────────────────────────────────────

    /// UnityEngine.Gradient's own key cap. A ramp with more stops than this is evenly subsampled ON EXPORT ONLY,
    /// endpoints always kept — the stop list itself is never truncated.
    public const int MaxGradientKeys = 8;

    /// One stop per DISTINCT key position across the Gradient's colour AND alpha keys, coloured by evaluating the
    /// Gradient there. On each sub-interval a Blend gradient is linear in both colour and alpha, so linear
    /// interpolation between these stops reproduces it exactly. A Fixed (stepped) gradient gets a second stop just
    /// before each boundary, so the step survives as a step instead of becoming a ramp.
    static void BuildStops(Gradient g, List<ZuiGradientStop> into)
    {
        const float StepEps = 1e-4f;
        var times = new List<float>();
        var ck = g.colorKeys;
        var ak = g.alphaKeys;
        if (ck != null) foreach (var k in ck) times.Add(Mathf.Clamp01(k.time));
        if (ak != null) foreach (var k in ak) times.Add(Mathf.Clamp01(k.time));
        times.Sort();

        bool stepped = g.mode == GradientMode.Fixed;
        float prev = float.NegativeInfinity;
        foreach (var t in times)
        {
            if (t - prev < 1e-6f) continue;                      // one stop per distinct position
            if (stepped && prev > float.NegativeInfinity && t - StepEps > prev)
                into.Add(new ZuiGradientStop(t - StepEps, g.Evaluate(t - StepEps)));
            into.Add(new ZuiGradientStop(t, g.Evaluate(t)));
            prev = t;
        }
        if (into.Count == 0) into.Add(new ZuiGradientStop(0f, g.Evaluate(0f)));
    }

    /// The stops as a Gradient: exact at or below Gradient's 8-key cap, evenly subsampled above it (first and
    /// last stop always kept, so the ramp's endpoints never move). Null when there is nothing to export.
    Gradient BuildGradient()
    {
        int n = stops?.Count ?? 0;
        if (n == 0) return null;

        var picked = new List<ZuiGradientStop>(Mathf.Min(n, MaxGradientKeys));
        if (n <= MaxGradientKeys) picked.AddRange(stops);
        else
            for (int i = 0; i < MaxGradientKeys; i++)
                picked.Add(stops[Mathf.RoundToInt(i * (n - 1) / (float)(MaxGradientKeys - 1))]);

        var ck = new GradientColorKey[picked.Count];
        var ak = new GradientAlphaKey[picked.Count];
        for (int i = 0; i < picked.Count; i++)
        {
            ck[i] = new GradientColorKey(picked[i].color, picked[i].pos);
            ak[i] = new GradientAlphaKey(picked[i].color.a, picked[i].pos);
        }
        var g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
