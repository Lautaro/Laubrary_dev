using System.Linq;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Tiny utility window for the per-animation Aseprite round-trip: pick a zoe + one of its draft
    /// animations, "Edit in Aseprite" to promote it to an owned editable <c>.aseprite</c>, edit + save in
    /// Aseprite, then "Sync from Aseprite" to pull the pixels back (into the zoe's own Source/ folder)
    /// and re-bake. Kept separate from the main Animation Builder window so it composes cleanly.
    /// </summary>
    public class AnimationAsepriteWindow : ZUIWindow
    {
        private Zoe _zoe;
        private int _animIndex;
        private string _status = "";

        [MenuItem("Laubrary/Zoetrope/Animation ↔ Aseprite")]
        public static void Open() => GetWindow<AnimationAsepriteWindow>("Anim ↔ Aseprite");

        protected override void OnZUI()
        {
            VerticalSpace();
            _zoe = ObjectField("Zoe", _zoe);

            if (_zoe == null)
            {
                Label("Pick a zoe to edit one of its draft animations in Aseprite.", ZUI.ZTextStyle.Subtle);
                return;
            }

            var draft = ZoeRepo.EnsureDraft(_zoe);
            var names = draft.animations.Select(a => a.name).ToArray();
            if (names.Length == 0)
            {
                Label("This zoe's draft has no animations yet. Make one in the Animation Builder first.", ZUI.ZTextStyle.Subtle);
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
                        "Promote this animation to an owned editable .aseprite in the zoe's Source/ folder and open Aseprite.")))
                {
                    if (AnimationAseprite.Promote(def, ZoeRepo.DraftFolder(_zoe), out _status))
                        ZoeRepo.SaveAnimationToDraft(_zoe, def);
                }

                using (new EditorGUI.DisabledScope(!hasSource))
                    if (row.Button(new GUIContent("Sync from Aseprite",
                        "Pull the edited .aseprite back into the zoe's own source and re-bake.")))
                    {
                        if (AnimationAseprite.Sync(def, ZoeRepo.DraftFolder(_zoe), out _status))
                            ZoeRepo.SaveAnimationToDraft(_zoe, def);
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
