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
    /// frame: every tick it RE-READS the renderer's CURRENT sprite pixels, so it rides on top of a live Lauminary/Animator
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
        [Tooltip("The stateless colour/mask stack applied while the filter plays, in order — order matters, " +
                 "because these effects clamp and so do not commute. Only the gather-free pixel family runs " +
                 "here; each effect's animatable values are resolved at the current life every frame. " +
                 "SpriteFxHurtFlash populates this for the flash-on-hurt case.")]
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

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
        [HideInInspector]
        public AnimationCurve envelope = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Optional easing/remap of raw progress into the LIFE value fed to every effect.")]
        public List<ZUIEnvelopePoint> envelopePoints = new List<ZUIEnvelopePoint>();

        /// Points, upgraded once from the frozen curve — see SpriteFxSpec.Envelope for why the data moved.
        public List<ZUIEnvelopePoint> Envelope
        {
            get
            {
                if (envelopePoints == null) envelopePoints = new List<ZUIEnvelopePoint>();
                if (envelopePoints.Count == 0) SpriteFxEnvelopeMigration.Seed(envelopePoints, envelope);
                return envelopePoints;
            }
        }

        [Tooltip("Inline (managed) vs Burst-job dispatch. Use project setting reads the SpriteFxSettings asset; " +
                 "Force inline / Force burst override it on this component (handy for profiling or verification).")]
        public SfxDispatch dispatch = SfxDispatch.UseProjectSetting;

        [Tooltip("Seed for any hashing modifier (LayerDissolve scatter, AlphaMask noise). Irrelevant for the " +
                 "plain Brightness/Tint flash.")]
        public int seed = 12345;

        [Tooltip("Own clock: how many times per second the stack's time advances while it plays. 0 (default) = " +
                 "continuous — re-evaluated every rendered frame, riding whatever animation drives it. Set a " +
                 "rate to step the effect on its own fixed grid, independent of the animation's fps (a fast " +
                 "flicker over a slow lauminary). Ignored when a Stack asset is assigned.")]
        [Min(0f)] public float targetFps = 0f;

        /// Optional: fed by a host OUTSIDE SpriteFx (e.g. Zoetrope's ReactionFxPlayer) that knows how to
        /// resolve an <see cref="IExternalPosition2D"/> modifier's key into a live position — a MetaLayer,
        /// say. Not serialized: this filter's stack may be a SHARED asset played by many characters at once,
        /// so the resolver (and therefore whose muzzle a light follows) is set fresh per character, not authored.
        [System.NonSerialized] public IExternalPositionResolver externalPositionResolver;

        // ── live state ─────────────────────────────────────────────────────────────────────────────────────────
        SpriteRenderer _sr;
        Sprite _sourceSprite;     // the live UN-filtered source (a Lauminary frame, or the static sprite) we ride on top of
        Sprite _filteredSprite;   // the sprite we swap in — references _work (pooled)
        Texture2D _work;          // pooled working texture, resized only when the source geometry changes
        bool _playing;
        float _elapsed, _dur;
        int _tickFrame;
        bool _warnedUnreadable;

        // playback-binding state (the Zoe-event attachment modes: loop-for-duration / run-at-end / ping-pong)
        bool _reversed;           // evaluate the current pass back-to-front (life 1→0) — ping-pong's return pass
        float _loopFor;           // > 0 ⇒ keep repeating whole passes until this many seconds have elapsed (Loop)
        bool _hasPending;         // one scheduled pass (run-at-end / ping-pong's return) — counts down even while idle
        float _pendingDelay, _pendingDur;
        bool _pendingReversed;

        // own-clock skip guard: the last applied step + the source it was applied to (targetFps > 0 only)
        int _lastStep = -1;
        Sprite _lastStepSrc;

        // cached geometry of the sprite _filteredSprite was built for, to know when to rebuild it
        Vector2 _lastPivotNorm;
        float _lastPpu;
        int _lastW, _lastH;

        /// True while a triggered timeline is running.
        public bool IsPlaying => _playing;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        /// Trigger the filter for the effective <see cref="duration"/> (the Stack asset's duration if one is assigned).
        public void Play() => Play(EffectiveDuration);

        /// Trigger the filter for a specific duration (seconds). Re-triggering while active restarts the timeline
        /// (and cancels any scheduled or looping playback).
        public void Play(float durationSeconds) => StartPass(durationSeconds, reversed: false, loopFor: 0f, cancelPending: true);

        /// Trigger ONE pass that runs back-to-front (life 1→0) — the SAME authored stack played against a
        /// reversed clock, so a "materialise" stack also dematerialises with no second asset and no mirrored
        /// copy of anything. Only the CLOCK reverses: the effect order is untouched, because these effects do
        /// not commute (brightness-then-contrast is a different picture from contrast-then-brightness), so
        /// reversing the list would produce a different effect rather than a mirrored one.
        ///
        /// Two things deliberately do NOT mirror: the hashing frame counter keeps counting forward, so a
        /// reversed dissolve un-dissolves but not through the identical grain; and the sprite underneath keeps
        /// animating forwards, so a character dematerialises while still moving instead of moonwalking.
        public void PlayReversed() => PlayReversed(EffectiveDuration);

        /// <inheritdoc cref="PlayReversed()"/>
        public void PlayReversed(float durationSeconds)
            => StartPass(durationSeconds, reversed: true, loopFor: 0f, cancelPending: true);

        /// Trigger and REPEAT whole passes back-to-back for <paramref name="totalSeconds"/> — the "loop for the
        /// event's duration" binding. Each pass lasts the effective duration; the final pass is cut wherever the
        /// total lands and the source sprite restores exactly then (the loop should end WITH its event, not
        /// finish a tail nobody is watching).
        public void PlayLooping(float totalSeconds)
            => StartPass(EffectiveDuration, reversed: false, loopFor: Mathf.Max(0.001f, totalSeconds), cancelPending: true);

        /// Schedule ONE pass to start <paramref name="delaySeconds"/> from now, playing for
        /// <paramref name="durationSeconds"/>; <paramref name="reversed"/> runs it back-to-front (life 1→0) —
        /// the fade-out half of ping-pong. Does NOT interrupt whatever is playing now (ping-pong's forward pass
        /// keeps going); a later <see cref="Play()"/>/<see cref="PlayLooping"/> cancels the schedule, and only
        /// one schedule is held at a time (the newest wins).
        public void SchedulePlay(float delaySeconds, float durationSeconds, bool reversed = false)
        {
            _hasPending = true;
            _pendingDelay = Mathf.Max(0f, delaySeconds);
            _pendingDur = Mathf.Max(0.001f, durationSeconds);
            _pendingReversed = reversed;
            if (_pendingDelay <= 0f) ConsumePending();   // "start at the end" of an already-ended window = now
        }

        /// Stop early, drop any scheduled pass, and restore the source sprite.
        public void Stop()
        {
            _hasPending = false;
            if (!_playing) return;
            _playing = false;
            Restore();
        }

        // The single start point every Play flavour routes through: one pass of `durationSeconds`, optionally
        // reversed, optionally repeated until `loopFor` seconds have elapsed.
        void StartPass(float durationSeconds, bool reversed, float loopFor, bool cancelPending)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (cancelPending) _hasPending = false;
            _dur = Mathf.Max(0.001f, durationSeconds);
            _elapsed = 0f;
            _tickFrame = 0;
            _reversed = reversed;
            _loopFor = loopFor > 0f ? loopFor : 0f;
            _lastStep = -1;
            _lastStepSrc = null;
            _playing = true;
            // Capture the live source now so a static (non-animated) sprite restores correctly on finish, and apply
            // the first frame immediately so there is no one-frame flash of the raw sprite. If the renderer is still
            // showing OUR OWN filtered output (a retrigger, or ping-pong's back-to-back return pass), keep the
            // previously captured source — capturing the filtered sprite as "source" would compound the effect onto
            // its own output every frame.
            Sprite cur = _sr != null ? _sr.sprite : null;
            if (cur == null || !ReferenceEquals(cur, _filteredSprite)) _sourceSprite = cur;
            Tick(0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // A scheduled pass counts down even while nothing is playing (run-at-end waits out most of its event).
            if (_hasPending)
            {
                _pendingDelay -= dt;
                if (_pendingDelay <= 0f) { ConsumePending(); return; }   // fresh pass already ticked at 0
            }

            if (!_playing) return;
            _elapsed += dt;
            float total = _loopFor > 0f ? _loopFor : _dur;
            if (_elapsed >= total)
            {
                // A single pass lands exactly on life 1 (the original behaviour); a timed loop just ends —
                // restoring mid-pass IS the contract (it ends with its event).
                if (_loopFor <= 0f) Tick(_dur);
                _playing = false;
                Restore();
                return;
            }
            Tick(_elapsed);
        }

        void ConsumePending()
        {
            _hasPending = false;
            StartPass(_pendingDur, _pendingReversed, 0f, cancelPending: false);
        }

        void Tick(float t)
        {
            if (_sr == null) return;

            // Ride on top of a live animation: whatever sprite is on the renderer right now that ISN'T our own
            // filtered sprite is the fresh source frame (a Lauminary/Animator wrote it this frame). If the renderer still
            // shows our filtered sprite, the underlying source didn't change — reuse the last one (handles a held
            // frame / paused animation without double-filtering our own output).
            Sprite cur = _sr.sprite;
            if (cur != null && !ReferenceEquals(cur, _filteredSprite)) _sourceSprite = cur;
            Sprite src = _sourceSprite;
            if (src == null || src.texture == null) return;

            // Pass-local time: a loop repeats whole passes back-to-back; a reversed pass runs back-to-front.
            float tp = _loopFor > 0f ? Mathf.Repeat(t, _dur) : Mathf.Min(t, _dur);
            if (_reversed) tp = _dur - tp;

            float life;
            int frame;
            float fps = EffectiveTargetFps;
            if (fps > 0f)
            {
                // OWN CLOCK: the stack's time advances on a fixed 1/fps step grid instead of every rendered
                // frame, so the effect ticks at ITS rate independent of the animation it rides (a 12-step
                // flicker over a 4-fps lauminary). The step index feeds the hashing modifiers — a dither/dissolve
                // pattern re-rolls per STEP — and it is GLOBAL across loop passes so a looped flicker doesn't
                // repeat in lockstep. An unchanged step over an unchanged source frame skips the pixel pass
                // entirely (the own clock genuinely ticks slower, it isn't just quantised output).
                int step = Mathf.FloorToInt(t * fps);
                if (step == _lastStep && ReferenceEquals(src, _lastStepSrc) &&
                    _filteredSprite != null && ReferenceEquals(_sr.sprite, _filteredSprite))
                    return;
                _lastStep = step;
                _lastStepSrc = src;
                float tq = Mathf.Floor(tp * fps) / fps;
                float p = _dur > 0f ? Mathf.Clamp01(tq / _dur) : 1f;
                life = SampleEnvelope(p);
                frame = step;
            }
            else
            {
                float p = _dur > 0f ? Mathf.Clamp01(tp / _dur) : 1f;
                life = SampleEnvelope(p);
                frame = _tickFrame;
            }

            if (!ReadSource(src, out Color32[] pixels, out int W, out int H)) return;

            FeedExternalPositions();

            int pad = EffectiveReach;
            if (pad <= 0)
            {
                // The common case: nothing in the stack draws outside the picture, so there is no second
                // buffer, no companion, and not one byte of extra work.
                SpriteFxStack.RunStack(pixels, W, H, EffectiveModifiers, frame, life, EffectiveSeed, ResolveUseBurst());
                _tickFrame++;
                WriteToRenderer(src, pixels, W, H);
                return;
            }

            // OVERFLOW PATH. Render into a buffer with a transparent margin, then SPLIT it: the picture's own
            // rect goes back to the character (identical geometry, so its bounds/collider/hit-test are
            // untouched), and everything outside goes to a companion renderer. Nothing is discarded — the
            // pixels an outline or a glow puts past the silhouette simply live on a different object.
            int pw = W + pad * 2, ph = H + pad * 2;
            EnsurePadBuffer(pw * ph);
            System.Array.Clear(_padded, 0, _padded.Length);
            for (int y = 0; y < H; y++)
                System.Array.Copy(pixels, y * W, _padded, (y + pad) * pw + pad, W);

            // The picture stays srcW×srcH at (pad,pad), so every effect still normalizes against the SPRITE
            // and not against the margin — otherwise every authored mask would shift the moment one appeared.
            SpriteFxStack.RunStack(_padded, pw, ph, W, H, pad, pad,
                                   EffectiveModifiers, frame, life, EffectiveSeed, ResolveUseBurst());
            _tickFrame++;

            // Centre back to the character, unchanged in size.
            for (int y = 0; y < H; y++)
                System.Array.Copy(_padded, (y + pad) * pw + pad, pixels, y * W, W);
            WriteToRenderer(src, pixels, W, H);

            // The margin — the same buffer with the character's own rect punched out, so the two never
            // double-draw the middle and the companion carries ONLY what fell outside.
            for (int y = 0; y < H; y++)
                System.Array.Clear(_padded, (y + pad) * pw + pad, W);
            WriteOverflow(src, _padded, pw, ph, pad);
        }

        // Paint the filtered picture onto the character's own renderer, at exactly its original geometry.
        void WriteToRenderer(Sprite src, Color32[] pixels, int W, int H)
        {
            EnsureWork(src, W, H);
            _work.SetPixels32(pixels);
            _work.Apply(false);
            EnsureFilteredSprite(src, W, H);
            _sr.sprite = _filteredSprite;
        }

        // ── the overflow companion ────────────────────────────────────────────────────────────────────────────
        // An outline, a glow, a drop shadow all draw OUTSIDE the silhouette, and a tightly-cropped sprite has
        // no room for that. Padding the character's own sprite would be the obvious fix and is the wrong one:
        // its rect, pivot and bounds are read by colliders (ZoeSpawner sizes one from sprite.bounds) and by
        // hit-testing (SpriteAlphaHitFilter), so the silhouette would start catching shots on its own glow.
        //
        // So the character is left exactly as it is and the overflow goes on a companion renderer — a child
        // object holding only the pixels that fell outside. It carries no collider and nothing reads it, so it
        // cannot change how the character behaves; it is pure decoration, created only when a stack actually
        // reaches outward and destroyed the moment the effect stops.
        [Tooltip("Sorting-order offset for the overflow layer relative to the character. Negative (the default) " +
                 "puts glows, shadows and outlines BEHIND it; positive brings them in front.")]
        public int overflowSortingOffset = -1;

        Color32[] _padded;
        GameObject _overflowGo;
        SpriteRenderer _overflowSr;
        Texture2D _overflowTex;
        Sprite _overflowSprite;
        int _overflowW = -1, _overflowH = -1;
        Vector2 _overflowPivot;
        float _overflowPpu;

        int _reach = -1;                 // cached; -1 = not yet computed for the current stack
        object _reachStackKey;           // what the cache was computed for

        /// How far this stack draws past the picture, computed ONCE per stack rather than per frame.
        ///
        /// It has to be a play-through PEAK (an animated outline width would otherwise size the buffer from
        /// frame one and clip its own growth), and a peak is invariant — so recomputing it every tick would
        /// only ever churn a Texture2D and a Sprite for an answer that never changed.
        int EffectiveReach
        {
            get
            {
                object key = stack != null ? (object)stack : modifiers;
                if (_reach < 0 || !ReferenceEquals(key, _reachStackKey))
                {
                    _reach = SpriteFxStack.OutwardReach(EffectiveModifiers);
                    _reachStackKey = key;
                }
                return _reach;
            }
        }

        /// Drop the cached reach — call after changing the stack at runtime.
        public void InvalidateReach() => _reach = -1;

        // EXACTLY len, never "at least len". Texture2D.SetPixels32 demands an array the exact size of the
        // texture, so a grow-only buffer throws the moment a frame with a different rect comes through and
        // the oversized one gets handed to it — which is every tick from then on, not a one-off.
        // Lauminary frames are uniform within a clip, so this reallocates only when the geometry genuinely
        // changes, and that already forces a texture and sprite rebuild anyway.
        void EnsurePadBuffer(int len)
        {
            if (_padded == null || _padded.Length != len) _padded = new Color32[len];
        }

        void WriteOverflow(Sprite src, Color32[] px, int W, int H, int pad)
        {
            if (_overflowGo == null)
            {
                _overflowGo = new GameObject(name + " (SpriteFx overflow)");
                _overflowGo.transform.SetParent(transform, worldPositionStays: false);
                _overflowGo.transform.localPosition = Vector3.zero;
                _overflowGo.transform.localRotation = Quaternion.identity;
                _overflowGo.transform.localScale = Vector3.one;
                // DontSave, not HideAndDontSave: it must never persist into a saved scene, but it SHOULD be
                // visible in the Hierarchy — it is the only way to see that the overflow layer exists, is
                // parented correctly and is being torn down. Same choice MirageRig makes for its realized
                // previewables, and for the same reason.
                _overflowGo.hideFlags = HideFlags.DontSave;
                _overflowSr = _overflowGo.AddComponent<SpriteRenderer>();
            }

            // Follow the character's own sorting so the overflow travels with it through any layer change.
            _overflowSr.sortingLayerID = _sr.sortingLayerID;
            _overflowSr.sortingOrder = _sr.sortingOrder + overflowSortingOffset;
            _overflowSr.color = _sr.color;
            _overflowSr.enabled = true;

            if (_overflowTex == null || _overflowTex.width != W || _overflowTex.height != H)
            {
                if (_overflowTex != null) SafeDestroy(_overflowTex);
                _overflowTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
                {
                    filterMode = src.texture != null ? src.texture.filterMode : FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _overflowW = _overflowH = -1;   // force the sprite to rebuild against the new texture
            }
            _overflowTex.SetPixels32(px);
            _overflowTex.Apply(false);

            // The pivot shifts by exactly the margin, so the bigger picture still lines up pixel-for-pixel
            // with the character underneath it: artwork pixel (i,j) sits at (i+pad, j+pad) and the pivot moves
            // with it, which is what makes this alignment exact rather than approximately right.
            Vector2 srcPivotPx = src.pivot;
            var pivotNorm = new Vector2((srcPivotPx.x + pad) / W, (srcPivotPx.y + pad) / H);
            if (_overflowSprite == null || _overflowW != W || _overflowH != H ||
                _overflowPivot != pivotNorm || _overflowPpu != src.pixelsPerUnit)
            {
                if (_overflowSprite != null) SafeDestroy(_overflowSprite);
                _overflowSprite = Sprite.Create(_overflowTex, new Rect(0, 0, W, H), pivotNorm,
                                                src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                _overflowSprite.name = src.name + " (SpriteFx overflow)";
                _overflowW = W; _overflowH = H; _overflowPivot = pivotNorm; _overflowPpu = src.pixelsPerUnit;
            }
            _overflowSr.sprite = _overflowSprite;
        }

        void HideOverflow()
        {
            if (_overflowSr != null) { _overflowSr.sprite = null; _overflowSr.enabled = false; }
        }

        void DestroyOverflow()
        {
            HideOverflow();
            if (_overflowSprite != null) { SafeDestroy(_overflowSprite); _overflowSprite = null; }
            if (_overflowTex != null) { SafeDestroy(_overflowTex); _overflowTex = null; }
            if (_overflowGo != null) { SafeDestroy(_overflowGo); _overflowGo = null; _overflowSr = null; }
            _overflowW = _overflowH = -1;
        }

        // Feed each IExternalPosition2D modifier in the CURRENT stack its live position for THIS tick, fresh —
        // never once-and-cached, because the stack may be a SHARED asset (EffectiveModifiers over `stack`) also
        // playing on other characters right now. Safe despite the sharing: this runs synchronously right before
        // RunStack below consumes it (Prepare reads it, Apply reads what Prepare wrote), on the same thread, in
        // the same call — nothing else touches these modifiers between here and RunStack returning.
        void FeedExternalPositions()
        {
            var mods = EffectiveModifiers;
            if (mods == null) return;
            for (int i = 0; i < mods.Count; i++)
            {
                if (!(mods[i] is IExternalPosition2D ext) || !ext.WantsExternalPosition) continue;
                if (externalPositionResolver != null &&
                    externalPositionResolver.TryResolve(ext.ExternalPositionKey, out var pos))
                    ext.SetExternalPosition(pos);
                else
                    ext.ClearExternalPosition();
            }
        }

        // ── effective source (a Stack asset, when assigned, overrides every inline field) ─────────────────────────
        List<PyreModifier> EffectiveModifiers => stack != null ? stack.modifiers : modifiers;
        float EffectiveDuration => stack != null ? Mathf.Max(0.001f, stack.duration) : duration;
        int EffectiveSeed => stack != null ? stack.seed : seed;

        // A stack owns no clock of its own — see SpriteFxSpec.targetFps for why the own-clock grid went.
        float EffectiveTargetFps => 0f;

        // Life IS progress. See SpriteFxSpec.SampleEnvelope for why the stack-wide remap went: it could pin
        // life to a constant and silently flatten every authored envelope in the stack at once.
        float SampleEnvelope(float progress01) => Mathf.Clamp01(progress01);

        bool ResolveUseBurst() => dispatch switch
        {
            SfxDispatch.ForceInline => false,
            SfxDispatch.ForceBurst => true,
            _ => SpriteFxSettings.UseBurstJobs
        };

        void Restore()
        {
            // Only restore if WE are still the one on the renderer — never stomp a frame a Lauminary/Animator advanced to.
            if (_sr != null && _sourceSprite != null && ReferenceEquals(_sr.sprite, _filteredSprite))
                _sr.sprite = _sourceSprite;
            // The overflow is only ever valid for a frame the effect drew. Leaving it up would strand a glow
            // around a character that has stopped glowing.
            HideOverflow();
        }

        // ── core apply — the SAME routine Tick and the edit-mode verification both call ──────────────────────────
        /// Apply a shaped modifier stack to a Color32 buffer in place, dispatching inline or to the Burst job. This
        /// is the single code path <see cref="Tick"/> uses each frame; the verification harness calls it directly so
        /// "what a probe tests" == "what plays". `mods` may contain any PixelModifiers — non-shaped/disabled ones
        /// are skipped by <see cref="SpriteFxStack.Resolve"/> (a runtime filter only handles the gather-free family).
        public static void Apply(Color32[] pixels, int W, int H, IReadOnlyList<PyreModifier> mods,
                                 int frame, float life, int seed, bool useBurst)
            => SpriteFxStack.RunStack(pixels, W, H, mods, frame, life, seed, useBurst);

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
            _hasPending = false;   // a disabled filter fires nothing later — a stale schedule must not survive
            if (_playing) { _playing = false; Restore(); }
        }

        void OnDestroy()
        {
            if (_work != null) SafeDestroy(_work);
            if (_filteredSprite != null) SafeDestroy(_filteredSprite);
            DestroyOverflow();
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
