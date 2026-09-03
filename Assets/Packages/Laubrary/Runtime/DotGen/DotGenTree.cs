// DotGenTree.cs
// The hierarchy view of a flat generator list. Built on demand and thrown away on any structural edit — it
// holds nothing the document does not already say, so it can never disagree with it.

using System.Collections.Generic;

namespace Laubrary.DotGen
{
    public class DotGenTree
    {
        readonly Dictionary<string, DotGenerator> _byId = new Dictionary<string, DotGenerator>();
        readonly Dictionary<string, List<DotGenerator>> _children = new Dictionary<string, List<DotGenerator>>();
        static readonly List<DotGenerator> Empty = new List<DotGenerator>();

        public DotGenerator Root { get; private set; }

        public DotGenTree(IList<DotGenerator> generators)
        {
            if (generators == null) return;

            for (int i = 0; i < generators.Count; i++)
            {
                var g = generators[i];
                if (g == null || string.IsNullOrEmpty(g.id)) continue;
                _byId[g.id] = g;
            }

            for (int i = 0; i < generators.Count; i++)
            {
                var g = generators[i];
                if (g == null || string.IsNullOrEmpty(g.id)) continue;

                if (string.IsNullOrEmpty(g.parentId))
                {
                    // Exactly one root. A second entry with no parent is an authoring accident, not a second
                    // document; adopting it keeps its work visible instead of silently dropping it.
                    if (Root == null) { Root = g; continue; }
                    g.parentId = Root.id;
                }

                if (!_byId.ContainsKey(g.parentId))
                {
                    if (Root == null) { Root = g; g.parentId = ""; continue; }
                    g.parentId = Root.id;
                }

                if (!_children.TryGetValue(g.parentId, out var list))
                    _children[g.parentId] = list = new List<DotGenerator>();
                list.Add(g);
            }
        }

        public DotGenerator Find(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var g) ? g : null;

        public IReadOnlyList<DotGenerator> ChildrenOf(DotGenerator g)
            => g != null && _children.TryGetValue(g.id, out var list) ? (IReadOnlyList<DotGenerator>)list : Empty;

        public DotGenerator ParentOf(DotGenerator g)
            => g == null ? null : Find(g.parentId);

        public int DepthOf(DotGenerator g)
        {
            int d = 0;
            var p = ParentOf(g);
            while (p != null && d < 64) { d++; p = ParentOf(p); }
            return d;
        }

        /// Depth-first, parent before its children — the order everything renders and reports in.
        public void Walk(DotGenerator from, List<DotGenerator> outList)
        {
            if (from == null) return;
            outList.Add(from);
            var kids = ChildrenOf(from);
            for (int i = 0; i < kids.Count; i++) Walk(kids[i], outList);
        }

        public List<DotGenerator> Flatten()
        {
            var list = new List<DotGenerator>();
            Walk(Root, list);
            return list;
        }
    }
}
