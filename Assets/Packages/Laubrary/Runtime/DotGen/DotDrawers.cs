// DotDrawers.cs
// The Fill drawer: the one module that puts actual paint on the frame. Everything else in DotGen decides
// WHERE things are; this decides what is visible.
//
// The paint itself is a ZuiFill, Laubrary's own fill type, rather than a private colour-pair. The reference's
// "flat" is a Solid ZuiFill and its "gradient A to B at an angle" is a Linear one, so nothing is lost — and a
// DotGen fill gains radial gradients, textures and animated gradients for free, and is authored with the same
// control every other Laubrary tool uses.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// What a drawer paints.
    public enum DotDrawTargetKind { GeneratorAreas, PlacementCells }

    /// Which side of the selector a cell drawer keeps.
    public enum DotDrawerUse { Selected, Inverse }

    /// Where the paint for one target comes from.
    public enum DotFillSource { SingleFill, RandomFromList }

    /// Paints generator areas, or the cells around surviving dots.
    [Serializable]
    [DotModule("fill", "Fill drawer", "Fill shapes", Order = 0)]
    public class DotFillDrawer : DotDrawer
    {
        [Tooltip("Paint each whole generator area, or the cell around each surviving dot.")]
        public DotDrawTargetKind drawTarget = DotDrawTargetKind.GeneratorAreas;

        [ZUIShowIf("drawTarget", "PlacementCells")]
        [Tooltip("Keep the cells the selector picked, or the ones it did not.")]
        public DotDrawerUse use = DotDrawerUse.Selected;

        [Range(0f, 100f)]
        [ZUIShowIf("drawTarget", "PlacementCells")]
        [Tooltip("How much weight a cell needs before it gets painted.")]
        public float threshold = 50f;

        [ZUIShowIf("drawTarget", "PlacementCells")]
        [Tooltip("The shape painted inside each cell.")]
        public DotShape cellShape = DotShape.Rectangle;

        [Range(1f, 120f)]
        [ZUIShowIf("drawTarget", "PlacementCells")]
        [Tooltip("Width of the painted shape, as a percentage of the cell.")]
        public float cellWidth = 62f;

        [Range(1f, 120f)]
        [ZUIShowIf("drawTarget", "PlacementCells")]
        [Tooltip("Height of the painted shape, as a percentage of the cell.")]
        public float cellHeight = 62f;

        [Range(0f, 100f)]
        [Tooltip("How solid the paint is over whatever is already there.")]
        public float opacity = 100f;

        [Tooltip("Paint every target the same, or pick from a list so the targets vary.")]
        public DotFillSource fillSource = DotFillSource.SingleFill;

        [ZUIShowIf("fillSource", "SingleFill")]
        [Tooltip("The paint used for every target.")]
        public ZuiFill fill = DotGenFills.Solid("#26384a");

        [Range(0, 999)]
        [ZUIShowIf("fillSource", "RandomFromList")]
        [Tooltip("Changes which target gets which fill, without changing the list.")]
        public int variationSeed = 211;

        [ZUIShowIf("fillSource", "RandomFromList")]
        [Tooltip("The fills to choose between. A target keeps its choice for a given seed.")]
        public List<ZuiFill> fills = DotGenFills.DefaultList();

        public override float OpacityFraction => opacity / 100f;

        public override ZuiFill FillFor(int fillKey, int globalSeed)
        {
            if (fillSource == DotFillSource.RandomFromList && fills != null && fills.Count > 0)
            {
                int pick = Mathf.Min(fills.Count - 1,
                    (int)System.Math.Floor(DotGenMath.Hash01(globalSeed + variationSeed, fillKey, id != null ? id.Length : 0) * fills.Count));
                var f = fills[Mathf.Clamp(pick, 0, fills.Count - 1)];
                if (f != null) return f;
            }
            return fill;
        }

        public override void Targets(DotGenerator gen, DotGenGeneratorData gd, int globalSeed, List<DotDrawTarget> outTargets)
        {
            if (gd == null) return;

            if (drawTarget == DotDrawTargetKind.GeneratorAreas)
            {
                for (int i = 0; i < gd.areas.Count; i++)
                    outTargets.Add(new DotDrawTarget { area = gd.areas[i], shape = gen.shape, fillKey = i + gd.areas[i].idx * 997 });
                return;
            }

            var sel = gen.ResolveSelector(selectorId);
            float cut = threshold / 100f;

            for (int i = 0; i < gd.finalDots.Count; i++)
            {
                var p = gd.finalDots[i];
                if (!p.hasCell || p.cellW == 0f || p.cellH == 0f) continue;

                float w = sel == null ? 1f : sel.Weight(gen, p.area, p.x, p.y, i, globalSeed);
                if (use == DotDrawerUse.Inverse) w = 1f - w;
                if (w < cut) continue;

                outTargets.Add(new DotDrawTarget
                {
                    area = new DotArea
                    {
                        cx = p.x, cy = p.y,
                        w = p.cellW * cellWidth / 100f,
                        h = p.cellH * cellHeight / 100f,
                        rot = p.cellRot,
                        anchorX = p.x, anchorY = p.y,
                        idx = p.area.idx
                    },
                    shape = cellShape,
                    fillKey = i
                });
            }
        }
    }

    /// The documented default fills, built once here so the drawer's field initializers and any migration
    /// cannot drift apart.
    public static class DotGenFills
    {
        public static ZuiFill Solid(string hex) => new ZuiFill(DotGenMath.Hex(hex));

        public static ZuiFill Linear(string hexA, string hexB, float angleDeg)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(DotGenMath.Hex(hexA), 0f), new GradientColorKey(DotGenMath.Hex(hexB), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ZuiFill
            {
                mode = ZuiFill.Mode.Linear,
                color = DotGenMath.Hex(hexA),
                gradient = g,
                angleDeg = angleDeg
            };
        }

        public static List<ZuiFill> DefaultList() => new List<ZuiFill>
        {
            Solid("#26384a"),
            Linear("#293449", "#79b4d2", 90f),
            Linear("#463247", "#d38d78", 90f)
        };

        /// A new entry added from the card's "+ Flat" / "+ Gradient" buttons.
        public static ZuiFill NewFlat() => Solid("#667788");
        public static ZuiFill NewGradient() => Linear("#293449", "#79b4d2", 90f);
    }
}
