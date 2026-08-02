using System.Collections.Generic;
using FOW;
using UnityEngine;

namespace Laubrary.Fov
{
    /// Fog-of-war visibility for spawned ACTORS: outside a revealer's lit area their renderers are off;
    /// revealed, the rig shows exactly what its own animation wanted. The composition follows the
    /// asset's intended pattern (FogOfWarHider + a HiderBehavior + sample points), built at runtime
    /// because spawned actors usually have no master prefab to author it on. Sample points are the
    /// visual centre plus the four corners of the visual bounds, and the asset counts a hider seen as
    /// soon as ANY sample point is visible (FogOfWarRevealer2D.CanSeeHider returns on the first
    /// visible point) — so an actor grazed by the cone edge shows, with the fullscreen fog pass
    /// dimming its unlit pixels, instead of popping in only once its centre is lit.
    ///
    /// Because HidersUseFogTexture is off in the FovRoomWorld recipe (a safety invariant — see there),
    /// the hide/reveal decision is the revealer's LIVE line-of-sight geometry: no memory floor or
    /// reveal stamp can ever leak a hidden actor.
    public static class FovActorHide
    {
        /// Wire `actor` to hide outside lit fog. No-ops without a live fog world — fog hiding is room
        /// dressing, not a dependency. Meant for actors the player must FIND (enemies, pickups): never
        /// attach it to the revealer's own carrier — a hidden player in a dark room is a softlock, not
        /// a mechanic.
        public static void Attach(GameObject actor)
        {
            if (actor == null || FogOfWarWorld.instance == null) return;
            if (actor.GetComponent<FovActorHideAttacher>() != null ||
                actor.GetComponentInChildren<FogOfWarHider>() != null) return;
            actor.AddComponent<FovActorHideAttacher>();
        }
    }

    /// Waits until the rig actually SHOWS something — an animated view can build its renderers a frame
    /// or more after spawn — then wires the hider with sample points at the visual centre and the
    /// visual bounds' corners, and removes itself. LateUpdate on purpose: the wire lands after the
    /// frame's animation pushed sprites but before anything renders, so there is no one-frame flash
    /// at spawn.
    [AddComponentMenu("")]
    public class FovActorHideAttacher : MonoBehaviour
    {
        const float GiveUpSeconds = 3f;   // a rig that never grew a visible renderer has nothing to hide
        float waited;

        void LateUpdate()
        {
            var bounds = default(Bounds);
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is SpriteRenderer sr && sr.sprite == null) continue;   // exists but shows nothing yet
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!any)
            {
                waited += Time.deltaTime;
                if (waited > GiveUpSeconds) Destroy(this);
                return;
            }

            // The hider rides an INACTIVE child while it is configured. Unity runs a component's
            // OnEnable inside AddComponent itself, and a play-mode AddComponent leaves serialized fields
            // at plain CLR defaults — so adding FogOfWarHider to a live GameObject fires its OnEnable
            // with SamplePoints still NULL, and CalculateSamplePointData throws (the NRE that used to
            // log on every wave spawn). Deferring activation until the array is assigned means the
            // hider's FIRST OnEnable already sees valid data and registers exactly once.
            //
            // The child doubles as the FIRST sample point, placed at the visual centre: the actor's
            // root sits at the feet, and a feet-only sample would light the body later than a light
            // visibly reaches it.
            var rig = new GameObject("FovHider");
            rig.SetActive(false);
            rig.transform.SetParent(transform, false);
            rig.transform.position = bounds.center;

            // Centre + the four corners of the visual bounds: the asset treats a hider as seen when
            // ANY sample point is visible, so any part of the sprite entering light reveals the actor
            // — the cone edge partially covers it instead of popping it in whole. SamplePoints[0]
            // stays the centre on purpose: the asset measures its search-radius expansion and the
            // spatial-hash position from the first entry. The corners ride as children of the hider
            // child, so they track the actor for free.
            var points = new Transform[5];
            points[0] = rig.transform;
            for (int i = 0; i < 4; i++)
            {
                var corner = new GameObject("SamplePoint" + i).transform;
                corner.SetParent(rig.transform, false);
                corner.position = new Vector3(i % 2 == 0 ? bounds.min.x : bounds.max.x,
                                              i < 2 ? bounds.min.y : bounds.max.y,
                                              bounds.center.z);
                points[i + 1] = corner;
            }

            var hider = rig.AddComponent<FogOfWarHider>();
            hider.SamplePoints = points;

            var renderers = rig.AddComponent<FovHideRenderers>();
            renderers.root = transform;   // sweep the ACTOR's rig, not the hider child's

            rig.SetActive(true);   // Awake hides the rig immediately; the hider registers with valid data
            Destroy(this);
        }
    }

    /// The HiderBehavior for ANIMATED sprite rigs. A stock one-shot renderer toggle loses to any
    /// animation player that re-enables its renderers every tick — a one-shot hide is undone a frame
    /// later. This behavior re-asserts the hide each LateUpdate (after every Update, before rendering)
    /// and records what the rig tried to show, so a reveal restores exactly the animation's intent —
    /// including renderers born while hidden (layer renderers, muzzle flashes).
    [AddComponentMenu("")]
    public class FovHideRenderers : HiderBehavior
    {
        [Tooltip("Root of the renderer rig to hide — the actor, not the hider child this sits on.")]
        public Transform root;

        readonly Dictionary<Renderer, bool> wanted = new();
        bool hidden;

        protected override void OnHide() { hidden = true; Sweep(); }

        protected override void OnReveal()
        {
            hidden = false;
            foreach (var kv in wanted)
                if (kv.Key != null && kv.Value) kv.Key.enabled = true;
            wanted.Clear();
        }

        void LateUpdate()
        {
            if (!hidden) return;

            // Debug X-ray: while RevealAll is on, show what the animation wanted instead of sweeping.
            // `hidden` stays true and `wanted` keeps its records, so normal hiding resumes on the first
            // frame after the toggle drops.
            if (FovDebug.RevealAll)
            {
                foreach (var kv in wanted)
                    if (kv.Key != null && kv.Value) kv.Key.enabled = true;
                return;
            }

            Sweep();
        }

        /// Disable everything that is (or this frame tried to become) visible, remembering it for the reveal.
        void Sweep()
        {
            var sweepRoot = root != null ? root : transform;
            foreach (var r in sweepRoot.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                wanted[r] = true;
                r.enabled = false;
            }
        }
    }
}
