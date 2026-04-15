using CodeGraph.Core.Models;

namespace CodeGraph.Query.Filters;

public static class RankingStrategy
{
    /// <summary>
    /// Ranks nodes according to priority:
    /// 1. Direct edges before transitive
    /// 2. Same project before cross-project
    /// 3. Internal before external
    /// 4. Methods before types before namespaces
    /// 5. Nodes with doc comments before undocumented
    /// </summary>
    public static List<GraphNode> Rank(
        List<GraphNode> nodes,
        HashSet<string> directNeighborIds,
        HashSet<string> externalNodeIds,
        string? targetProject)
    {
        return nodes
            .OrderByDescending(n => directNeighborIds.Contains(n.Id) ? 1 : 0)
            .ThenByDescending(n => IsSameProject(n, targetProject) ? 1 : 0)
            .ThenByDescending(n => externalNodeIds.Contains(n.Id) ? 0 : 1)
            .ThenBy(n => KindPriority(n.Kind))
            .ThenByDescending(n => n.DocComment is not null ? 1 : 0)
            .ThenBy(n => n.Id)
            .ToList();
    }

    private static int KindPriority(NodeKind kind) => kind switch
    {
        NodeKind.Method => 0,
        NodeKind.Constructor => 0,
        NodeKind.Property => 1,
        NodeKind.Field => 1,
        NodeKind.Event => 1,
        NodeKind.Type => 2,
        NodeKind.Namespace => 3,
        _ => 4
    };

    private static bool IsSameProject(GraphNode node, string? targetProject)
    {
        if (targetProject is null) return true;
        // Check if the node's Id starts with the target project prefix
        return node.Id.StartsWith(targetProject, StringComparison.OrdinalIgnoreCase);
    }
}
