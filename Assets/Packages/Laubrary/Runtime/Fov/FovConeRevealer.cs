using System.Reflection;
using FOW;
using UnityEngine;

namespace Laubrary.Fov
{
    /// The aim-following flashlight cone: a FogOfWarRevealer2D on a child GameObject, steered by
    /// rotating that child — the asset reads transform.up as the cone's centre line. Attach() builds the
    /// child on a carrier (typically the player); the game then steers it with SetAimDirection every
    /// frame, or assigns `follow` to mirror another transform's facing instead.
    [AddComponentMenu("")]
    public class FovConeRevealer : MonoBehaviour
    {
        [Tooltip("Optional transform-follow mode: mirror this transform's up vector as the cone " +
                 "direction every frame instead of being fed SetAimDirection calls. Leave empty when " +
                 "the game steers the cone itself.")]
        public Transform follow;

        FogOfWarRevealer2D revealer;

        /// Total cone angle, degrees. Forwards to the asset, which re-pushes its revealer data on write.
        public float ViewAngle
        {
            get => revealer != null ? revealer.ViewAngle : 0f;
            set { if (revealer != null) revealer.ViewAngle = value; }
        }

        /// How far the cone sees, world units (the asset's ViewRadius). Settable live every frame just
        /// like ViewAngle — expanding-cone reveals, height bonuses, powerups; the asset re-pushes its
        /// revealer data on write.
        public float ViewRadius
        {
            get => revealer != null ? revealer.ViewRadius : 0f;
            set { if (revealer != null) revealer.ViewRadius = value; }
        }

        /// Radius revealed in EVERY direction regardless of the cone — body-glow, so the carrier is
        /// never blind at its own feet.
        public float UnobscuredRadius
        {
            get => revealer != null ? revealer.UnobscuredRadius : 0f;
            set { if (revealer != null) revealer.UnobscuredRadius = value; }
        }

        /// Physics layers that block sight. ObstacleMask is serialized-protected on the asset with no
        /// public accessor (it expects Inspector authoring), so a code-built revealer reaches it by name.
        public LayerMask ObstacleMask
        {
            get => revealer != null ? (LayerMask)ObstacleMaskField.GetValue(revealer) : default;
            set { if (revealer != null) ObstacleMaskField.SetValue(revealer, value); }
        }

        static FieldInfo obstacleMaskField;
        static FieldInfo ObstacleMaskField => obstacleMaskField ??= typeof(RaycastRevealer)
            .GetField("ObstacleMask", BindingFlags.Instance | BindingFlags.NonPublic);

        /// Own-occluder exclusion — the investigated verdict, and the supported mechanism. The asset's
        /// sight rays are plain LayerMask physics queries fired FROM the eye (RaycastRevealer's
        /// ObstacleMask straight into PhysicsScene2D.Raycast): there is no per-collider exclusion hook,
        /// and rays do NOT start outside UnobscuredRadius — so a carrier whose own collider sits on an
        /// obstacle layer blinds its own revealer. True per-collider exclusion would need an asset
        /// patch, which we don't do. The supported mechanism is per-REVEALER mask surgery, layer-
        /// granular: put the owner's sight-blocking collider on its own physics layer and strip that
        /// layer from THIS revealer's ObstacleMask — every other revealer keeps blocking on it. The
        /// grain is the layer, so give self-blocking things a dedicated layer per owner CATEGORY
        /// (own-buildings, own-turrets), not per instance. Takes effect on the next sight update — the
        /// mask is read fresh at every raycast, no re-registration needed.
        ///
        /// Alternative when the eye genuinely sits INSIDE the blocking collider: turning OFF Project
        /// Settings ▸ Physics 2D ▸ "Queries Start In Colliders" makes every 2D query skip colliders it
        /// starts inside — exactly the own-building case — but that is a project-wide physics behaviour
        /// change (gameplay raycasts included), so it is documented here rather than set by the module.
        public void IgnoreObstacleLayer(int layer) => ObstacleMask &= ~(1 << layer);

        /// Convenience for IgnoreObstacleLayer: strip the layer `ownOccluder`'s GameObject lives on.
        /// Layer-granular — see IgnoreObstacleLayer for exactly what that means.
        public void IgnoreObstacleLayerOf(Component ownOccluder)
        {
            if (ownOccluder != null) IgnoreObstacleLayer(ownOccluder.gameObject.layer);
        }

        /// Build the cone as a child of `parent`. Built INACTIVE so every value is in place before the
        /// revealer's OnEnable registers it with the fog world. A `viewRadius` of 0 or less keeps the
        /// asset's serialized default; either way it stays live-settable through ViewRadius.
        public static FovConeRevealer Attach(Transform parent, float viewAngle, float unobscuredRadius,
                                             LayerMask obstacleMask, float viewRadius = 0f)
        {
            var eye = new GameObject("FovEye");
            eye.SetActive(false);
            eye.transform.SetParent(parent, false);
            var revealer = eye.AddComponent<FogOfWarRevealer2D>();
            revealer.ViewAngle = viewAngle;
            revealer.UnobscuredRadius = unobscuredRadius;
            if (viewRadius > 0f) revealer.ViewRadius = viewRadius;
            ObstacleMaskField.SetValue(revealer, obstacleMask);
            var cone = eye.AddComponent<FovConeRevealer>();
            cone.revealer = revealer;
            eye.SetActive(true);
            return cone;
        }

        void Awake()
        {
            // A scene-authored cone (not built through Attach) still finds its revealer.
            if (revealer == null) revealer = GetComponent<FogOfWarRevealer2D>();
        }

        /// Point the cone's centre line along a world direction. The steering call a game makes each
        /// frame — feeding aim input, a facing, whatever "where the light looks" means there.
        public void SetAimDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f) transform.up = direction;
        }

        void LateUpdate()
        {
            if (follow != null) transform.up = follow.up;
        }
    }
}
