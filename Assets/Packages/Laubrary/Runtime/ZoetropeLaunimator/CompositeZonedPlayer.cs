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
    /// the whole body). A non-root part either Transform-parents statically under its parent part (no
    /// <c>attachMetaLayerId</c>), or follows the parent's named MetaLayer point LIVE via
    /// <see cref="ZonedAnimationPlayer.OnMetaLayerReached"/> — reusing the exact point mechanism already built
    /// for muzzle alignment, not a second one. Game code addresses parts by name:
    /// <c>zoe.Part("Legs").Play("Run"); zoe.Part("Torso").Play("Shot");</c> Lives in this bridge (not core
    /// Zoetrope) because it directly touches Launimator's <see cref="ZonedAnimationPlayer"/> — same reason
    /// <see cref="ZonedReelView"/> lives here instead of in core.
    /// </summary>
    public class CompositeZonedPlayer : MonoBehaviour
    {
        class PartEntry
        {
            public GameObject go;
            public ZoeBodyPart def;
        }

        readonly Dictionary<string, PartEntry> _byName = new Dictionary<string, PartEntry>(StringComparer.OrdinalIgnoreCase);
        readonly List<Action> _unsubscribers = new List<Action>();

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
            }

            foreach (var entry in _byName.Values)
            {
                if (string.IsNullOrEmpty(entry.def.parentPartName)) continue;
                if (!_byName.TryGetValue(entry.def.parentPartName, out var parentEntry)) continue;

                if (string.IsNullOrEmpty(entry.def.attachMetaLayerId))
                {
                    // Static attach: Transform-parent under the parent part, so it moves automatically if the
                    // parent's own transform ever does.
                    entry.go.transform.SetParent(parentEntry.go.transform, false);
                    continue;
                }

                var parentPlayer = parentEntry.go.GetComponent<ZonedAnimationPlayer>();
                if (parentPlayer == null) continue; // parent's view isn't a ZonedAnimationPlayer-based one

                var child = entry.go;
                var wantLayer = entry.def.attachMetaLayerId;
                void Handler(string layerId, Vector3 worldPos)
                {
                    if (string.Equals(layerId, wantLayer, StringComparison.OrdinalIgnoreCase))
                        child.transform.position = worldPos;
                }
                parentPlayer.OnMetaLayerReached += Handler;
                _unsubscribers.Add(() => parentPlayer.OnMetaLayerReached -= Handler);
            }

            return size == Vector2.zero ? Vector2.one : size;
        }

        /// <summary>The named part's ZonedAnimationPlayer, or null if that part's view didn't build one (a
        /// plain SpriteView part has nothing to Play) or the name is unknown.</summary>
        public ZonedAnimationPlayer Part(string name) =>
            _byName.TryGetValue(name, out var e) ? e.go.GetComponent<ZonedAnimationPlayer>() : null;

        void OnDestroy()
        {
            foreach (var unsub in _unsubscribers) unsub();
            _unsubscribers.Clear();
        }
    }
}
