using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// A single bullet fired straight at the screen. The aim line only decides where on the body it lands (somewhere along the part of the line that
    /// crosses visible pixels); the hole is dug straight in from the viewer's side and stays on that spot of the body when it turns. One member is
    /// hit, chosen at random among those the line can reach.
    /// </summary>
    [Serializable]
    public sealed class BulletRecipe : IWoundRecipe
    {
        [Header("Power")]
        [Tooltip("Bullet energy. More energy digs deeper and wider.")]
        public double energy = 7;
        [Tooltip("Base radius of the hole in pixels.")]
        public double radius = 1.3;
        [Tooltip("How hard the body resists. Higher means shallower holes.")]
        public double toughness = 0.9;
        [Tooltip("How quickly power fades with distance from the muzzle (per 100 pixels, beyond 10).")]
        public double rangeFalloff = 0.3;
        [Tooltip("Scales how deep the hole goes into the body; below 1 the bullet does not pierce through.")]
        public double straightDepth = 0.4;

        public string DisplayName { get { return "Bullet"; } }

        public void Generate(WoundContext ctx, List<GoreRemover> into)
        {
            var tuning = new ShotTuning { energy = energy, radius = radius, toughness = toughness, rangeFalloff = rangeFalloff, straightDepth = straightDepth };
            var holes = new List<KeyValuePair<int, List<GoreRemover>>>();
            for (int i = 0; i < ctx.members.Length; i++)
            {
                if (!GoreRecipeUtil.Usable(ctx, i)) continue;
                var made = new List<GoreRemover>();
                GoreRecipeUtil.Straight(ctx, i, tuning, 1, made);
                if (made.Count > 0) holes.Add(new KeyValuePair<int, List<GoreRemover>>(i, made));
            }
            if (holes.Count == 0) return;

            // One member takes the bullet, chosen by the wound's own seed so the same shot always lands the same way.
            int pick = (int)Math.Floor(GoreRng.Hash(ctx.seed, 3, ctx.cut.seed) * holes.Count) % holes.Count;
            GoreRecipeUtil.Append(ctx, into, holes[pick].Key, holes[pick].Value);
        }
    }
}
