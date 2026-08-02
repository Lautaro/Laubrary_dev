using System.Collections.Generic;
using UnityEngine;
using Laubrary.PreviewKit;

namespace Laubrary.Zoetrope
{
    /// A composition recipe for a weapon: its fire stats, the ammo it shoots, and its muzzle effect. Feeds a
    /// Combat2D ProjectileWeapon at spawn/equip time (fire rate, damage, spread, speed, projectiles-per-shot), and
    /// the muzzle effect plays at the muzzle each shot. Stats-only — the gun's own LOOK lives on the Zoe's body
    /// via a SpriteLayer, not here. The ammo's own look + impact live on AmmoDef.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Weapon", fileName = "Weapon")]
    public class WeaponDef : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        public string displayName = "New Weapon";

        [Header("Fire")]
        [Tooltip("Held trigger keeps firing (a machine gun). OFF = one shot per pull, however long you hold " +
                 "(a pistol, a shotgun). Off by default: a weapon that empties itself because the player kept " +
                 "the trigger down is a surprise, whereas having to hold for auto-fire never is.")]
        public bool automatic = false;

        [Min(0.01f)] public float fireRate = 6f;          // shots per second
        [Min(0f)] public float damage = 10f;
        [Min(0f)] public float projectileSpeed = 12f;
        [Range(0f, 180f)] public float spreadDeg = 0f;
        [Min(1)] public int projectilesPerShot = 1;

        [Header("Ammo")]
        [Tooltip("What this weapon fires (look + flight + impact). The first entry is used today; a weapon " +
                 "supporting multiple ammo types (e.g. a crossbow firing bolts or explosive bolts) is future work.")]
        public List<AmmoDef> ammoTypes = new List<AmmoDef>();

        [Header("Muzzle")]
        [Tooltip("Flash / smoke played at the muzzle each shot (pluggable effect).")]
        [SerializeReference] public ICombatFx muzzle;
        [Tooltip("Which MetaLayer id the shooter's animation must reach to trigger the muzzle effect AT ITS " +
                 "LIVE PAINTED POINT (via ICueSink, e.g. CueRelay). Ignored when muzzleEventName below is set. " +
                 "Falls back to a fixed muzzleOffset position triggered on every successful shot when the " +
                 "shooter has no ICueSink (e.g. a plain SpriteView).")]
        public string muzzleLayerId = "Muzzle";
        [Tooltip("Alternative to muzzleLayerId — the name of a FrameEvent (with an authored pixel position, " +
                 "set via the Animation Builder's pixel tool) that triggers the muzzle effect at its point " +
                 "instead. Takes priority over muzzleLayerId when set. Prefer this for a simple one-point-per-" +
                 "frame muzzle/spawn signal — MetaLayer painting is still the right tool for anything needing " +
                 "MULTIPLE pixels or MULTIPLE frames (e.g. hit detection).")]
        public string muzzleEventName = "";
        [Tooltip("Muzzle offset from the shooter, along its aim (x = forward, y = up). Only used by the " +
                 "no-ICueSink fallback above — a ZonedReelView shooter's muzzle position comes from the " +
                 "MetaLayer/FrameEvent itself, not this offset.")]
        public Vector2 muzzleOffset = new Vector2(0.5f, 0f);

        [Header("Audio")]
        [Tooltip("Zound (by name) played once per successful shot, at the same moment as the muzzle effect. Empty = silent.")]
        public string fireZoundName = "";

        // IVisualPreview — a judgment call (WeaponDef's own doc comment above is explicit that it has no
        // look of its own: "the gun's own LOOK lives on the Zoe's body ... not here"). Falls back to
        // ammoTypes[0]'s own preview — the projectile visual actually visible when this weapon fires, same
        // "first entry is used today" convention ammoTypes' own tooltip and ZoeSpawner.BuildProjectileTemplate
        // already use — so a browser/picker shows SOMETHING recognisable instead of a blank swatch. Returns
        // null (falls back to Unity's default ScriptableObject icon, via LauAssetGridGUI.GetThumbnail) when
        // there's no ammo configured yet — no fabricated placeholder art.
        public Texture2D RenderPreviewTexture()
        {
            var ammo = ammoTypes != null && ammoTypes.Count > 0 ? ammoTypes[0] : null;
            return ammo != null ? ammo.RenderPreviewTexture() : null;
        }
        public bool CanAnimatePreview => false;   // ammoTypes[0] itself never animates either — see AmmoDef.CanAnimatePreview
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
