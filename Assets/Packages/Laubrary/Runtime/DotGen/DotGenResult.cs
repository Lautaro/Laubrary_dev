// DotGenResult.cs
// The transient product of one evaluation: areas, dots before and after mutation, and the traces a gizmo
// needs. None of it is ever serialized — it is rebuilt from the document and the seed, which is the whole
// point of a deterministic pipeline.

using System.Collections.Generic;

namespace Laubrary.DotGen
{
    /// A dot in its area's own local space (-0.5..0.5), straight out of a placement.
    public struct DotLocal
    {
        public float x, y;

        /// Stable index within the area — the placement decides it, and every hash that touches this dot uses
        /// it, so a dot keeps its random draw when its neighbours change.
        public int key;

        public bool hasCell;
        public float cellW, cellH;   // local units
        public float cellRot;        // radians, relative to the area
    }

    /// A dot in world (frame-normalized) space, with its cell carried along. Moving a dot moves its cell's
    /// centre; the cell's size and rotation do not change.
    public struct DotPoint
    {
        public float x, y;
        public int key;
        public bool hasCell;
        public float cellW, cellH;   // world units
        public float cellRot;        // world radians
        public DotArea area;
    }

    /// What one mutator did to one area, kept only long enough to draw it.
    public class DotMutatorTrace
    {
        public string moduleId;
        public DotMutator module;
        public DotArea area;
        public List<DotPoint> before = new List<DotPoint>();
        public List<DotPoint> after = new List<DotPoint>();
        public List<DotPoint> removed = new List<DotPoint>();
    }

    /// Everything one generator definition produced across all of its evaluated instances.
    public class DotGenGeneratorData
    {
        public DotGenerator gen;
        public List<DotArea> areas = new List<DotArea>();
        public List<DotPoint> baseDots = new List<DotPoint>();
        public List<DotPoint> finalDots = new List<DotPoint>();
        public List<DotMutatorTrace> traces = new List<DotMutatorTrace>();
        public List<string> childIds = new List<string>();
    }

    public class DotGenResult
    {
        /// Generator data in hierarchy order (depth first, parent before its children) — the same order the
        /// renderer paints in, so later siblings cover earlier ones.
        public List<DotGenGeneratorData> order = new List<DotGenGeneratorData>();

        public Dictionary<string, DotGenGeneratorData> byId = new Dictionary<string, DotGenGeneratorData>();

        /// Dots that would be drawn: enabled generators with Dot output on.
        public int totalVisibleDots;

        /// Every evaluated generator area in the document.
        public int totalAreas;

        public DotGenGeneratorData For(DotGenerator g)
            => g != null && byId.TryGetValue(g.id, out var d) ? d : null;
    }
}
