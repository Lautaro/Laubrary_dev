using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Loom;

namespace Laubrary.Story
{
    // The authored asset: a branching graph of Pages plus the wiring between them. Run by a StoryRunner.
    // Pages are [SerializeReference] so games can store their own Page subclasses here, exactly like rules.
    // Implements Loom's IRunnableGraph (the runner walks it) + IGraphAsset (the shared graph editor authors it).
    [CreateAssetMenu(menuName = "Laubrary/Story/Screenplay", fileName = "Screenplay")]
    public class Screenplay : ScriptableObject, IRunnableGraph<Page>, IGraphAsset
    {
        [SerializeReference] public List<Page> Pages = new List<Page>();
        public List<Edge> Edges = new List<Edge>();
        public string EntryId;

        public Page GetPage(string id)
        {
            if (string.IsNullOrEmpty(id) || Pages == null) return null;
            for (int i = 0; i < Pages.Count; i++)
                if (Pages[i] != null && Pages[i].Id == id) return Pages[i];
            return null;
        }

        // IRunnableGraph<Page>
        public Page GetNode(string id) => GetPage(id);

        public IEnumerable<Edge> OutEdges(string fromId)
        {
            if (Edges == null) yield break;
            for (int i = 0; i < Edges.Count; i++)
                if (Edges[i].From == fromId) yield return Edges[i];
        }

        // The starting page: the explicit EntryId, else the first EntryPage, else the first page.
        public string ResolveEntry()
        {
            if (!string.IsNullOrEmpty(EntryId) && GetPage(EntryId) != null) return EntryId;
            if (Pages != null)
            {
                for (int i = 0; i < Pages.Count; i++) if (Pages[i] is EntryPage) return Pages[i].Id;
                if (Pages.Count > 0 && Pages[0] != null) return Pages[0].Id;
            }
            return null;
        }

        // ── IGraphAsset (shared Loom editor) ────────────────────────────────────────────────────────────────
        IReadOnlyList<INode> IGraphAsset.Nodes => Pages;   // covariant: List<Page> → IReadOnlyList<INode>
        List<Edge> IGraphAsset.Edges { get => Edges; set => Edges = value; }
        string IGraphAsset.EntryId { get => EntryId; set => EntryId = value; }
        INode IGraphAsset.GetNodeUntyped(string id) => GetPage(id);
        Type IGraphAsset.NodeBaseType => typeof(Page);
        bool IGraphAsset.IsEntryNode(INode node) => node is EntryPage;
        UnityEngine.Object IGraphAsset.AssetObject => this;

        INode IGraphAsset.AddNode(Type nodeType, string id, Vector2 pos)
        {
            var p = (Page)Activator.CreateInstance(nodeType);
            p.Id = id; p.GraphPos = pos;
            Pages.Add(p);
            return p;
        }

        void IGraphAsset.RebuildNodes(IReadOnlyList<INode> nodesInOrder)
        {
            Pages = new List<Page>(nodesInOrder.Count);
            foreach (var n in nodesInOrder) if (n is Page pg) Pages.Add(pg);
        }
    }
}
