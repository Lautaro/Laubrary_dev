using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// How a <see cref="SpriteFxFilter"/> picks the inline-vs-Burst code path.
    public enum SfxDispatch
    {
        UseProjectSetting,   // read SpriteFxSettings.UseBurstJobs (the default)
        ForceInline,         // always run the managed inline loop
        ForceBurst           // always schedule the Burst SfxStackJob
    }

    /// A RUNTIME sprite filter: applies a stateless colour/mask modifier stack (the #45/#46 SpriteFx family) to a
    /// live SpriteRenderer over a triggered timeline — the "brightness flash on hurt" use case. It is stateless per
    /// frame: every tick it RE-READS the renderer's CURRENT sprite pixels, so it rides on top of a live Reel/Animator
    /// animation (the underlying frame keeps advancing underneath the flash), resolves the stack's animatable params
    /// at the current progress, applies them (inline OR Burst per the project toggle) into a pooled Texture2D, and
    /// swaps the filtered result onto the renderer. When the timeline ends it restores the original sprite.
    ///
    /// TRIGGER it with <see cref="Play()"/> / <see cref="Play(float)"/>. Author the stack in `modifiers` (any of the
    /// eight shaped PixelModifiers: Brightness/Tint/Contrast/Saturation/Posterize/OrderedDither/LayerDissolve/
    /// AlphaMask — non-shaped/disabled entries are skipped) or let <see cref="SpriteFxHurtFlash"/> build one for you.
    ///
    /// REQUIREMENT — the source sprite's texture must be Read/Write enabled (its importer's "Read/Write Enabled"
    /// checkbox), because the filter reads its pixels on the CPU each frame. If it is not readable the filter logs a
    /// one-time warning and leaves the sprite untouched (graceful no-op) rather than throwing. Pixel-art sprites are
    /// small and cheap to mark R/W; a GPU-readback (RenderTexture + ReadPixels) fallback for locked textures is the
    /// noted escape hatch but is intentionally NOT built here (it adds an orientation/platform-flip risk this thin
    /// slice avoids).
    [RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("Laubrary/SpriteFx/Sprite Fx Filter")]
    public class SpriteFxFilter : MonoBehaviour
    {
        [Tooltip("The stateless colour/mask stack applied while the filter plays, in order. Use the shaped pixel " +
                 "modifiers (Brightness/Tint/Contrast/Saturation/Posterize/OrderedDither/LayerDissolve/AlphaMask); " +
                 "each modifier's animatable values are resolved at the current life each frame. SpriteFxHurtFlash " +
                 "populates this for the flash-on-hurt case.")]
        [SerializeReference] public List<PixelModifier> modifiers = new List<PixelModifier>();

        [Tooltip("Optional: source the stack, duration, envelope and seed from a shared SpriteFxSpec ASSET (a " +
                 "reusable 'SpriteFx Stack') instead of the inline fields below. When assigned the asset WINS (the " +
                 "inline modifiers/duration/envelope/seed are ignored), so one authored effect can be reused across " +
                 "many entities and browsed/tagged.")]
        public SpriteFxSpec stack;

        [Tooltip("How long one Play() lasts, in seconds. Ignored when a Stack asset is assigned.")]
        [Min(0.001f)] public float duration = 0.15f;

        [Tooltip("Optional easing/remap of raw progress (0→1 over Duration) into the LIFE value fed to every " +
                 "modifier's animatable curves. Identity by default (life = progress); make it a triangle " +
                 "(0→1→0) to turn a monotonic modifier curve into a pulse, or an ease to soften the ends.")]
        public AnimationCurve envelope = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Inline (managed) vs Burst-job dispatch. Use project setting reads the SpriteFxSettings asset; " +
                 "Force inline / Force burst override it on this component (handy for profiling or verification).")]
        public SfxDispatch dispatch = SfxDispatch.UseProjectSetting;

        [Tooltip("Seed for any hashing modifier (LayerDissolve scatter, AlphaMask noise). Irrelevant for the " +
                 "plain Brightness/Tint flash.")]
        public int seed = 12345;

        // ── live state ─────────────────────────────────────────────────────────────────────────────────────────
        SpriteRenderer _sr;
        Sprite _sourceSprite;     // the live UN-filtered source (a Reel frame, or the static sprite) we ride on top of
        Sprite _filteredSprite;   // the sprite we swap in — references _work (pooled)
        Texture2D _work;          // pooled working texture, resized only when the source geometry changes
        bool _playing;
        float _elapsed, _dur;
        int _tickFrame;
        bool _warnedUnreadable;

        // cached geometry of the sprite _filteredSprite was built for, to know when to rebuild it
        Vector2 _lastPivotNorm;
        float _lastPpu;
        int _lastW, _lastH;

        /// True while a triggered timeline is running.
        public bool IsPlaying => _playing;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        /// Trigger the filter for the effective <see cref="duration"/> (the Stack asset's duration if one is assigned).
        public void Play() => Play(EffectiveDuration);

        /// Trigger the filter for a specific duration (seconds). Re-triggering while active restarts the timeline.
        public void Play(float durationSeconds)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            _dur = Mathf.Max(0.001f, durationSeconds);
            _elapsed = 0f;
            _tickFrame = 0;
            _playing = true;
            // Capture the live source now so a static (non-animated) sprite restores correctly on finish, and apply
            // the first frame immediately so there is no one-frame flash of the raw sprite.
            _sourceSprite = _sr != null ? _sr.sprite : null;
            Tick(0f);
        }

        /// Stop early and restore the source sprite.
        public void Stop()
        {
            if (!_playing) return;
            _playing = false;
            Restore();
        }

        void Update()
        {
            if (!_playing) return;
            _elapsed += Time.deltaTime;
            if (_elapsed >= _dur) { Tick(_dur); _playing = false; Restore(); return; }
            Tick(_elapsed);
        }

        void Tick(float t)
        {
            if (_sr == null) return;

            // Ride on top of a live animation: whatever sprite is on the renderer right now that ISN'T our own
            // filtered sprite is the fresh source frame (a Reel/Animator wrote it this frame). If the renderer still
            // shows our filtered sprite, the underlying source didn't change — reuse the last one (handles a held
            // frame / paused animation without double-filtering our own output).
            Sprite cur = _sr.sprite;
            if (cur != null && !ReferenceEquals(cur, _filteredSprite)) _sourceSprite = cur;
            Sprite src = _sourceSprite;
            if (src == null || src.texture == null) return;

            if (!ReadSource(src, out Color32[] pixels, out int W, out int H)) return;

            float p = _dur > 0f ? Mathf.Clamp01(t / _dur) : 1f;
            float life = SampleEnvelope(p);

            Apply(pixels, W, H, EffectiveModifiers, _tickFrame, life, EffectiveSeed, ResolveUseBurst());
            _tickFrame++;

            EnsureWork(src, W, H);
            _work.SetPixels32(pixels);
            _work.Apply(false);
            EnsureFilteredSprite(src, W, H);
            _sr.sprite = _filteredSprite;
        }

        // ── effective source (a Stack asset, when assigned, overrides every inline field) ─────────────────────────
        List<PixelModifier> EffectiveModifiers => stack != null ? stack.modifiers : modifiers;
        float EffectiveDuration => stack != null ? Mathf.Max(0.001f, stack.duration) : duration;
        int EffectiveSeed => stack != null ? stack.seed : seed;

        float SampleEnvelope(float progress01)
        {
            if (stack != null) return stack.SampleEnvelope(progress01);
            if (envelope == null || envelope.length == 0) return progress01;
            return envelope.Evaluate(progress01);
        }

        bool ResolveUseBurst() => dispatch switch
        {
            SfxDispatch.ForceInline => false,
            SfxDispatch.ForceBurst => true,
            _ => SpriteFxSettings.UseBurstJobs
        };

        void Restore()
        {
            // Only restore if WE are still the one on the renderer — never stomp a frame a Reel/Animator advanced to.
            if (_sr != null && _sourceSprite != null && ReferenceEquals(_sr.sprite, _filteredSprite))
                _sr.sprite = _sourceSprite;
        }

        // ── core apply — the SAME routine Tick and the edit-mode verification both call ──────────────────────────
        /// Apply a shaped modifier stack to a Color32 buffer in place, dispatching inline or to the Burst job. This
        /// is the single code path <see cref="Tick"/> uses each frame; the verification harness calls it directly so
        /// "what a probe tests" == "what plays". `mods` may contain any PixelModifiers — non-shaped/disabled ones
        /// are skipped by <see cref="SpriteFxStack.Resolve"/> (a runtime filter only handles the gather-free family).
        public static void Apply(Color32[] pixels, int W, int H, IReadOnlyList<PixelModifier> mods,
                                 int frame, float life, int seed, bool useBurst)
        {
            if (pixels == null || pixels.Length == 0) return;

            var eval = SpriteFxStack.LifeEval(life, seed);
            SpriteFxStack.Resolve(mods, eval, Allocator.TempJob, out var ops, out var luts);
            try
            {
                if (ops.Length == 0) return;   // nothing shaped/enabled — leave the buffer untouched
                if (useBurst)
                {
                    var native = new NativeArray<Color32>(pixels.Length, Allocator.TempJob);
                    native.CopyFrom(pixels);
                    SpriteFxStack.Schedule(native, ops, luts, W, H, frame, life, seed).Complete();
                    native.CopyTo(pixels);
                    native.Dispose();
                }
                else
                {
                    SpriteFxStack.RunInline(pixels, ops, luts, W, H, frame, life, seed);
                }
            }
            finally
            {
                if (ops.IsCreated) ops.Dispose();
                if (luts.IsCreated) luts.Dispose();
            }
        }

        // ── source read (R/W required) ───────────────────────────────────────────────────────────────────────────
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
                    Debug.LogWarning($"[SpriteFxFilter] Sprite texture '{tex.name}' is not Read/Write enabled — " +
                                     "cannot read its pixels on the CPU, so the filter is a no-op. Tick " +
                                     "'Read/Write Enabled' on the sprite's import settings.", this);
                }
                return false;
            }

            if (x == 0 && y == 0 && W == tex.width && H == tex.height)
            {
                px = tex.GetPixels32();   // whole-texture fast path — exact Color32, no conversion
            }
            else
            {
                Color[] block = tex.GetPixels(x, y, W, H);   // atlased sub-rect
                px = new Color32[block.Length];
                for (int i = 0; i < block.Length; i++) px[i] = (Color32)block[i];
            }
            return true;
        }

        // ── pooled texture + sprite ──────────────────────────────────────────────────────────────────────────────
        void EnsureWork(Sprite src, int W, int H)
        {
            if (_work != null && _work.width == W && _work.height == H) return;
            if (_work != null) SafeDestroy(_work);
            _work = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = src.texture != null ? src.texture.filterMode : FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            // force a matching sprite rebuild
            _lastW = _lastH = -1;
        }

        void EnsureFilteredSprite(Sprite src, int W, int H)
        {
            Vector2 pivotNorm = new Vector2(
                src.rect.width > 0f ? src.pivot.x / src.rect.width : 0.5f,
                src.rect.height > 0f ? src.pivot.y / src.rect.height : 0.5f);

            bool geometryChanged = _filteredSprite == null || _lastW != W || _lastH != H ||
                                   _lastPpu != src.pixelsPerUnit || _lastPivotNorm != pivotNorm;
            if (!geometryChanged) return;   // same geometry — the existing sprite already reflects _work's new pixels

            if (_filteredSprite != null) SafeDestroy(_filteredSprite);
            _filteredSprite = Sprite.Create(_work, new Rect(0, 0, W, H), pivotNorm, src.pixelsPerUnit, 0,
                                            SpriteMeshType.FullRect, src.border);
            _filteredSprite.name = src.name + " (SpriteFx)";
            _lastW = W; _lastH = H; _lastPpu = src.pixelsPerUnit; _lastPivotNorm = pivotNorm;
        }

        void OnDisable()
        {
            if (_playing) { _playing = false; Restore(); }
        }

        void OnDestroy()
        {
            if (_work != null) SafeDestroy(_work);
            if (_filteredSprite != null) SafeDestroy(_filteredSprite);
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
