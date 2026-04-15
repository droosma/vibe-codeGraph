using CodeGraph.Core.Models;

namespace CodeGraph.Core.IO;

public class GraphMerger
{
    /// <summary>
    /// Merges a partial graph update into an existing full graph.
    /// Projects present in the partial graph replace their counterparts in the full graph.
    /// Projects not in the partial graph remain unchanged.
    /// </summary>
    public (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) Merge(
        Dictionary<string, GraphNode> existingNodes,
        List<GraphEdge> existingEdges,
        IEnumerable<ProjectGraph> partialGraphs)
    {
        var partialProjects = partialGraphs.ToList();
        var projectKeys = new HashSet<string>(
            partialProjects.Select(p => p.ProjectOrNamespace));

        // Remove existing nodes/edges that belong to projects being updated
        var mergedNodes = new Dictionary<string, GraphNode>(existingNodes);
        var mergedEdges = new List<GraphEdge>(existingEdges);

        // Collect IDs of nodes that belong to updated projects
        var nodeIdsToRemove = new HashSet<string>();
        foreach (var (id, _) in mergedNodes)
        {
            var project = ExtractProject(id);
            if (projectKeys.Contains(project))
                nodeIdsToRemove.Add(id);
        }

        foreach (var id in nodeIdsToRemove)
            mergedNodes.Remove(id);

        // Remove edges where source belongs to an updated project
        mergedEdges.RemoveAll(e => nodeIdsToRemove.Contains(e.FromId));

        // Add nodes and edges from partial graphs
        foreach (var pg in partialProjects)
        {
            foreach (var (id, node) in pg.Nodes)
                mergedNodes[id] = node;

            mergedEdges.AddRange(pg.Edges);
        }

        return (mergedNodes, mergedEdges);
    }

    private static string ExtractProject(string fullyQualifiedId)
    {
        var dotIndex = fullyQualifiedId.IndexOf('.');
        return dotIndex > 0 ? fullyQualifiedId[..dotIndex] : fullyQualifiedId;
    }
}
