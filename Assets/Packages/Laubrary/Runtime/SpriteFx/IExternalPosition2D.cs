// IExternalPosition2D.cs
// The registration seam that lets a SpriteFx modifier take its POSITION from something outside SpriteFx entirely
// (a Zoetrope MetaLayer, say) without SpriteFx ever knowing what that something is — same shape as
// ISpriteFxPreviewOverlay's seam (SpriteFxPreviewOverlay.cs), applied to a live per-tick value instead of a
// preview-only diagnostic mark.
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// <summary>
    /// Opt-in capability on a modifier: "one of my authored fields is a POSITION, and I can be handed a live
    /// value for it instead of evaluating my own painted curve." Implemented by <see cref="RelightModifier"/>
    /// for its point-light position; nothing about this interface is light-specific, so a future modifier with
    /// its own live-positionable field (a warp pivot, a mask origin) implements it the same way.
    ///
    /// SpriteFx itself never decides WHERE the value comes from — see <see cref="IExternalPositionResolver"/>,
    /// which a host module outside SpriteFx (Zoetrope, say) supplies to <see cref="SpriteFxFilter"/>.
    /// </summary>
    public interface IExternalPosition2D
    {
        /// A caller-defined key identifying WHICH external source this instance wants (e.g. a MetaLayer id).
        /// Meaningless to SpriteFx itself — purely a lookup key handed to an <see cref="IExternalPositionResolver"/>.
        string ExternalPositionKey { get; }

        /// Whether THIS instance is currently configured to want an external position at all — the instance
        /// decides, same "who checks configuration" rule as <see cref="ISpriteFxPreviewOverlay.WantsPreviewOverlay"/>.
        bool WantsExternalPosition { get; }

        /// Feed this tick's live position, in the modifier's OWN local coordinate space (for
        /// <see cref="RelightModifier"/>: half-frame units, sprite-centre-relative — the same space
        /// <c>lightX</c>/<c>lightY</c> already use). Must be called fresh EVERY tick a live value is wanted —
        /// the modifier does not cache it across ticks on its own, so a resolver that stops calling this simply
        /// stops overriding, it does not leave a stale position behind.
        void SetExternalPosition(Vector2 localPosition);

        /// No live value available this tick (the source has nothing right now) — falls back to this
        /// instance's own painted/default behaviour for the tick.
        void ClearExternalPosition();
    }

    /// <summary>
    /// Supplied by a host module OUTSIDE SpriteFx (e.g. Zoetrope's <c>ReactionFxPlayer</c>) to a
    /// <see cref="SpriteFxFilter"/> instance, so that instance's per-tick <see cref="IExternalPosition2D"/>
    /// modifiers get fed real data without SpriteFx depending on whatever that data means.
    /// </summary>
    public interface IExternalPositionResolver
    {
        /// Resolve the current live position for <paramref name="key"/>, in the CONSUMING modifier's own local
        /// coordinate space (see <see cref="IExternalPosition2D.SetExternalPosition"/>). Returns false when
        /// there is nothing to report right now (an unpainted frame, a view with no such layer) — the caller
        /// clears rather than leaving a stale value.
        bool TryResolve(string key, out Vector2 localPosition);
    }
}
