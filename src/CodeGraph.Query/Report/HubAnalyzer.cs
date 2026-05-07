using CodeGraph.Core.Models;

namespace CodeGraph.Query.Report;

public static class HubAnalyzer
{
    public record HubNode(string Id, string Name, NodeKind Kind, int InDegree, int OutDegree, int TotalDegree);

    public static List<HubNode> FindHubs(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        int topN = 10)
    {
        var inDegree = new Dictionary<string, int>();
        var outDegree = new Dictionary<string, int>();

        foreach (var edge in edges.Where(e => e.Type != EdgeType.Contains))
        {
            inDegree[edge.ToId] = inDegree.GetValueOrDefault(edge.ToId) + 1;
            outDegree[edge.FromId] = outDegree.GetValueOrDefault(edge.FromId) + 1;
        }

        return nodes.Values
            .Where(n => n.Kind == NodeKind.Type)
            .Select(n => new HubNode(
                n.Id, n.Name, n.Kind,
                inDegree.GetValueOrDefault(n.Id),
                outDegree.GetValueOrDefault(n.Id),
                inDegree.GetValueOrDefault(n.Id) + outDegree.GetValueOrDefault(n.Id)))
            .OrderByDescending(h => h.TotalDegree)
            .Take(topN)
            .ToList();
    }
}
