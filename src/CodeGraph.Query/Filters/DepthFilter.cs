using CodeGraph.Core.Models;

namespace CodeGraph.Query.Filters;

public static class DepthFilter
{
    /// <summary>
    /// BFS traversal from seed node IDs up to the specified depth.
    /// Returns the set of all reachable node IDs within depth hops.
    /// </summary>
    public static HashSet<string> Traverse(
        IEnumerable<string> seedIds,
        Dictionary<string, List<GraphEdge>> outgoing,
        Dictionary<string, List<GraphEdge>> incoming,
        int depth,
        bool includeExternal)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<(string Id, int CurrentDepth)>();

        foreach (var id in seedIds)
        {
            if (visited.Add(id))
                queue.Enqueue((id, 0));
        }

        while (queue.Count > 0)
        {
            var (current, currentDepth) = queue.Dequeue();

            if (currentDepth >= depth)
                continue;

            var neighbors = GetNeighbors(current, outgoing, incoming, includeExternal);
            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                    queue.Enqueue((neighbor, currentDepth + 1));
            }
        }

        return visited;
    }

    private static IEnumerable<string> GetNeighbors(
        string nodeId,
        Dictionary<string, List<GraphEdge>> outgoing,
        Dictionary<string, List<GraphEdge>> incoming,
        bool includeExternal)
    {
        if (outgoing.TryGetValue(nodeId, out var outEdges))
        {
            foreach (var edge in outEdges)
            {
                if (includeExternal || !edge.IsExternal)
                    yield return edge.ToId;
            }
        }

        if (incoming.TryGetValue(nodeId, out var inEdges))
        {
            foreach (var edge in inEdges)
            {
                if (includeExternal || !edge.IsExternal)
                    yield return edge.FromId;
            }
        }
    }
}
