using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>How an <see cref="AttachAnchor"/> locates its point on a part's current frame.</summary>
    public enum AttachAnchorMode
    {
        /// Computed automatically from the part's current sprite bounds — zero authoring. The common case
        /// (stack two parts edge-to-edge) needs nothing more than picking a side.
        Edge,
        /// Read from a named, per-frame painted MetaLayer point — for content that genuinely shifts through
        /// the animation (e.g. a walk cycle's weight shift), where a fixed edge isn't enough.
        MetaLayer,
    }

    /// <summary>Which side of a part's current sprite bounds an <see cref="AttachAnchorMode.Edge"/> anchor
    /// reads. World-space axis-aligned (the sprite's rendered bounds, not its unrotated source rect).</summary>
    public enum AttachEdge { Center, Top, Bottom, Left, Right }

    /// <summary>
    /// One side of a connection between two composite body parts — where, on THAT part's current frame, the
    /// attach point sits. Two of these (one per side) plus a shared connection form the whole attach
    /// definition; see <see cref="ZoeBodyPart.parentAnchor"/>/<see cref="ZoeBodyPart.childAnchor"/>. "Static"
    /// attachment is not a separate mode — it's simply <see cref="AttachAnchorMode.Edge"/> with <see
    /// cref="offset"/> left at its default, since Edge mode already needs no per-frame painting.
    /// </summary>
    [System.Serializable]
    public class AttachAnchor
    {
        public AttachAnchorMode mode = AttachAnchorMode.Edge;
        [Tooltip("Edge mode only: which side of the current sprite's bounds.")]
        public AttachEdge edge = AttachEdge.Center;
        [Tooltip("MetaLayer mode only: the painted point's layer id (e.g. \"Waist\"). Must match a Point-mode " +
                 "MetaLayer authored on this part's own Lauminary.")]
        public string metaLayerId = "";
        [Tooltip("Fine-tune nudge added on top of the computed/painted point, in world units. (0,0) = use the " +
                 "anchor exactly as computed — this is what makes Edge mode alone sufficient for a plain " +
                 "static offset, with no separate \"fixed offset\" mode needed.")]
        public Vector2 offset = Vector2.zero;
    }

    /// <summary>
    /// One named part of a composite body — its own pluggable view (independently timed from every other
    /// part), attached to its parent part via one <see cref="AttachAnchor"/> per side (see <see
    /// cref="parentAnchor"/>/<see cref="childAnchor"/>). Built by <see cref="CompositeZonedPlayer"/>, one
    /// child GameObject per part.
    /// </summary>
    [System.Serializable]
    public class ZoeBodyPart
    {
        [Tooltip("Referenced by other parts' parentPartName, and by gameplay code via CompositeZonedPlayer.Part(name).")]
        public string name = "Torso";
        [SerializeReference] public ICharacterView view = new SpriteView();
        [Tooltip("Which part this attaches to. Empty = the root part (positioned at the Zoe's own transform, " +
                 "the anchors below are unused).")]
        public string parentPartName = "";

        [Tooltip("Where on the PARENT's current frame this part attaches — resolved every frame.")]
        public AttachAnchor parentAnchor = new AttachAnchor();
        [Tooltip("Where on THIS part's own current frame the connection lands — resolved every frame and " +
                 "pinned to the parent anchor, so a part whose own art isn't registered around the attach " +
                 "point still lines up correctly.")]
        public AttachAnchor childAnchor = new AttachAnchor();

        [Tooltip("This part's own directional pose — a torso can read Aim while legs read Heading, " +
                 "independently, because each part gets its own MotionPoseAnimator all reading the SAME " +
                 "character-wide MotionState off the root. Additive and unauthored by default.")]
        public MotionPose motionPose = new MotionPose();
    }

    /// <summary>
    /// An <see cref="ICharacterView"/> that IS a composite, multi-part body — independently-timed parts (e.g.
    /// legs running while a torso is mid-shot on a different clip), each with its own view. Assign it to a
    /// <c>Zoe.view</c> in place of <see cref="ZonedLauminaryView"/> for a character that needs more than one part;
    /// <see cref="ZoeSpawner.SpawnCharacter"/> needs no special-casing for this —
    /// composite-ness is purely a property of which view is plugged in, same as every other pluggable view.
    /// Adds a <see cref="CompositeZonedPlayer"/> to the host, so game code addresses parts via
    /// <c>host.GetComponent&lt;CompositeZonedPlayer&gt;().Part("Legs").Play("Run")</c>.
    /// </summary>
    [System.Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "CompositeReelView")]
    public class CompositeLauminaryView : ICharacterView, IPreviewableView
    {
        [Tooltip("One entry should have an empty parentPartName (the root part, positioned at the Zoe's own " +
                 "transform); every other entry names which part it attaches to.")]
        public List<ZoeBodyPart> parts = new List<ZoeBodyPart>();

        // ── IPreviewableView ──
        // A composite body is several views stacked at runtime, and a thumbnail cannot stack them without
        // building the whole rig — the parts are positioned by live meta-layer attachment, not by fixed
        // offsets. So it previews its ROOT part, which is the torso/body in every authored case and is what
        // makes the character recognisable in a picker. A partial preview beats a blank card; the Zoe Preview
        // window is where the assembled body is meant to be inspected.
        ZoeBodyPart RootPart()
        {
            if (parts == null || parts.Count == 0) return null;
            foreach (var p in parts)
                if (p != null && string.IsNullOrEmpty(p.parentPartName) && p.view is IPreviewableView) return p;
            foreach (var p in parts)
                if (p != null && p.view is IPreviewableView) return p;
            return null;
        }

        public Sprite[] PreviewFrames()
        {
            var root = RootPart();
            return root != null ? ((IPreviewableView)root.view).PreviewFrames() : System.Array.Empty<Sprite>();
        }

        public float PreviewFps
        {
            get { var root = RootPart(); return root != null ? ((IPreviewableView)root.view).PreviewFps : 0f; }
        }

        public Vector2 Build(GameObject host)
        {
            var player = host.AddComponent<CompositeZonedPlayer>();
            return player.Build(parts);
        }
    }
}
