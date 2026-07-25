using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// A thin demonstration of <see cref="SpriteFxFilter"/>: a brief brightness (+ optional tint) PULSE, the classic
    /// "sprite flashes on hit". Call <see cref="Flash()"/> and it configures the sibling filter's stack to a pulse
    /// and triggers it — the sprite ramps toward the flash colour and back over <see cref="flashDuration"/>, riding
    /// on top of whatever Reel/Animator frame is playing.
    ///
    /// HOOK: this is DELIBERATELY not auto-wired to any damage event. The SpriteFx runtime module sits below
    /// Zoetrope/combat in the dependency graph, so it must not reference them — coupling the low-level filter to a
    /// specific Health/ReactionFxPlayer would invert the layering. Instead <see cref="Flash()"/> is a public,
    /// argument-less method: drop it into a UnityEvent in the inspector, or call it from your own glue, e.g.
    ///   <c>health.Damaged += (_, __) => hurtFlash.Flash();</c>  (Zoetrope's Health.Damaged / ReactionFxPlayer.OnHit),
    /// keeping the dependency pointing the right way (your combat code → this helper).
    [RequireComponent(typeof(SpriteFxFilter))]
    [AddComponentMenu("Laubrary/SpriteFx/Sprite Fx Hurt Flash")]
    public class SpriteFxHurtFlash : MonoBehaviour
    {
        [Tooltip("How long one flash lasts, in seconds. Short (a fraction of a second) reads as a hit blink.")]
        [Min(0.01f)] public float flashDuration = 0.15f;

        [Tooltip("Peak brightness multiply at the middle of the flash. 1 = no change; higher blows the sprite out " +
                 "toward white (the classic hit flash). RGB ramps 1 → peak → 1 over the flash.")]
        [Min(1f)] public float peakBrightness = 4f;

        [Tooltip("Multiply tint held for the flash (white = none). Set it red/cyan/etc. for a coloured hit wash — " +
                 "combined with Peak brightness that reads as a bright coloured blink.")]
        public Color flashTint = Color.white;

        SpriteFxFilter _filter;

        void Awake() => _filter = GetComponent<SpriteFxFilter>();

        /// Fire a hurt flash for the configured <see cref="flashDuration"/>.
        public void Flash() => Flash(flashDuration);

        /// Fire a hurt flash for a specific duration (seconds).
        public void Flash(float duration)
        {
            if (_filter == null) _filter = GetComponent<SpriteFxFilter>();
            if (_filter == null) return;

            _filter.modifiers = BuildStack();
            // The pulse shape lives in the Brightness curve itself, so leave the filter's envelope as identity
            // (life == progress). Author a custom filter.envelope only if you want to further ease the ends.
            _filter.envelope = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            _filter.Play(Mathf.Max(0.01f, duration));
        }

        List<PixelModifier> BuildStack()
        {
            var stack = new List<PixelModifier>(2);

            // A brightness value that curves 1 → peak → 1 across life, i.e. the pulse. yMax = peak bounds the curve.
            var brightness = new BrightnessModifier
            {
                amount = Sfx.CurveVal(Mathf.Max(1f, peakBrightness),
                                      0f, 1f, 0.5f, Mathf.Max(1f, peakBrightness), 1f, 1f)
            };
            stack.Add(brightness);

            // A held multiply tint (skipped when white, since ×white is a no-op — keeps the stack minimal).
            // crossAmount 0 = a pure flat multiply, no cross-gradient/LUT work.
            if (flashTint != Color.white)
                stack.Add(new TintModifier { tint = flashTint, crossAmount = new ZUIValue(0f) });

            return stack;
        }
    }
}
