using System.Collections.Generic;
using Laubrary.SpriteFx;
using Laubrary.SpriteFx.Editor;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// Teaches the SpriteFx stack window how to preview a stack on the CHARACTER that uses it.
    ///
    /// The problem it solves, in the form a user meets it: you add a SpriteFx to a Zoe's death reaction, hit
    /// Edit, and the stack window opens on an empty stage asking you to go find a sprite. You are authoring a
    /// death flash for THAT imp and the one thing the window will not show you is that imp. So the effect
    /// gets tuned against an unrelated still and looks wrong the first time it plays in game.
    ///
    /// Direction of the dependency is why this file exists at all rather than the window doing it: Zoetrope
    /// references SpriteFx, never the reverse, so the window cannot go looking for a Zoe. It exposes a
    /// registry instead and this registers into it — the module that can see both sides supplies the bridge.
    [InitializeOnLoad]
    static class ZoeSpriteFxPreviewLink
    {
        static ZoeSpriteFxPreviewLink() => SpriteFxPreviewSubjects.Register(Resolve);

        /// Which Zoe reaction uses this stack, and what does it look like?
        ///
        /// Searched rather than passed, deliberately. The window is reached several ways — the Edit pen on a
        /// Zoe effect, a LauAsset picker, double-clicking the asset in the Project window — and threading a
        /// subject through every one of those routes would mean each caller has to remember to. A search
        /// answers all of them, including the routes that have no context to pass.
        ///
        /// A stack shared by several characters resolves to the first match in a stable order. That is a
        /// real limitation and the window says which subject it picked, so a wrong guess is visible rather
        /// than silent; the manual sprite picker remains the override.
        static SpriteFxPreviewSubject Resolve(SpriteFxSpec spec)
        {
            if (spec == null) return null;

            var guids = AssetDatabase.FindAssets("t:Zoe");
            System.Array.Sort(guids, System.StringComparer.Ordinal);   // stable: same project, same answer

            foreach (var g in guids)
            {
                var zoe = AssetDatabase.LoadAssetAtPath<Zoe>(AssetDatabase.GUIDToAssetPath(g));
                if (zoe == null) continue;

                foreach (var (reaction, name) in Reactions(zoe))
                {
                    if (reaction == null || !UsesStack(reaction, spec)) continue;
                    var frames = Frames(zoe, reaction.clip, out float fps);
                    if (frames == null || frames.Length == 0) continue;
                    return new SpriteFxPreviewSubject
                    {
                        Frames = frames,
                        Fps = fps,
                        Label = string.IsNullOrEmpty(reaction.clip)
                            ? $"{zoe.name} — {name}"
                            : $"{zoe.name} — {name} ({reaction.clip})",
                    };
                }
            }
            return null;
        }

        /// Every named reaction on a Zoe. One place to extend when custom named events land, so the preview
        /// picks them up without this file being revisited.
        static IEnumerable<(ReactionFx, string)> Reactions(Zoe zoe)
        {
            yield return (zoe.hit, "hit");
            yield return (zoe.death, "death");
        }

        /// Both places a reaction can carry a stack: the body filter, and any BodySpriteFxEffect in its FX
        /// list. Missing the second would make the feature work for one authoring route and not the other.
        static bool UsesStack(ReactionFx reaction, SpriteFxSpec spec)
        {
            if (reaction.bodyFx == spec) return true;
            if (reaction.fx == null) return false;
            foreach (var entry in reaction.fx)
                if (entry != null && entry.fx is BodySpriteFxEffect body && body.stack == spec) return true;
            return false;
        }

        /// The view's frames for that clip. A clip-aware view gives the event's own animation; a plain
        /// SpriteView has one picture and no clips, and correctly yields that — "whatever it is configured
        /// for" covers both without the caller branching on view type.
        static Sprite[] Frames(Zoe zoe, string clip, out float fps)
        {
            fps = 0f;
            if (zoe.view is IClipPreviewableView byClip)
            {
                fps = byClip.PreviewFpsOf(clip);
                return byClip.PreviewFrames(clip);
            }
            if (zoe.view is IPreviewableView plain)
            {
                fps = plain.PreviewFps;
                return plain.PreviewFrames();
            }
            return System.Array.Empty<Sprite>();
        }
    }
}
