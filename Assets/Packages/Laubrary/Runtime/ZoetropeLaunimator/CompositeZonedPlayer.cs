using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// Owns N independently-timed named body parts for a composite <see cref="Zoe"/> — one child GameObject
    /// per <see cref="ZoeBodyPart"/>, built through that part's own pluggable <see cref="ICharacterView"/> (the
    /// SAME mechanism a simple Zoe's single <c>view</c> already uses, just once per part instead of once for
    /// the whole body). A non-root part attaches to its parent every frame via one <see cref="AttachAnchor"/>
    /// per side (<see cref="ZoeBodyPart.parentAnchor"/>/<see cref="ZoeBodyPart.childAnchor"/>) — each side
    /// independently either computed from that part's current sprite bounds (<see cref="AttachAnchorMode.Edge"/>,
    /// no authoring needed) or read from a named painted point (<see cref="AttachAnchorMode.MetaLayer"/>), so
    /// two independently-authored sprite sheets that don't share a registration origin still line up: the
    /// CHILD's own anchor point is pinned to the PARENT's, not the child's raw transform origin. Resolved every
    /// frame in <see cref="LateUpdate"/> (not event-driven) since Edge mode has no "painted" event to hang off
    /// and needs to react to whatever's currently showing regardless of source. Game code addresses parts by
    /// name: <c>zoe.Part("Legs").Play("Run"); zoe.Part("Torso").Play("Shot");</c> Lives in this bridge (not core
    /// Zoetrope) because it directly touches Launimator's <see cref="ZonedAnimationPlayer"/> — same reason
    /// <see cref="ZonedLauminaryView"/> lives here instead of in core.
    /// </summary>
    public class CompositeZonedPlayer : MonoBehaviour, IPartLookup
    {
        class PartEntry
        {
            public GameObject go;
            public ZoeBodyPart def;
        }

        class Connection
        {
            public GameObject parentGo;
            public GameObject childGo;
            public ZoeBodyPart def;
        }

        readonly Dictionary<string, PartEntry> _byName = new Dictionary<string, PartEntry>(StringComparer.OrdinalIgnoreCase);
        readonly List<Connection> _connections = new List<Connection>();

        /// <summary>
        /// Build one child GameObject per part and wire non-root attachments. Call once, right after
        /// AddComponent. Returns the largest single part's own <see cref="ICharacterView.Build"/> size, as a
        /// simple stand-in for the composite's overall footprint (hurtbox sizing).
        /// </summary>
        public Vector2 Build(List<ZoeBodyPart> parts)
        {
            Vector2 size = Vector2.zero;
            if (parts == null) return Vector2.one;

            foreach (var part in parts)
            {
                if (part == null || string.IsNullOrEmpty(part.name)) continue;
                var go = new GameObject(part.name);
                go.transform.SetParent(transform, false);
                var partSize = part.view != null ? part.view.Build(go) : Vector2.one;
                size = Vector2.Max(size, partSize);
                _byName[part.name] = new PartEntry { go = go, def = part };

                // Each part's own directional animator, if authored — see ZoeBodyPart.motionPose. Reads
                // MotionState off the root via GetComponentInParent (MotionPoseAnimator itself does that
                // lookup), so this works with no extra wiring beyond attaching it here.
                go.GetComponent<IMotionPoseHost>()?.BindMotionPose(go, part.motionPose);
            }

            foreach (var entry in _byName.Values)
            {
                if (string.IsNullOrEmpty(entry.def.parentPartName)) continue;
                if (!_byName.TryGetValue(entry.def.parentPartName, out var parentEntry)) continue;
                _connections.Add(new Connection { parentGo = parentEntry.go, childGo = entry.go, def = entry.def });
            }

            return size == Vector2.zero ? Vector2.one : size;
        }

        void LateUpdate()
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                var c = _connections[i];
                if (c.parentGo == null || c.childGo == null) continue;

                Vector3 parentPoint = ResolveAnchor(c.parentGo, c.def.parentAnchor);
                // The child's own anchor is read BEFORE moving it (against its current position) so the
                // offset-from-origin it implies is transform-independent — self-correcting every frame
                // regardless of where the child currently sits, same reasoning the original two-point
                // MetaLayer stitching used.
                Vector3 childPointBefore = ResolveAnchor(c.childGo, c.def.childAnchor);
                Vector3 childLocalOffset = childPointBefore - c.childGo.transform.position;
                c.childGo.transform.position = parentPoint - childLocalOffset;
            }
        }

        /// <summary>Resolve one side of a connection to a world-space point on <paramref name="partGo"/>'s
        /// CURRENT frame. MetaLayer mode degrades to Edge mode when the named layer has nothing painted on the
        /// current frame (or is unset) — a quiet frame gets a still-sensible point instead of freezing at the
        /// last valid one, matching the "optional capability, graceful no-op" rule the rest of this codebase
        /// follows.</summary>
        static Vector3 ResolveAnchor(GameObject partGo, AttachAnchor anchor)
        {
            if (anchor.mode == AttachAnchorMode.MetaLayer && !string.IsNullOrEmpty(anchor.metaLayerId))
            {
                var player = partGo.GetComponent<ZonedAnimationPlayer>();
                if (player != null && player.TryGetMetaPoint(anchor.metaLayerId, out var worldPos, out _))
                    return worldPos + (Vector3)anchor.offset;
            }

            var sr = partGo.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return partGo.transform.position + (Vector3)anchor.offset;

            var b = sr.bounds;
            Vector3 basePoint;
            switch (anchor.edge)
            {
                case AttachEdge.Top: basePoint = new Vector3(b.center.x, b.max.y, b.center.z); break;
                case AttachEdge.Bottom: basePoint = new Vector3(b.center.x, b.min.y, b.center.z); break;
                case AttachEdge.Left: basePoint = new Vector3(b.min.x, b.center.y, b.center.z); break;
                case AttachEdge.Right: basePoint = new Vector3(b.max.x, b.center.y, b.center.z); break;
                default: basePoint = b.center; break;
            }
            return basePoint + (Vector3)anchor.offset;
        }

        /// <summary>The named part's ZonedAnimationPlayer, or null if that part's view didn't build one (a
        /// plain SpriteView part has nothing to Play) or the name is unknown.</summary>
        public ZonedAnimationPlayer Part(string name) =>
            _byName.TryGetValue(name, out var e) ? e.go.GetComponent<ZonedAnimationPlayer>() : null;

        /// IPartLookup: lets ZoeSpawner.EquipWeaponSlots attach a weapon to a named composite part
        /// (WeaponDef.attachToPartName) without core Zoetrope depending on this bridge.
        public Transform FindPartTransform(string partName) =>
            !string.IsNullOrEmpty(partName) && _byName.TryGetValue(partName, out var e) ? e.go.transform : null;
    }
}
