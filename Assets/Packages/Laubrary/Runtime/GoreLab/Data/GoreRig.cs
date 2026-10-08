using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Launimator;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// The gore data for one character, kept in its own asset so the character never knows it exists: which body members it has, where each member
    /// sits on every drawn frame, and which damage types it offers. Several rigs may point at the same character.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/GoreLab/Gore Rig", fileName = "GoreRig")]
    public sealed class GoreRig : ScriptableObject, Laubrary.PreviewKit.IVisualPreview
    {
        [Header("Target")]
        [Tooltip("The character this rig adds wounds to. The character itself does not know about the rig.")]
        public Zoe zoe;
        [Tooltip("Used when the Zoe field is empty: a bare animation set to add wounds to.")]
        public LauminaryVersion reel;

        [Header("Body")]
        [Tooltip("The body members that can be cut. Their order is fixed: frame tags, wounds and recipes refer to a member by its position here.")]
        public List<GoreMemberDef> members = DefaultMembers();
        [Tooltip("The authored tags, one entry per drawn sprite.")]
        public List<GoreFrameTags> frames = new List<GoreFrameTags>();

        [Header("Damage")]
        [SerializeReference, Tooltip("The damage types this rig offers (slice, bullet...). An empty list is filled with the standard set on first use.")]
        public List<IWoundRecipe> recipes = GoreRecipes.CreateDefaultList();
        [Tooltip("Cut-engine numbers: noise seed, ragged-edge amplitude and frequency, bone core.")]
        public GoreCutConfig cut = GoreCutConfig.Default();
        [Tooltip("Colours of the wounds, blood and bone.")]
        public GoreStyle style = GoreStyle.Fleshy();

        [NonSerialized] Dictionary<Sprite, GoreFrameTags> _byFrame;
        [NonSerialized] int _byFrameCount = -1;

        /// <summary>Per member: may a slice or neck cut sever it? Parallel to the member list; passed to recipes through the wound context.</summary>
        public bool[] SliceableFlags()
        {
            var f = new bool[members != null ? members.Count : 0];
            for (int i = 0; i < f.Length; i++) f[i] = members[i] == null || members[i].sliceable;
            return f;
        }

        public static List<GoreMemberDef> DefaultMembers()
        {
            return new List<GoreMemberDef>
            {
                new GoreMemberDef("Head", MemberKind.Ball, new Color(1f, 0.55f, 0.2f)),
                new GoreMemberDef("Torso", MemberKind.Box, new Color(0.35f, 0.7f, 1f), sliceable: false),
            };
        }

        /// <summary>Fills an empty recipe list with the standard damage types and returns the list.</summary>
        public List<IWoundRecipe> EnsureRecipes()
        {
            if (recipes == null) recipes = new List<IWoundRecipe>();
            if (recipes.Count == 0) recipes.AddRange(GoreRecipes.CreateDefaultList());
            return recipes;
        }

        /// <summary>Forget the sprite lookup; call after the frame list was edited in a way that keeps its length (replacing a sprite).</summary>
        public void InvalidateFrameCache() { _byFrame = null; _byFrameCount = -1; }

        void OnValidate() { InvalidateFrameCache(); }

        /// <summary>The tags authored for this drawn sprite, if any.</summary>
        public bool TryGetFrame(Sprite s, out GoreFrameTags f)
        {
            f = null;
            if (s == null || frames == null) return false;
            if (_byFrame == null || _byFrameCount != frames.Count) RebuildFrameCache();
            if (_byFrame.TryGetValue(s, out f)) return true;
#if UNITY_EDITOR
            // The editor can swap a sprite inside an entry without changing the count; one rebuild on a miss catches that.
            RebuildFrameCache();
            return _byFrame.TryGetValue(s, out f);
#else
            return false;
#endif
        }

        void RebuildFrameCache()
        {
            if (_byFrame == null) _byFrame = new Dictionary<Sprite, GoreFrameTags>(); else _byFrame.Clear();
            for (int i = 0; i < frames.Count; i++)
            {
                var ft = frames[i];
                if (ft != null && ft.sprite != null && !_byFrame.ContainsKey(ft.sprite)) _byFrame.Add(ft.sprite, ft);
            }
            _byFrameCount = frames.Count;
        }

        /// <summary>Every distinct sprite the target can display: all animation frames of the target's version, then the frames of its direction sets.</summary>
        public IEnumerable<Sprite> EnumerateTargetSprites()
        {
            var version = TargetVersion();
            if (version == null) yield break;
            var seen = new HashSet<Sprite>();
            if (version.animations != null)
                for (int a = 0; a < version.animations.Count; a++)
                    foreach (var s in FramesOf(version.animations[a], seen)) yield return s;
            if (version.sets != null)
                for (int i = 0; i < version.sets.Count; i++)
                {
                    var set = version.sets[i];
                    if (set == null || set.members == null) continue;
                    for (int m = 0; m < set.members.Count; m++)
                        foreach (var s in FramesOf(set.members[m]?.laumination, seen)) yield return s;
                }
        }

        static IEnumerable<Sprite> FramesOf(Laumination anim, HashSet<Sprite> seen)
        {
            if (anim == null || anim.frames == null) yield break;
            for (int i = 0; i < anim.frames.Count; i++)
            {
                var s = anim.frames[i];
                if (s != null && seen.Add(s)) yield return s;
            }
        }

        LauminaryVersion TargetVersion()
        {
            if (zoe != null)
            {
                if (zoe.view is ZonedLauminaryView zv && zv.version != null) return zv.version;
                if (zoe.view is LauminaryView lv && lv.version != null) return lv.version;
            }
            return reel;
        }

        // ── IVisualPreview ──
        public Texture2D RenderPreviewTexture() { return GoreRigPreview.Render(this); }
        public bool CanAnimatePreview { get { return false; } }
        public float PreviewFps { get { return 0f; } }
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
