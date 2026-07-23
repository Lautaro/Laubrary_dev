using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Tiny utility window for the per-animation Aseprite round-trip: pick a reel + one of its draft
    /// animations, "Edit in Aseprite" to promote it to an owned editable <c>.aseprite</c>, edit + save in
    /// Aseprite, then "Sync from Aseprite" to pull the pixels back (into the reel's own Source/ folder)
    /// and re-bake. Kept separate from the main Animation Builder window so it composes cleanly.
    ///
    /// UI TOOLKIT PORT: fully native — no canvas, so no IMGUI island. The control set depends on whether a
    /// reel/animation is picked, so the whole (tiny) body is rebuilt on those changes.
    /// </summary>
    public class AnimationAsepriteWindow : ZuiWindow
    {
        private Reel _reel;
        private int _animIndex;
        private string _status = "";

        [MenuItem("Laubrary/Launimator/Animation ↔ Aseprite")]
        public static void Open() => GetWindow<AnimationAsepriteWindow>("Anim ↔ Aseprite");

        protected override void BuildUI(VisualElement root)
        {
            root.Add(Z.Field("Reel", "The reel whose draft animation you want to round-trip through Aseprite.",
                Z.Object<Reel>(_reel, "The reel whose draft animation you want to round-trip through Aseprite.",
                    v => { _reel = v; _animIndex = 0; Rebuild(); }, 240f)));

            if (_reel == null)
            {
                root.Add(Z.Text("Pick a reel to edit one of its draft animations in Aseprite.", ZuiText.Subtle,
                    "Nothing to do until a reel is assigned above."));
                return;
            }

            var draft = ReelRepo.EnsureDraft(_reel);
            var names = draft.animations.Select(a => a.name).ToList();
            if (names.Count == 0)
            {
                root.Add(Z.Text("This reel's draft has no animations yet. Make one in the Animation Builder first.",
                    ZuiText.Subtle, "An animation has to exist on the draft before it can be promoted to Aseprite."));
                return;
            }

            _animIndex = Mathf.Clamp(_animIndex, 0, names.Count - 1);
            root.Add(Z.Field("Animation", "Which of this reel's draft animations to round-trip.",
                Z.Dropdown(_animIndex, names, "Which of this reel's draft animations to round-trip.",
                    v => { _animIndex = v; Rebuild(); }, 200f)));

            var def = draft.animations[_animIndex];
            bool hasSource = !string.IsNullOrEmpty(def.asepriteSourcePath);
            root.Add(Z.Text("Source: " + (hasSource ? def.asepriteSourcePath : "(not promoted yet)"), ZuiText.Subtle,
                "The owned .aseprite this animation's pixels round-trip through, once promoted."));

            root.Add(Z.VSpace());
            var editButton = Z.Button(hasSource ? "Re-open in Aseprite" : "Edit in Aseprite",
                "Promote this animation to an owned editable .aseprite in the reel's Source/ folder and open Aseprite.",
                () =>
                {
                    if (AnimationAseprite.Promote(def, ReelRepo.DraftFolder(_reel), out _status))
                        ReelRepo.SaveAnimationToDraft(_reel, def);
                    Rebuild();
                });
            var syncButton = Z.Button("Sync from Aseprite",
                "Pull the edited .aseprite back into the reel's own source and re-bake.",
                () =>
                {
                    if (AnimationAseprite.Sync(def, ReelRepo.DraftFolder(_reel), out _status))
                        ReelRepo.SaveAnimationToDraft(_reel, def);
                    Rebuild();
                });
            syncButton.SetEnabled(hasSource);
            root.Add(Z.Row(editButton, syncButton));

            if (!string.IsNullOrEmpty(_status))
            {
                root.Add(Z.VSpace());
                root.Add(Z.Text(_status, ZuiText.Subtle, "Result of the last promote/sync."));
            }

            root.Add(Z.VSpace());
            root.Add(Z.Help(
                "Pixels only — authored events and meta-layers are preserved across the round-trip (paint those in " +
                "the Animation Builder). Keep each sprite at its position on the Aseprite canvas; the canvas is the " +
                "animation's frame box, so moving art down makes it render lower while the pivot stays put."));
        }
    }
}
