// ZuiPaletteCycle.cs
// Drives a SpriteRenderer through the Laubrary/ZUI/SpritePaletteCycle shader: bakes a ZuiGradient into a 1-D
// LUT and advances the shader's _Phase over time (colour cycling), all via a MaterialPropertyBlock so one shared
// material serves many renderers, each with its own phase. Re-bakes the LUT when the gradient changes (or a
// SwatchPalette it draws from bumps its version — pass one to track live swatch edits). Editor + play.

using UnityEngine;

[ExecuteAlways]
[AddComponentMenu("Laubrary/ZUI/Palette Cycle")]
[RequireComponent(typeof(SpriteRenderer))]
public class ZuiPaletteCycle : MonoBehaviour
{
    [Tooltip("The colour ramp. Its quantise + cycle-speed defaults are honoured; ToLut() feeds the shader.")]
    public ZuiGradient gradient = new ZuiGradient();

    [Tooltip("Cycle speed in phase-units per second (overrides the gradient's own cycleSpeed when non-zero).")]
    public float speed = 0.5f;

    [Tooltip("Optional: a SwatchPalette this ramp draws from — the LUT re-bakes when the palette's Version changes, " +
             "so live swatch edits recolour the cycling sprite.")]
    public SwatchPalette watchPalette;

    [Range(16, 512)] public int lutWidth = 256;

    SpriteRenderer _sr;
    MaterialPropertyBlock _mpb;
    Texture2D _lut;
    float _phase;
    int _watchedVersion = int.MinValue;
    static readonly int IdLut = Shader.PropertyToID("_LUT");
    static readonly int IdPhase = Shader.PropertyToID("_Phase");
    static readonly int IdSteps = Shader.PropertyToID("_Steps");

    void OnEnable()
    {
        _sr = GetComponent<SpriteRenderer>();
        _mpb = new MaterialPropertyBlock();
        RebuildLut();
    }

    void OnDisable() => DisposeLut();
    void OnValidate() { _lut = null; }   // force a re-bake with the edited gradient on the next tick

    void RebuildLut()
    {
        DisposeLut();
        _lut = gradient != null ? gradient.ToLut(lutWidth) : null;
        if (_lut != null) _lut.hideFlags = HideFlags.HideAndDontSave;
        _watchedVersion = watchPalette != null ? watchPalette.Version : int.MinValue;
    }

    void DisposeLut()
    {
        if (_lut != null) { if (Application.isPlaying) Destroy(_lut); else DestroyImmediate(_lut); _lut = null; }
    }

    void LateUpdate()
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null || gradient == null) return;

        // Re-bake if the watched palette changed, or the LUT was invalidated (edit / first run).
        if (_lut == null || (watchPalette != null && watchPalette.Version != _watchedVersion)) RebuildLut();

        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;  // in edit mode, tick a nominal frame
        float s = speed != 0f ? speed : gradient.cycleSpeed;
        if (gradient.cycle || !Application.isPlaying) _phase = Mathf.Repeat(_phase + dt * s, 1f);

        _sr.GetPropertyBlock(_mpb);
        if (_lut != null) _mpb.SetTexture(IdLut, _lut);
        _mpb.SetFloat(IdPhase, _phase);
        _mpb.SetFloat(IdSteps, gradient.quantiseSteps);
        _sr.SetPropertyBlock(_mpb);
    }

    /// <summary>Set the phase directly (e.g. for a deterministic bake / a captured frame).</summary>
    public void SetPhase(float phase)
    {
        _phase = Mathf.Repeat(phase, 1f);
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        if (_sr == null) return;
        if (_lut == null) RebuildLut();
        _sr.GetPropertyBlock(_mpb ??= new MaterialPropertyBlock());
        if (_lut != null) _mpb.SetTexture(IdLut, _lut);
        _mpb.SetFloat(IdPhase, _phase);
        _mpb.SetFloat(IdSteps, gradient != null ? gradient.quantiseSteps : 0);
        _sr.SetPropertyBlock(_mpb);
    }
}
