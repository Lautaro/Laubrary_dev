using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// A PERSISTENT, managed recolour of a <see cref="SpriteRenderer"/>: every frame it re-reads the renderer's
    /// CURRENT sprite pixels, runs a MANAGED <see cref="PixelModifier"/> stack over them via
    /// <see cref="PixelModifier.ApplyPixel"/>, and swaps the recoloured result onto the renderer. Runs in the editor
    /// too (<c>[ExecuteAlways]</c>) so authoring a recolour is WYSIWYG.
    ///
    /// This is the delivery host for the #77 <see cref="ColorRemapModifier"/> — the "SpriteFx CPU filter" row of the
    /// colour design's delivery table. Unlike <see cref="SpriteFxFilter"/> (which is TRIGGERED and runs the Burst/op
    /// path — <see cref="SpriteFxStack.IsShaped"/> modifiers only, so it skips ColorRemap), this is ALWAYS-ON and
    /// MANAGED, so it runs the non-shaped colour family. It rides a live Reel / Animator animation the same way
    /// SpriteFxFilter does: whatever sprite is on the renderer that isn't OUR own output is treated as the fresh
    /// source frame, so the recolour follows the animation automatically. It restores the original sprite when
    /// disabled or destroyed.
    ///
    /// REQUIREMENT — the source sprite's texture must be Read/Write enabled (the pixels are read on the CPU each
    /// frame). If it is not readable this logs one warning and no-ops (graceful). Pixel-art sheets are small and
    /// cheap to mark R/W. Cost is per-sprite/per-frame CPU — fine for a few key sprites; the GPU/shader path is the
    /// answer for many (see ZUI_COLOR_DESIGN.md → Delivery mechanisms).
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Laubrary/SpriteFx/Sprite Fx Recolor")]
    public class SpriteFxRecolor : MonoBehaviour
    {
        [Tooltip("The managed recolour stack applied every frame, in order — typically a single Colour remap effect, " +
                 "but any PixelModifier works. Ignored when a Stack asset is assigned below.")]
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

        [Tooltip("Optional: source the stack from a shared SpriteFxSpec ASSET instead of the inline list above. When " +
                 "assigned the asset WINS, so one authored recolour can be reused across many sprites.")]
        public SpriteFxSpec stack;

        [Range(0f, 1f)]
        [Tooltip("The life value fed to the stack (0..1) — drives a cycling gradient region's phase and any " +
                 "animatable modifier param. A plain swatch/gradient recolour ignores it.")]
        public float life = 0f;

        [Tooltip("Play mode only: advance Life automatically over time so a cycling gradient region animates. In " +
                 "edit mode Life stays exactly where you set it (no continuous repaint is forced).")]
        public bool autoLife = false;
        [Min(0.01f)]
        [Tooltip("Seconds for Life to travel 0→1 when Auto life is on.")]
        public float lifeSeconds = 2f;

        [Tooltip("Seed for any hashing modifier in the stack (Dissolve scatter, etc). Irrelevant to a plain recolour.")]
        public int seed = 12345;

        // ── live state ───────────────────────────────────────────────────────────────────────────────────────────
        SpriteRenderer _sr;
        Sprite _sourceSprite;     // the live UN-recoloured source (a Reel frame, or the static sprite) we ride on
        Sprite _filteredSprite;   // the sprite we swap in — references _work (pooled)
        Texture2D _work;          // pooled working texture, resized only when the source geometry changes
        int _frame;
        bool _warnedUnreadable;

        Vector2 _lastPivotNorm;
        float _lastPpu;
        int _lastW = -1, _lastH = -1;

        // A recolour runs the managed per-pixel path only, so it takes just the PIXEL effects out of a stack
        // that may now also hold warps and whole-frame passes — those need the full RunStack dispatch, which
        // this component deliberately does not do.
        static readonly List<PixelModifier> s_pixelOnly = new List<PixelModifier>();
        List<PixelModifier> Mods
        {
            get
            {
                s_pixelOnly.Clear();
                if (stack != null)
                {
                    // A shared stack may now hold warps and whole-frame passes too; take only what this
                    // component's per-pixel path can actually run.
                    if (stack.modifiers != null)
                        foreach (var m in stack.modifiers) if (m is PixelModifier pm) s_pixelOnly.Add(pm);
                }
                else if (modifiers != null)
                    foreach (var m in modifiers) if (m is PixelModifier pm) s_pixelOnly.Add(pm);
                return s_pixelOnly;
            }
        }
        int EffectiveSeed => stack != null ? stack.seed : seed;

        void OnEnable() { _sr = GetComponent<SpriteRenderer>(); Refresh(); }
        void OnDisable() { Restore(); }

        void OnDestroy()
        {
            if (_work != null) SafeDestroy(_work);
            if (_filteredSprite != null) SafeDestroy(_filteredSprite);
        }

        void LateUpdate()
        {
            if (autoLife && Application.isPlaying && lifeSeconds > 0f)
                life = Mathf.Repeat(Time.time / lifeSeconds, 1f);
            Refresh();
        }

        /// Re-read the current source sprite, apply the managed stack, and swap in the recoloured result. Public so
        /// an editor preview / a demo can drive it explicitly without waiting for LateUpdate.
        public void Refresh()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;

            // Ride a live animation: any sprite on the renderer that isn't our own output is the fresh source frame.
            Sprite cur = _sr.sprite;
            if (cur != null && !ReferenceEquals(cur, _filteredSprite)) _sourceSprite = cur;
            Sprite src = _sourceSprite;
            if (src == null || src.texture == null) return;

            var mods = Mods;
            if (!AnyEnabled(mods)) { Restore(); return; }   // nothing to do → show the untouched source

            if (!ReadSource(src, out Color32[] pixels, out int W, out int H)) return;

            ApplyManaged(pixels, W, H, mods, life, EffectiveSeed, _frame++);

            EnsureWork(src, W, H);
            _work.SetPixels32(pixels);
            _work.Apply(false);
            EnsureFilteredSprite(src, W, H);
            _sr.sprite = _filteredSprite;
        }

        static bool AnyEnabled(List<PixelModifier> mods)
        {
            if (mods == null) return false;
            for (int i = 0; i < mods.Count; i++)
                if (mods[i] != null && mods[i].enabled) return true;
            return false;
        }

        // ── managed per-pixel apply — the SAME ApplyPixel contract the Pyre baker's ApplyPix uses ────────────────
        static readonly List<PixelModifier> s_active = new List<PixelModifier>();

        /// Apply an enabled managed <see cref="PixelModifier"/> stack to a Color32 buffer in place, via ApplyPixel
        /// (NOT the Burst/op path — so it runs the non-shaped colour family like ColorRemap). Each modifier is
        /// Prepared once at this life, then run per pixel; a modifier returning false drops the pixel (alpha 0).
        public static void ApplyManaged(Color32[] px, int W, int H, IReadOnlyList<PixelModifier> mods,
                                        float life, int seed, int frame)
        {
            if (px == null || px.Length == 0 || mods == null) return;

            var eval = SpriteFxStack.LifeEval(life, seed);
            s_active.Clear();
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;
                m.Prepare(eval);
                s_active.Add(m);
            }
            if (s_active.Count == 0) return;

            for (int y = 0, idx = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++, idx++)
                {
                    Color32 s = px[idx];
                    Color col = new Color(s.r / 255f, s.g / 255f, s.b / 255f, s.a / 255f);
                    float a = col.a;
                    int hash = unchecked(seed ^ (x * 73856093) ^ (y * 19349663));
                    var info = new PixelInfo(x, y, x + 0.5f, y + 0.5f, frame, 0f, life, hash, W, H);

                    bool keep = true;
                    for (int i = 0; i < s_active.Count; i++)
                        if (!s_active[i].ApplyPixel(ref col, ref a, info)) { keep = false; break; }

                    col.a = keep ? a : 0f;
                    px[idx] = (Color32)col;
                }
            }
            s_active.Clear();
        }

        void Restore()
        {
            // Only restore if WE are still the one on the renderer — never stomp a frame a Reel/Animator advanced to.
            if (_sr != null && _sourceSprite != null && ReferenceEquals(_sr.sprite, _filteredSprite))
                _sr.sprite = _sourceSprite;
        }

        // ── source read (R/W required) — mirrors SpriteFxFilter.ReadSource ────────────────────────────────────────
        bool ReadSource(Sprite s, out Color32[] px, out int W, out int H)
        {
            px = null; W = H = 0;
            Texture2D tex = s.texture;
            Rect tr = s.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            W = Mathf.RoundToInt(tr.width); H = Mathf.RoundToInt(tr.height);
            if (W <= 0 || H <= 0) return false;

            if (!tex.isReadable)
            {
                if (!_warnedUnreadable)
                {
                    _warnedUnreadable = true;
                    Debug.LogWarning($"[SpriteFxRecolor] Sprite texture '{tex.name}' is not Read/Write enabled — " +
                                     "cannot read its pixels on the CPU, so the recolour is a no-op. Tick " +
                                     "'Read/Write Enabled' on the sprite's import settings.", this);
                }
                return false;
            }

            if (x == 0 && y == 0 && W == tex.width && H == tex.height)
            {
                px = tex.GetPixels32();
            }
            else
            {
                Color[] block = tex.GetPixels(x, y, W, H);
                px = new Color32[block.Length];
                for (int i = 0; i < block.Length; i++) px[i] = (Color32)block[i];
            }
            return true;
        }

        // ── pooled texture + sprite — mirrors SpriteFxFilter ──────────────────────────────────────────────────────
        void EnsureWork(Sprite src, int W, int H)
        {
            if (_work != null && _work.width == W && _work.height == H) return;
            if (_work != null) SafeDestroy(_work);
            _work = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = src.texture != null ? src.texture.filterMode : FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave   // [ExecuteAlways] runs in edit mode — never serialise into a saved scene
            };
            _lastW = _lastH = -1;   // force a matching sprite rebuild
        }

        void EnsureFilteredSprite(Sprite src, int W, int H)
        {
            Vector2 pivotNorm = new Vector2(
                src.rect.width > 0f ? src.pivot.x / src.rect.width : 0.5f,
                src.rect.height > 0f ? src.pivot.y / src.rect.height : 0.5f);

            bool geometryChanged = _filteredSprite == null || _lastW != W || _lastH != H ||
                                   _lastPpu != src.pixelsPerUnit || _lastPivotNorm != pivotNorm;
            if (!geometryChanged) return;

            if (_filteredSprite != null) SafeDestroy(_filteredSprite);
            _filteredSprite = Sprite.Create(_work, new Rect(0, 0, W, H), pivotNorm, src.pixelsPerUnit, 0,
                                            SpriteMeshType.FullRect, src.border);
            _filteredSprite.name = src.name + " (Recolor)";
            _filteredSprite.hideFlags = HideFlags.DontSave;
            _lastW = W; _lastH = H; _lastPpu = src.pixelsPerUnit; _lastPivotNorm = pivotNorm;
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
