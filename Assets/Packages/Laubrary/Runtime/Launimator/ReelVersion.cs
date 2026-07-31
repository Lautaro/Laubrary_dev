using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// One version of a reel — either the editable <c>draft</c> (<see cref="versionNumber"/> 0) or an
    /// immutable committed snapshot (1, 2, …). Holds the ordered animations and the game-ready generated
    /// assets (prefab + controller) for that version.
    ///
    /// IMPORTANT: this type MUST live in its own file (filename == class name). Unity only mints a MonoScript
    /// for the class matching the file name; when ReelVersion shared Reel.cs it had no MonoScript,
    /// so its assets serialized with <c>m_Script {fileID: 0}</c> (type resolved only via m_EditorClassIdentifier).
    /// Such script-less ScriptableObjects (a) are invisible to <c>FindAssets("t:ReelVersion")</c>, (b) become
    /// UNLOADABLE if their asset file is renamed, and (c) LOSE their serialized data (recipes!) when a dirty
    /// instance survives a domain reload. Keeping it here gives every version asset a real m_Script and avoids all
    /// three. (See ReelRepo for the historical workarounds.)
    /// </summary>
    public class ReelVersion : ScriptableObject, Laubrary.PreviewKit.IVisualPreview
    {
        [Tooltip("0 = the editable draft; 1, 2, … = immutable committed snapshots.")]
        public int versionNumber;

        [Tooltip("UTC timestamp (round-trip 'o' format) this version was created/last rebuilt.")]
        public string createdUtc;

        [Tooltip("The animations that make up this reel version, in display/build order.")]
        public List<AnimationDef> animations = new List<AnimationDef>();

        [Tooltip("Generated SpriteRenderer + Animator prefab for this version.")]
        public GameObject prefab;

        [Tooltip("Generated AnimatorController (one state per animation) for this version.")]
        public RuntimeAnimatorController controller;

        // ── IVisualPreview ──
        // Required, not optional: authoring.md §10 is "never show a visual asset by name alone", and a reel is
        // about as visual as an asset gets. Without this, every field that picks one — a Zoe's view, a
        // composite body part — could only show the text "SomeZoe_draft (Reel Version)", which tells an author
        // nothing about which of five near-identical sheets they just chose.
        //
        /// The animation a preview should show: the first one with frames. Reels have no notion of a
        /// "default" clip — that is the CONSUMER's choice (a view's idleClip) — so this deliberately picks
        /// the first rather than guessing at a name that may not exist.
        public AnimationDef PreviewAnimation()
        {
            if (animations == null) return null;
            foreach (var a in animations)
                if (a != null && a.frames != null && a.frames.Count > 0) return a;
            return null;
        }

        public Texture2D RenderPreviewTexture()
        {
            var a = PreviewAnimation();
            return a != null ? Laubrary.PreviewKit.PreviewTex.CropSprite(a.frames[0]) : null;
        }

        public bool CanAnimatePreview
        {
            get { var a = PreviewAnimation(); return a != null && a.frames.Count > 1; }
        }

        public float PreviewFps
        {
            get { var a = PreviewAnimation(); return a != null && a.fps > 0f ? a.fps : 12f; }
        }

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            var a = PreviewAnimation();
            if (tex == null || a == null || a.frames.Count <= 1) return;
            int i = Mathf.Abs((int)(time * PreviewFps)) % a.frames.Count;
            Laubrary.PreviewKit.PreviewTex.BlitInto(tex, a.frames[i]);
        }
    }
}
