using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class PathFinder
{
    private readonly Dictionary<string, List<GraphEdge>> _outgoing;
    private readonly Dictionary<string, List<GraphEdge>> _incoming;
    private readonly Dictionary<string, GraphNode> _nodes;

    public PathFinder(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _outgoing = new Dictionary<string, List<GraphEdge>>();
        _incoming = new Dictionary<string, List<GraphEdge>>();
        foreach (var edge in edges)
        {
            if (!_outgoing.TryGetValue(edge.FromId, out var outList))
            { outList = new List<GraphEdge>(); _outgoing[edge.FromId] = outList; }
            outList.Add(edge);
            if (!_incoming.TryGetValue(edge.ToId, out var inList))
            { inList = new List<GraphEdge>(); _incoming[edge.ToId] = inList; }
            inList.Add(edge);
        }
    }

    /// <summary>Find shortest path from source to target. Returns null if no path exists.</summary>
    public PathResult? FindPath(string fromPattern, string toPattern, int maxDepth = 10)
    {
        var fromNodes = FindMatchingNodes(fromPattern);
        var toNodes = FindMatchingNodes(toPattern);

        if (fromNodes.Count == 0 || toNodes.Count == 0)
            return null;

        var fromId = fromNodes[0].Id;
        var toIds = new HashSet<string>(toNodes.Select(n => n.Id));

        // BFS from source
        var visited = new Dictionary<string, (string PrevId, GraphEdge Edge)>();
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((fromId, 0));
        visited[fromId] = (null!, null!);

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;

            if (toIds.Contains(current) && current != fromId)
                return ReconstructPath(fromId, current, visited);

            if (_outgoing.TryGetValue(current, out var outEdges))
            {
                foreach (var edge in outEdges)
                {
                    if (!visited.ContainsKey(edge.ToId))
                    {
                        visited[edge.ToId] = (current, edge);
                        queue.Enqueue((edge.ToId, depth + 1));
                        if (toIds.Contains(edge.ToId))
                            return ReconstructPath(fromId, edge.ToId, visited);
                    }
                }
            }
        }

        return null;
    }

    private PathResult ReconstructPath(string fromId, string toId, Dictionary<string, (string PrevId, GraphEdge Edge)> visited)
    {
        var steps = new List<PathStep>();
        var current = toId;
        while (current != fromId)
        {
            var (prev, edge) = visited[current];
            _nodes.TryGetValue(current, out var node);
            steps.Insert(0, new PathStep(prev, current, edge, node));
            current = prev;
        }
        _nodes.TryGetValue(fromId, out var fromNode);
        return new PathResult(fromId, toId, fromNode, steps);
    }

    private List<GraphNode> FindMatchingNodes(string pattern)
    {
        return _nodes.Values
            .Where(n => n.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                || n.Id.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)
                || n.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public record PathResult(string FromId, string ToId, GraphNode? FromNode, List<PathStep> Steps);
public record PathStep(string FromId, string ToId, GraphEdge Edge, GraphNode? ToNode);
