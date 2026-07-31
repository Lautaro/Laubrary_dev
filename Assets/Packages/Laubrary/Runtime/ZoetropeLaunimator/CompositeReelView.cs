using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// One named part of a composite body — its own pluggable view (independently timed from every other
    /// part), optionally riding a named MetaLayer point on its parent part (e.g. a torso following its legs
    /// part's painted "Hip" point every frame the legs' current clip has one). Built by
    /// <see cref="CompositeZonedPlayer"/>, one child GameObject per part.
    /// </summary>
    [System.Serializable]
    public class ZoeBodyPart
    {
        [Tooltip("Referenced by other parts' parentPartName, and by gameplay code via CompositeZonedPlayer.Part(name).")]
        public string name = "Torso";
        [SerializeReference] public ICharacterView view = new SpriteView();
        [Tooltip("Which part this attaches to. Empty = the root part (positioned at the Zoe's own transform).")]
        public string parentPartName = "";
        [Tooltip("Named MetaLayer id on the parent part's current frame this part's position follows every " +
                 "frame that id has painted data (e.g. \"Hip\"). Empty = stay at the parent's origin (a static, " +
                 "non-animated attach).")]
        public string attachMetaLayerId = "";
    }

    /// <summary>
    /// An <see cref="ICharacterView"/> that IS a composite, multi-part body — independently-timed parts (e.g.
    /// legs running while a torso is mid-shot on a different clip), each with its own view. Assign it to a
    /// <c>Zoe.view</c> in place of <see cref="ZonedReelView"/> for a character that needs more than one part;
    /// <see cref="ZoeSpawner.SpawnCharacter"/> needs no special-casing for this —
    /// composite-ness is purely a property of which view is plugged in, same as every other pluggable view.
    /// Adds a <see cref="CompositeZonedPlayer"/> to the host, so game code addresses parts via
    /// <c>host.GetComponent&lt;CompositeZonedPlayer&gt;().Part("Legs").Play("Run")</c>.
    /// </summary>
    [System.Serializable]
    public class CompositeReelView : ICharacterView, IPreviewableView
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
