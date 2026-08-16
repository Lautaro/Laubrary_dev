using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Keeps a weapon's muzzle transform on the point the LAUMINATION says the shot leaves from.
    ///
    /// <para><b>Position is a separate question from timing, and the laumination owns it.</b> The laumination IS
    /// the weapon's body, so it says where the bullet leaves; WHEN the shot happens is the game's business. The
    /// arrangement this replaces conflated the two — the painted MetaLayer TRIGGERED the shot — so if nobody
    /// painted the muzzle layer the gun silently did not fire at all: a presentation omission breaking
    /// gameplay.</para>
    ///
    /// <para>Resolution chain, every frame: the layer's point on the CURRENT frame → the nearest frame that does
    /// have it painted → leave the muzzle where the weapon's own offset put it. So an unpainted frame costs a
    /// little accuracy and nothing else, and the offset is a last resort rather than the normal path.</para>
    ///
    /// <para>Talks to <see cref="IAnimatedView"/>, not to any animation system: a view that has meta-layers
    /// provides the point, and a plain sprite view simply never resolves one, leaving the offset in place.
    /// LateUpdate, so it reads the frame playback has already advanced to this tick.</para>
    /// </summary>
    [AddComponentMenu("")]
    public class MuzzleTracker : MonoBehaviour
    {
        [Tooltip("The transform a weapon spawns its shots from — moved to the painted point each frame.")]
        public Transform muzzle;
        [Tooltip("Which painted MetaLayer on the body's laumination marks where the shot leaves.")]
        public string layerId = "Muzzle";

        IAnimatedView _view;
        Vector3 _restLocalPos;
        bool _hasRest;

        /// Wire it up right after AddComponent — OnEnable has already run by the time a caller could assign
        /// fields the normal way (the same trap <see cref="WeaponMuzzleCue.Configure"/> exists for).
        public void Configure(Transform muzzle, string layerId)
        {
            this.muzzle = muzzle;
            this.layerId = string.IsNullOrEmpty(layerId) ? "Muzzle" : layerId;
            CaptureRest();
        }

        void OnEnable() => CaptureRest();

        // Where the weapon's own offset put the muzzle. Kept so an unpainted laumination RESTORES it rather
        // than leaving the muzzle stuck wherever the last painted frame happened to be.
        void CaptureRest()
        {
            if (muzzle == null || _hasRest) return;
            _restLocalPos = muzzle.localPosition;
            _hasRest = true;
        }

        void LateUpdate()
        {
            if (muzzle == null) return;
            // Resolved lazily and re-resolved while missing: the view lives on the spawned body, which the
            // spawner may add AFTER the weapon slots (and a Mirage rebuild replaces it wholesale).
            if (_view == null) _view = GetComponentInParent<IAnimatedView>();
            if (_view == null) return;

            if (_view.TryGetMetaPointNearest(layerId, out var world)) muzzle.position = world;
            else if (_hasRest) muzzle.localPosition = _restLocalPos;
        }
    }
}
