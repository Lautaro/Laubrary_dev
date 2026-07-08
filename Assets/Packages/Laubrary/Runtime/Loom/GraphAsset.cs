using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Loom
{
    // Structural view of a node for the graph EDITOR (position, header, ports). The runtime lifecycle is the
    // separate IGraphNode<TCtx>; a concrete node implements both. Kept property-based so the editor can read/write
    // position + id generically.
    public interface INode
    {
        string Id { get; set; }
        Vector2 GraphPos { get; set; }
        string DisplayTitle { get; }              // node header text (may fold in the type name)
        IReadOnlyList<string> Ports { get; }      // output ports
        string Describe();
    }

    // Structural view of a graph ASSET for the editor: enumerate / add / rebuild nodes, own the edge list + entry.
    // Both Screenplay (Story) and Brain (Daemon) implement this so ONE GraphView window authors either.
    public interface IGraphAsset
    {
        IReadOnlyList<INode> Nodes { get; }
        List<Edge> Edges { get; set; }
        string EntryId { get; set; }

        INode GetNodeUntyped(string id);
        string ResolveEntry();

        // The base node type — the editor's palette lists TypeCache.GetTypesDerivedFrom(NodeBaseType).
        Type NodeBaseType { get; }

        // Create a node of `nodeType`, assign id + position, append it, and return it.
        INode AddNode(Type nodeType, string id, Vector2 pos);

        // Replace the node list (in the given order) after the view rearranged / removed nodes.
        void RebuildNodes(IReadOnlyList<INode> nodesInOrder);

        // Whether `node` is an entry-type node (no input port; auto-entry). Replaces the hardwired EntryPage check.
        bool IsEntryNode(INode node);

        // The UnityEngine.Object to mark dirty on edits (the ScriptableObject asset itself).
        UnityEngine.Object AssetObject { get; }
    }
}
