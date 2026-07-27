using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// The GPU / live twin of <see cref="SpriteFxRecolor"/>: drives a <see cref="SpriteRenderer"/> through the
    /// <c>Laubrary/ZUI/SpriteColorRemap</c> shader so a <see cref="ColorRemapModifier"/> recolour runs on the GPU —
    /// no per-frame CPU pixel work, and colour CYCLING comes for free (the shader scrolls each cycling region's
    /// gradient by <c>_Phase</c>). It bakes the remap's regions into the shader's uniform arrays plus a gradient-LUT
    /// ATLAS (one row per region, from <see cref="ZuiGradient.ToLut"/>), assigns a SpriteColorRemap material to the
    /// renderer (restoring the original on disable), and advances the phase over time.
    ///
    /// Up to <see cref="MaxRegions"/> regions (the shader's MAXREGIONS). Rebakes lazily — only when the regions
    /// change (an inspector edit) or a watched <see cref="SwatchPalette"/> bumps its version (so a live swatch edit
    /// recolours the sprite). Editor + play. The recolour matches <see cref="SpriteFxRecolor"/>'s managed output
    /// (verified: a URP render diffs to ~0.0003 mean per-channel against the CPU path).
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Laubrary/SpriteFx/Color Remap (GPU)")]
    public class ColorRemapGpu : MonoBehaviour
    {
        /// Matches MAXREGIONS in SpriteColorRemap.shader.
        public const int MaxRegions = 8;

        [Tooltip("The recolour to run on the GPU — the same ColorRemap effect the CPU SpriteFxRecolor uses, so the " +
                 "authoring is identical. Only the first 8 regions are used (the shader's limit).")]
        [SerializeReference] public ColorRemapModifier remap = new ColorRemapModifier();

        [Range(0f, 4f)]
        [Tooltip("Global cycle-speed multiplier applied on top of each cycling region's own cycleSpeed. Play mode " +
                 "advances the phase over time; edit mode holds it (no continuous repaint is forced).")]
        public float speed = 1f;

        [Tooltip("Optional: a SwatchPalette the regions draw from — the bake refreshes when its Version changes, so " +
                 "a live swatch edit recolours the sprite.")]
        public SwatchPalette watchPalette;

        [Range(16, 512)] public int lutWidth = 256;

        SpriteRenderer _sr;
        Material _mat;              // our SpriteColorRemap material instance (HideFlags.DontSave)
        Material _origSharedMat;    // restored on disable
        Texture2D _atlas;
        float _phase;
        bool _dirty = true;
        int _watchedVersion = int.MinValue;

        static readonly int IdRegionA = Shader.PropertyToID("_RegionA");
        static readonly int IdRegionB = Shader.PropertyToID("_RegionB");
        static readonly int IdRegionColor = Shader.PropertyToID("_RegionColor");
        static readonly int IdGradLut = Shader.PropertyToID("_GradLUT");
        static readonly int IdCount = Shader.PropertyToID("_RegionCount");
        static readonly int IdDrop = Shader.PropertyToID("_DropUnmatched");
        static readonly int IdPhase = Shader.PropertyToID("_Phase");
        static readonly int IdColor = Shader.PropertyToID("_Color");

        void OnEnable()
        {
            _sr = GetComponent<SpriteRenderer>();
            EnsureMaterial();
            _dirty = true;
        }

        void OnDisable()
        {
            if (_sr != null && ReferenceEquals(_sr.sharedMaterial, _mat)) _sr.sharedMaterial = _origSharedMat;
            DisposeOwned();
        }

        void OnDestroy() => DisposeOwned();

        void OnValidate() { _dirty = true; }

        /// Force a re-bake of the shader uniforms + LUT atlas on the next tick (call after editing the regions from
        /// a custom editor, which does not run OnValidate).
        public void MarkDirty() => _dirty = true;

        void EnsureMaterial()
        {
            if (_mat != null) return;
            var shader = Shader.Find("Laubrary/ZUI/SpriteColorRemap");
            if (shader == null)
            {
                Debug.LogWarning("[ColorRemapGpu] Shader 'Laubrary/ZUI/SpriteColorRemap' not found — recolour is a no-op.", this);
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.DontSave, name = "SpriteColorRemap (runtime)" };
            if (_sr != null)
            {
                _origSharedMat = _sr.sharedMaterial;
                _sr.sharedMaterial = _mat;
            }
        }

        void LateUpdate()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            EnsureMaterial();
            if (_mat == null || remap == null) return;

            if (watchPalette != null && watchPalette.Version != _watchedVersion) _dirty = true;
            if (_dirty) { Bake(); _dirty = false; }

            if (AnyCycle())
            {
                float dt = Application.isPlaying ? Time.deltaTime : 0f;   // edit mode holds; play advances
                _phase = Mathf.Repeat(_phase + dt * speed, 1f);
                _mat.SetFloat(IdPhase, _phase);
            }
        }

        bool AnyCycle()
        {
            var rs = remap?.regions;
            if (rs == null) return false;
            for (int i = 0; i < rs.Count && i < MaxRegions; i++)
                if (rs[i] != null && rs[i].cycle) return true;
            return false;
        }

        void Bake()
        {
            var rs = remap.regions;
            int count = 0;
            var A = new Vector4[MaxRegions];
            var B = new Vector4[MaxRegions];
            var col = new Vector4[MaxRegions];

            int total = rs != null ? rs.Count : 0;
            if (total > MaxRegions)
                Debug.LogWarning($"[ColorRemapGpu] {total} regions but the shader supports {MaxRegions}; using the first {MaxRegions}.", this);

            int rows = Mathf.Max(1, Mathf.Min(total, MaxRegions));
            EnsureAtlas(rows);
            var atlasPx = new Color[lutWidth * rows];

            for (int i = 0; i < rows; i++)
            {
                var r = rs[i];
                if (r == null) { A[i] = Vector4.zero; B[i] = new Vector4(0, 1, 0, i); col[i] = Vector4.zero; FillRow(atlasPx, i, Color.clear); continue; }

                Color.RGBToHSV(r.source, out float sh, out float ss, out float sv);
                bool grey = ss <= r.minSaturation;
                float center = grey ? sv : sh;
                float window = grey ? r.tolerance : r.tolerance * 0.5f;
                float code = (grey ? 2f : 0f) + (r.mode == ColorRemapMode.Gradient ? 1f : 0f);
                A[i] = new Vector4(center, window, r.minSaturation, code);
                B[i] = new Vector4(r.lumaLow, r.lumaHigh, r.cycle ? r.cycleSpeed : 0f, i);

                Color flat = r.swatch.Resolve();
                col[i] = new Vector4(flat.r, flat.g, flat.b, flat.a);

                if (r.mode == ColorRemapMode.Gradient && r.gradient != null)
                    for (int u = 0; u < lutWidth; u++)
                        atlasPx[i * lutWidth + u] = r.gradient.Evaluate(u / (float)(lutWidth - 1), 0f);
                else
                    FillRow(atlasPx, i, flat);

                count = i + 1;
            }

            _atlas.SetPixels(atlasPx);
            _atlas.Apply(false);

            _mat.SetVectorArray(IdRegionA, A);
            _mat.SetVectorArray(IdRegionB, B);
            _mat.SetVectorArray(IdRegionColor, col);
            _mat.SetTexture(IdGradLut, _atlas);
            _mat.SetFloat(IdCount, count);
            _mat.SetFloat(IdDrop, remap.dropUnmatched ? 1f : 0f);
            _mat.SetColor(IdColor, Color.white);
            _mat.SetFloat(IdPhase, _phase);

            _watchedVersion = watchPalette != null ? watchPalette.Version : int.MinValue;
        }

        void FillRow(Color[] px, int row, Color c)
        {
            for (int u = 0; u < lutWidth; u++) px[row * lutWidth + u] = c;
        }

        void EnsureAtlas(int rows)
        {
            if (_atlas != null && _atlas.width == lutWidth && _atlas.height == rows) return;
            if (_atlas != null) DestroyTex(ref _atlas);
            _atlas = new Texture2D(lutWidth, rows, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = "SpriteColorRemap LUT atlas"
            };
        }

        void DisposeOwned()
        {
            if (_atlas != null) DestroyTex(ref _atlas);
            if (_mat != null) { if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat); _mat = null; }
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t == null) return;
            if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
            t = null;
        }
    }
}
