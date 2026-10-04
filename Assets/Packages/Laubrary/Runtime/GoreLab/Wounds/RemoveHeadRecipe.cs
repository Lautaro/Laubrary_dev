using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>Takes the head off with one flat cut across the neck. Ignores the swipe and the target choice; does nothing if the head is already gone.</summary>
    [Serializable]
    public sealed class RemoveHeadRecipe : IWoundRecipe
    {
        [Header("Neck")]
        [Tooltip("Position of the head in the rig's member list (the first member by default).")]
        public int headMember = 0;
        [Tooltip("How far below the head's centre the cut lies, in head radii. Everything above it comes off.")]
        public double neckDepth = 0.8;

        public string DisplayName { get { return "Remove head"; } }

        public void Generate(WoundContext ctx, List<GoreRemover> into)
        {
            if (ctx.members == null || headMember < 0 || headMember >= ctx.members.Length) return;
            var head = ctx.members[headMember];
            if (!head.present || head.skip) return;
            if (HasNeckCut(ctx.existing) || HasNeckCut(into)) return;

            var made = new List<GoreRemover>(1)
            {
                new GoreRemover { kind = RemoverKinds.Plane, nx = 0, ny = 1, nz = 0, d = -neckDepth },
            };
            GoreRecipeUtil.Append(ctx, into, headMember, made);
        }

        // A plane facing straight up in the head's own coordinates is a neck cut, whoever made it.
        bool HasNeckCut(List<GoreRemover> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r.kind == RemoverKinds.Plane && r.member == headMember && r.nx == 0 && r.ny == 1) return true;
            }
            return false;
        }
    }
}
