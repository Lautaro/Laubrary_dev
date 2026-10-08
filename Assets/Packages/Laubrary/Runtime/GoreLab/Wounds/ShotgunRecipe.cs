using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// A shotgun blast: a cone of pellets from the muzzle toward the aim point, each digging its own tunnel. Hits every tagged member the cone reaches.
    /// With 'straight on' the blast comes from the viewer instead: the aim line only decides where the pellets land on the visible body.
    /// </summary>
    [Serializable]
    public sealed class ShotgunRecipe : IWoundRecipe
    {
        [Header("Blast")]
        [Tooltip("Number of pellets.")]
        public int pellets = 30;
        [Tooltip("Half-angle of the pellet cone in degrees. Most pellets stay near the middle.")]
        public double coneDeg = 12;
        [Tooltip("Fire from the viewer's side: pellets land on the visible body along the aim line and dig shallow holes straight in, instead of flying across the screen.")]
        public bool straightOn = false;

        [Header("Power")]
        [Tooltip("Energy of a pellet. More energy digs deeper and wider.")]
        public double energy = 7;
        [Tooltip("Base radius of a pellet hole in pixels.")]
        public double radius = 1.3;
        [Tooltip("How hard the body resists. Higher means shallower holes.")]
        public double toughness = 0.9;
        [Tooltip("How quickly power fades with distance from the muzzle (per 100 pixels, beyond 10).")]
        public double rangeFalloff = 0.3;
        [Tooltip("Straight-on only: scales how deep the holes go into the body.")]
        public double straightDepth = 0.4;

        public string DisplayName { get { return "Shotgun"; } }

        public void Generate(WoundContext ctx, List<GoreRemover> into)
        {
            var tuning = new ShotTuning
            {
                energy = energy, radius = radius, toughness = toughness, rangeFalloff = rangeFalloff,
                straightDepth = straightDepth, coneDeg = coneDeg, pellets = pellets,
            };
            for (int i = 0; i < ctx.members.Length; i++)
            {
                if (!GoreRecipeUtil.Usable(ctx, i)) continue;
                var made = new List<GoreRemover>();
                if (straightOn) GoreRecipeUtil.Straight(ctx, i, tuning, pellets, made);
                else GoreRecipeUtil.Shotgun(ctx, i, tuning, made);
                GoreRecipeUtil.Append(ctx, into, i, made);
            }
        }
    }
}
