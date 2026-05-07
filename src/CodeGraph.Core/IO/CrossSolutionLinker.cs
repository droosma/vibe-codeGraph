using CodeGraph.Core.Models;

namespace CodeGraph.Core.IO;

/// <summary>
/// Resolves external edges that point to nodes defined in other solutions within
/// the same mono-repo. Resolved edges are tagged with
/// <see cref="EdgeConfidence.Inferred"/> and <c>cross_solution=true</c> metadata.
/// </summary>
public static class CrossSolutionLinker
{
    /// <summary>
    /// Scans <paramref name="unifiedEdges"/> for external edges whose target exists
    /// as an internal node in <paramref name="unifiedNodes"/>. Returns replacement
    /// edges with <see cref="EdgeConfidence.Inferred"/> and a list of external IDs
    /// that were successfully resolved.
    /// </summary>
    public static (List<GraphEdge> NewEdges, List<string> ResolvedExternalIds) Link(
        Dictionary<string, GraphNode> unifiedNodes,
        List<GraphEdge> unifiedEdges)
    {
        var newEdges = new List<GraphEdge>();
        var resolvedIds = new HashSet<string>();

        // Build a name → node lookup for fuzzy matching
        var nodesByName = new Dictionary<string, List<GraphNode>>();
        foreach (var node in unifiedNodes.Values)
        {
            if (!nodesByName.TryGetValue(node.Name, out var list))
            {
                list = new List<GraphNode>();
                nodesByName[node.Name] = list;
            }
            list.Add(node);
        }

        foreach (var edge in unifiedEdges)
        {
            if (!edge.IsExternal)
                continue;

            // Exact ID match — external target is defined in another solution
            if (unifiedNodes.ContainsKey(edge.ToId))
            {
                newEdges.Add(edge with
                {
                    IsExternal = false,
                    Confidence = EdgeConfidence.Inferred,
                    Resolution = "cross-solution",
                    Metadata = new Dictionary<string, string>(edge.Metadata)
                    {
                        ["cross_solution"] = "true"
                    }
                });
                resolvedIds.Add(edge.ToId);
                continue;
            }

            // Fuzzy match: last segment of the external ID matches exactly one
            // internal node by name
            var targetName = edge.ToId.Contains('.')
                ? edge.ToId.Substring(edge.ToId.LastIndexOf('.') + 1)
                : edge.ToId;

            if (nodesByName.TryGetValue(targetName, out var candidates))
            {
                var filtered = candidates
                    .Where(n => n.Id != edge.FromId)
                    .ToList();

                if (filtered.Count == 1)
                {
                    newEdges.Add(new GraphEdge
                    {
                        FromId = edge.FromId,
                        ToId = filtered[0].Id,
                        Type = edge.Type,
                        IsExternal = false,
                        Confidence = EdgeConfidence.Inferred,
                        Resolution = "cross-solution-fuzzy",
                        Metadata = new Dictionary<string, string>(edge.Metadata)
                        {
                            ["cross_solution"] = "true",
                            ["original_target"] = edge.ToId
                        }
                    });
                    resolvedIds.Add(edge.ToId);
                }
            }
        }

        return (newEdges, resolvedIds.ToList());
    }
}
