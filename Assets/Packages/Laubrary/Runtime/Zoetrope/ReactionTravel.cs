using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// How far a move carries its character, as a curve over the move's whole length: where the body is,
    /// relative to where the move started, at each moment. Forward follows the body's facing, so one curve
    /// works facing either way. Authored as an envelope in the Zoe window, under a timeline of the move.
    ///
    /// The curve's height is a fraction of <see cref="maxDistancePx"/>: 0..1 when unipolar (a negative maximum
    /// makes it a backwards move), -1..1 when bipolar (a move that goes back, then forward). Distances are in
    /// the art's own pixels, which is how a pixel animator judges a step.
    /// </summary>
    [System.Serializable]
    public class ReactionTravel
    {
        [Tooltip("Where the body is over the move: across = the move's whole length, up = how much of Max " +
                 "distance it has travelled at that moment.")]
        public List<ZUIEnvelopePoint> envelope = new List<ZUIEnvelopePoint>();

        [Tooltip("The distance, in the art's pixels, that the top of the envelope stands for. Scales the whole " +
                 "move without redrawing it. Negative = backwards.")]
        public float maxDistancePx = 0f;

        [Tooltip("Unipolar: the envelope runs from 0 to Max distance. Bipolar: from minus to plus Max distance, " +
                 "for a move that goes one way and then the other.")]
        public bool bipolar = false;

        /// Anything to apply: a curve, and a distance for it to mean.
        public bool IsAuthored => envelope != null && envelope.Count > 0 && !Mathf.Approximately(maxDistancePx, 0f);

        /// Offset from the start, in art pixels, at <paramref name="t01"/> of the move's length (0..1).
        public float OffsetPx(float t01)
        {
            if (envelope == null || envelope.Count == 0) return 0f;
            float v = ZUIEnvelopeEvaluator.Evaluate(envelope, Mathf.Clamp01(t01), 0f);
            v = bipolar ? Mathf.Clamp(v, -1f, 1f) : Mathf.Clamp01(v);
            return v * maxDistancePx;
        }

        /// Where the move ends up, in art pixels.
        public float EndPx => OffsetPx(1f);
    }

    /// <summary>
    /// Applies one move's <see cref="ReactionTravel"/> while that move plays: each frame the body moves by
    /// the change in the curve since the last frame, along the facing it had when the move began. Moves the
    /// transform directly, like a knockback, so it carries the body even while walking is held still.
    /// Started and stopped by <see cref="ReactionFxPlayer"/>; a new move replaces the old one's travel.
    /// </summary>
    public class ReactionTravelMotion : MonoBehaviour
    {
        ReactionTravel _travel;
        float _seconds, _elapsed, _appliedPx, _worldPerPx;
        Vector3 _forward;
        int _token;

        /// The move currently travelling, or 0 when none.
        public int Token => _travel != null ? _token : 0;

        public void Begin(ReactionTravel travel, float seconds, Vector2 forward, float worldPerPx, int token)
        {
            _travel = travel;
            _seconds = Mathf.Max(0.0001f, seconds);
            _elapsed = 0f;
            _appliedPx = 0f;
            _forward = forward.normalized;
            _worldPerPx = worldPerPx;
            _token = token;
        }

        /// Stop the move with this token (another move, or an interrupt, has taken over). The distance already
        /// travelled stays; only the rest of the curve is dropped.
        public void Stop(int token) { if (_travel != null && token == _token) _travel = null; }

        void LateUpdate()
        {
            if (_travel == null) return;
            _elapsed += Time.deltaTime;
            float t01 = _elapsed / _seconds;
            float targetPx = _travel.OffsetPx(t01);
            transform.position += _forward * ((targetPx - _appliedPx) * _worldPerPx);
            _appliedPx = targetPx;
            if (t01 >= 1f) _travel = null;
        }
    }
}
