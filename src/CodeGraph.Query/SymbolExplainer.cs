using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class SymbolExplainer
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, List<GraphEdge>> _outgoing;
    private readonly Dictionary<string, List<GraphEdge>> _incoming;

    public SymbolExplainer(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
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

    public ExplainResult? Explain(string pattern)
    {
        var node = _nodes.Values
            .FirstOrDefault(n => n.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                || n.Id.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)
                || n.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase));

        if (node is null) return null;

        var outEdges = _outgoing.GetValueOrDefault(node.Id, new List<GraphEdge>());
        var inEdges = _incoming.GetValueOrDefault(node.Id, new List<GraphEdge>());

        // Members (Contains edges FROM this node)
        var members = outEdges
            .Where(e => e.Type == EdgeType.Contains)
            .Select(e => _nodes.TryGetValue(e.ToId, out var n) ? n : null)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        // Test coverage
        var tests = inEdges
            .Where(e => e.Type == EdgeType.Covers || e.Type == EdgeType.CoveredBy)
            .Concat(outEdges.Where(e => e.Type == EdgeType.Covers || e.Type == EdgeType.CoveredBy))
            .Select(e => e.FromId == node.Id ? e.ToId : e.FromId)
            .Distinct()
            .Select(id => _nodes.TryGetValue(id, out var n) ? n : null)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        return new ExplainResult(node, outEdges, inEdges, members, tests);
    }
}

public record ExplainResult(
    GraphNode Node,
    List<GraphEdge> OutgoingEdges,
    List<GraphEdge> IncomingEdges,
    List<GraphNode> Members,
    List<GraphNode> Tests);
