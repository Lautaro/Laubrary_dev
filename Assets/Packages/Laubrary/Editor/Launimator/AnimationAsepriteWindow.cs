using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Tiny utility window for the per-animation Aseprite round-trip: pick a reel + one of its draft
    /// animations, "Edit in Aseprite" to promote it to an owned editable <c>.aseprite</c>, edit + save in
    /// Aseprite, then "Sync from Aseprite" to pull the pixels back (into the reel's own Source/ folder)
    /// and re-bake. Kept separate from the main Animation Builder window so it composes cleanly.
    /// </summary>
    public class AnimationAsepriteWindow : ZUIWindow
    {
        private Reel _reel;
        private int _animIndex;
        private string _status = "";

        [MenuItem("Laubrary/Launimator/Animation ↔ Aseprite")]
        public static void Open() => GetWindow<AnimationAsepriteWindow>("Anim ↔ Aseprite");

        protected override void OnZUI()
        {
            VerticalSpace();
            _reel = ObjectField("Reel", _reel);

            if (_reel == null)
            {
                Label("Pick a reel to edit one of its draft animations in Aseprite.", ZUI.ZTextStyle.Subtle);
                return;
            }

            var draft = ReelRepo.EnsureDraft(_reel);
            var names = draft.animations.Select(a => a.name).ToArray();
            if (names.Length == 0)
            {
                Label("This reel's draft has no animations yet. Make one in the Animation Builder first.", ZUI.ZTextStyle.Subtle);
                return;
            }

            _animIndex = Mathf.Clamp(_animIndex, 0, names.Length - 1);
            _animIndex = Dropdown("Animation", _animIndex, names);
            var def = draft.animations[_animIndex];

            bool hasSource = !string.IsNullOrEmpty(def.asepriteSourcePath);
            Label("Source: " + (hasSource ? def.asepriteSourcePath : "(not promoted yet)"), ZUI.ZTextStyle.Small);

            VerticalSpace();
            using (var row = ZUI.HRow())
            {
                if (row.Button(new GUIContent(hasSource ? "Re-open in Aseprite" : "Edit in Aseprite",
                        "Promote this animation to an owned editable .aseprite in the reel's Source/ folder and open Aseprite.")))
                {
                    if (AnimationAseprite.Promote(def, ReelRepo.DraftFolder(_reel), out _status))
                        ReelRepo.SaveAnimationToDraft(_reel, def);
                }

                using (new EditorGUI.DisabledScope(!hasSource))
                    if (row.Button(new GUIContent("Sync from Aseprite",
                        "Pull the edited .aseprite back into the reel's own source and re-bake.")))
                    {
                        if (AnimationAseprite.Sync(def, ReelRepo.DraftFolder(_reel), out _status))
                            ReelRepo.SaveAnimationToDraft(_reel, def);
                    }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                VerticalSpace();
                Label(_status, ZUI.ZTextStyle.Subtle);
            }

            VerticalSpace();
            Label("Pixels only — authored events and meta-layers are preserved across the round-trip (paint those in " +
                  "the Animation Builder). Keep each sprite at its position on the Aseprite canvas; the canvas is the " +
                  "animation's frame box, so moving art down makes it render lower while the pivot stays put.",
                  ZUI.ZTextStyle.Subtle);
        }
    }
}
