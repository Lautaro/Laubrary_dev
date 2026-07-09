using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;

namespace Laubrary.ZoeCombat
{
    /// <summary>
    /// The Zoetrope ⇄ Colosseum pixel-perfect bridge. Drop this on a Combatant that renders through a
    /// <see cref="ZonedAnimationPlayer"/> and Colosseum will run its cheap checks (distance + collider overlap +
    /// faction) first, then defer to the Zoe's authored meta-layer for the final per-pixel word: a hit only lands
    /// if the contact point falls on a painted cell of the <see cref="hurtLayer"/> at the CURRENT frame. This is
    /// the <see cref="IHitFilter"/> seam <see cref="Combat.TryDamage"/> honours no matter what dealt the hit
    /// (hitbox, projectile). Neither Colosseum nor Zoetrope depends on the other — this tiny module bridges them.
    /// </summary>
    [AddComponentMenu("Laubrary/ZoeCombat/Zoe Hit Filter")]
    public class ZoeHitFilter : MonoBehaviour, IHitFilter
    {
        [Tooltip("The Zoe whose painted silhouette gates incoming hits. Auto-resolved from this object (or its " +
                 "children) if left empty.")]
        public ZonedAnimationPlayer body;

        [Tooltip("Meta-layer id on the Zoe that represents the vulnerable region (the painted mask a hit must land " +
                 "on). Authored per Zoe in Zoetrope. If blank the filter can't test and falls back to " +
                 "acceptWhenUnresolved.")]
        public string hurtLayer = "body";

        [Tooltip("Optional: if the attacker is ITSELF a Zoe, test this meta-layer on it against the hurt layer with a " +
                 "full mask-vs-mask overlap (more accurate than a single contact point). Blank = always use the " +
                 "point test.")]
        public string attackLayer = "";

        [Tooltip("What to answer when the filter can't run (no player resolved, or hurtLayer blank). True accepts the " +
                 "collider hit (fail-open — a misconfigured filter never makes the target invincible); false rejects.")]
        public bool acceptWhenUnresolved = true;

        bool _resolved;

        void EnsureBody()
        {
            if (_resolved) return;
            if (body == null) body = GetComponentInChildren<ZonedAnimationPlayer>();
            _resolved = true;
        }

        // ── IHitFilter ──
        public bool ConfirmHit(Combatant self, GameObject attacker, Vector2 worldPoint)
        {
            EnsureBody();
            if (body == null || string.IsNullOrEmpty(hurtLayer)) return acceptWhenUnresolved;

            // Best case: the attacker is also a Zoe → confirm the two painted masks actually overlap.
            if (!string.IsNullOrEmpty(attackLayer) && attacker != null)
            {
                var atk = attacker.GetComponentInChildren<ZonedAnimationPlayer>();
                if (atk != null) return body.PixelOverlaps(hurtLayer, atk, attackLayer, out _);
            }

            // Otherwise gate on the contact point landing on a painted cell of the hurt layer at the current frame.
            return body.IsMetaPainted(hurtLayer, worldPoint, out _);
        }
    }
}
