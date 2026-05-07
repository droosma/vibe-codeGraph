using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class ImpactAnalyzer
{
    private readonly Dictionary<string, List<GraphEdge>> _incoming;
    private readonly Dictionary<string, GraphNode> _nodes;

    public ImpactAnalyzer(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _incoming = new Dictionary<string, List<GraphEdge>>();
        foreach (var edge in edges)
        {
            if (!_incoming.TryGetValue(edge.ToId, out var inList))
            { inList = new List<GraphEdge>(); _incoming[edge.ToId] = inList; }
            inList.Add(edge);
        }
    }

    public ImpactResult Analyze(string pattern, int maxDepth = 3)
    {
        var targetNodes = _nodes.Values
            .Where(n => n.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                || n.Id.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)
                || n.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetNodes.Count == 0)
            return new ImpactResult(pattern, null, new List<ImpactLayer>());

        var targetId = targetNodes[0].Id;
        var visited = new HashSet<string> { targetId };
        var layers = new List<ImpactLayer>();
        var currentLevel = new HashSet<string> { targetId };

        for (var depth = 1; depth <= maxDepth; depth++)
        {
            var nextLevel = new HashSet<string>();
            var layerEdges = new List<GraphEdge>();

            foreach (var nodeId in currentLevel)
            {
                if (!_incoming.TryGetValue(nodeId, out var inEdges)) continue;
                foreach (var edge in inEdges.Where(e => e.Type != EdgeType.Contains))
                {
                    if (visited.Add(edge.FromId))
                    {
                        nextLevel.Add(edge.FromId);
                        layerEdges.Add(edge);
                    }
                }
            }

            if (nextLevel.Count == 0) break;

            var layerNodes = nextLevel
                .Select(id => _nodes.TryGetValue(id, out var n) ? n : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();

            layers.Add(new ImpactLayer(depth, layerNodes, layerEdges));
            currentLevel = nextLevel;
        }

        return new ImpactResult(pattern, targetNodes[0], layers);
    }
}

public record ImpactResult(string Pattern, GraphNode? Target, List<ImpactLayer> Layers)
{
    public int TotalAffected => Layers.Sum(l => l.Nodes.Count);
}

public record ImpactLayer(int Depth, List<GraphNode> Nodes, List<GraphEdge> Edges);
