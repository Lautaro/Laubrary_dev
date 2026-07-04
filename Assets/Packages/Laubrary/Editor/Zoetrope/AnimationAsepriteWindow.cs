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
    public class AnimationAsepriteWindow : EditorWindow
    {
        private Zoe _zoe;
        private int _animIndex;
        private string _status = "";

        [MenuItem("Laubrary/Zoetrope/Animation ↔ Aseprite")]
        public static void Open() => GetWindow<AnimationAsepriteWindow>("Anim ↔ Aseprite");

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _zoe = (Zoe)EditorGUILayout.ObjectField("Zoe", _zoe, typeof(Zoe), false);

            if (_zoe == null)
            {
                EditorGUILayout.HelpBox("Pick a zoe to edit one of its draft animations in Aseprite.", MessageType.Info);
                return;
            }

            var draft = ZoeRepo.EnsureDraft(_zoe);
            var names = draft.animations.Select(a => a.name).ToArray();
            if (names.Length == 0)
            {
                EditorGUILayout.HelpBox("This zoe's draft has no animations yet. Make one in the Animation Builder first.", MessageType.Info);
                return;
            }

            _animIndex = Mathf.Clamp(_animIndex, 0, names.Length - 1);
            _animIndex = EditorGUILayout.Popup("Animation", _animIndex, names);
            var def = draft.animations[_animIndex];

            bool hasSource = !string.IsNullOrEmpty(def.asepriteSourcePath);
            EditorGUILayout.LabelField("Source", hasSource ? def.asepriteSourcePath : "(not promoted yet)");

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(hasSource ? "Re-open in Aseprite" : "Edit in Aseprite",
                        "Promote this animation to an owned editable .aseprite in the zoe's Source/ folder and open Aseprite.")))
                {
                    if (AnimationAseprite.Promote(def, ZoeRepo.DraftFolder(_zoe), out _status))
                        ZoeRepo.SaveAnimationToDraft(_zoe, def);
                }

                using (new EditorGUI.DisabledScope(!hasSource))
                    if (GUILayout.Button(new GUIContent("Sync from Aseprite",
                        "Pull the edited .aseprite back into the zoe's own source and re-bake.")))
                    {
                        if (AnimationAseprite.Sync(def, ZoeRepo.DraftFolder(_zoe), out _status))
                            ZoeRepo.SaveAnimationToDraft(_zoe, def);
                    }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Pixels only — authored events and meta-layers are preserved across the round-trip (paint those in " +
                "the Animation Builder). Keep each sprite at its position on the Aseprite canvas; the canvas is the " +
                "animation's frame box, so moving art down makes it render lower while the pivot stays put.",
                MessageType.Info);
        }
    }
}
