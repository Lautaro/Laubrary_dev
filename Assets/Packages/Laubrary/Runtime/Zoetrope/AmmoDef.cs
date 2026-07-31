using UnityEngine;
using Laubrary.Chunks;
using Laubrary.Combat2D;
using Laubrary.PreviewKit;

namespace Laubrary.Zoetrope
{
    /// A composition recipe for what a projectile IS: its visual, flight, and impact — the ammo half of a shot.
    /// Combat2D's Projectile is the spine (movement / faction / damage / collision); a WeaponDef supplies the
    /// fire stats and picks which AmmoDef(s) it fires. The visual is an IChunkAnimation reference — the same
    /// unifier Chunks already uses for "any visual, thrown as a physics object" (a Pyre blast or a Reel
    /// animation both implement it), so ammo doesn't invent a third visual-asset kind. A spawner bridges this
    /// onto a runtime Combat2D Projectile. Implements IVisualPreview directly (crops Visual's own first
    /// frame, the SAME frame the projectile itself actually shows — see Visual's own doc comment below) so
    /// any LauAsset browser/picker (WeaponDef.ammoTypes' list, Mirage, ...) gets a real thumbnail for free.
    [CreateAssetMenu(menuName = "Laubrary/Zoetrope/Ammo", fileName = "Ammo")]
    public class AmmoDef : ScriptableObject, IVisualPreview
    {
        [Header("Identity")]
        public string displayName = "New Ammo";

        [Header("Visual")]
        [Tooltip("A Pyre Blast Chunk Animation or a Reel Chunk Animation — anything implementing IChunkAnimation. " +
                 "Only its first frame is shown on the projectile today; per-projectile animation playback is a " +
                 "future add.")]
        [RequireInterface(typeof(IChunkAnimation))]
        public Object visual;
        public IChunkAnimation Visual => visual as IChunkAnimation;
        public float scale = 1f;
        [Tooltip("Spin the sprite while it flies (rotating rocket / grenade).")]
        public bool spin;
        [Tooltip("Spin rate in degrees/second (when Spin is on).")]
        public float spinSpeed = 360f;
        [Tooltip("Face the sprite along its travel direction (bolts/arrows).")]
        public bool faceTravel = false;

        // IVisualPreview — a plain static crop of Visual's own first frame, no separate render path (same
        // rule PyreChunkAnimation/ReelAnimationChunkAdapter's own IVisualPreview implementations
        // follow). Never animates: "Only its first frame is shown on the projectile today" above is real
        // runtime behaviour, not a preview shortcut, so an animated preview would misrepresent it.
        public Texture2D RenderPreviewTexture()
        {
            var frames = Visual?.GetFrames();
            if (frames == null || frames.Length == 0) return null;
            // Via PreviewTex, because the hand-rolled crop this used to do called GetPixels directly and threw
            // on any sprite imported with Read/Write disabled — i.e. on most real art, while working fine on
            // anything a tool generated itself.
            return Laubrary.PreviewKit.PreviewTex.CropSprite(frames[0]);
        }
        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }

        [Header("Flight")]
        [Min(0.05f)] public float lifetime = 3f;
        [Tooltip("Pass through targets instead of dying on first hit.")]
        public bool pierce = false;
        [Tooltip("How this ammo actually moves each frame (straight-line 2D by default). Pick DepthMotion for " +
                 "rail-shooter travel toward a resolved target. See Combat2D.IProjectileMotion.")]
        [SerializeReference] public IProjectileMotion motion = new PlanarMotion();
        [Tooltip("Not yet wired into flight — a future BallisticMotion (IProjectileMotion) would consume this.")]
        public float gravity = 0f;
        [Tooltip("Not yet wired into flight — a future HomingMotion (IProjectileMotion) would consume this.")]
        public bool homing = false;

        [Header("Impact")]
        [Tooltip("Effect played where the projectile hits (pluggable — e.g. blast + debris via the Zoetrope.Pyre bridge).")]
        [SerializeReference] public ICombatFx impact;

        // TODO(bridge): a pluggable flight trail (was a Chunks trail on the old coupled Codex) belongs in a
        // presentation bridge as an ITrail-style seam, keeping this core Combat2D-only.
        // TODO(zounds): impact Zound ref.
    }
}
