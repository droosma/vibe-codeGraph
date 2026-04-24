using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public enum GraphDiffChangeType
{
    AddedNodes,
    RemovedNodes,
    SignatureChangedNodes,
    AddedEdges,
    RemovedEdges
}

public record GraphSignatureChange
{
    public GraphNode Previous { get; init; } = null!;
    public GraphNode Current { get; init; } = null!;
}

public record GraphDiffResult
{
    public GraphMetadata BaseMetadata { get; init; } = null!;
    public GraphMetadata HeadMetadata { get; init; } = null!;
    public List<GraphNode> AddedNodes { get; init; } = new();
    public List<GraphNode> RemovedNodes { get; init; } = new();
    public List<GraphSignatureChange> SignatureChangedNodes { get; init; } = new();
    public List<GraphEdge> AddedEdges { get; init; } = new();
    public List<GraphEdge> RemovedEdges { get; init; } = new();
}

public static class GraphDiffEngine
{
    public static GraphDiffResult Compare(
        GraphMetadata baseMetadata,
        Dictionary<string, GraphNode> baseNodes,
        List<GraphEdge> baseEdges,
        GraphMetadata headMetadata,
        Dictionary<string, GraphNode> headNodes,
        List<GraphEdge> headEdges)
    {
        var addedNodes = headNodes.Values
            .Where(node => !baseNodes.ContainsKey(node.Id))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var removedNodes = baseNodes.Values
            .Where(node => !headNodes.ContainsKey(node.Id))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var signatureChangedNodes = baseNodes.Keys
            .Intersect(headNodes.Keys, StringComparer.Ordinal)
            .Select(id => (Previous: baseNodes[id], Current: headNodes[id]))
            .Where(pair => !string.Equals(
                pair.Previous.Signature ?? string.Empty,
                pair.Current.Signature ?? string.Empty,
                StringComparison.Ordinal))
            .Select(pair => new GraphSignatureChange { Previous = pair.Previous, Current = pair.Current })
            .OrderBy(change => change.Current.Id, StringComparer.Ordinal)
            .ToList();

        var baseEdgeMap = baseEdges
            .GroupBy(GetEdgeKey)
            .ToDictionary(group => group.Key, group => group.First());
        var headEdgeMap = headEdges
            .GroupBy(GetEdgeKey)
            .ToDictionary(group => group.Key, group => group.First());

        var addedEdges = headEdgeMap.Keys
            .Except(baseEdgeMap.Keys)
            .Select(key => headEdgeMap[key])
            .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Type.ToString(), StringComparer.Ordinal)
            .ToList();

        var removedEdges = baseEdgeMap.Keys
            .Except(headEdgeMap.Keys)
            .Select(key => baseEdgeMap[key])
            .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Type.ToString(), StringComparer.Ordinal)
            .ToList();

        return new GraphDiffResult
        {
            BaseMetadata = baseMetadata,
            HeadMetadata = headMetadata,
            AddedNodes = addedNodes,
            RemovedNodes = removedNodes,
            SignatureChangedNodes = signatureChangedNodes,
            AddedEdges = addedEdges,
            RemovedEdges = removedEdges
        };
    }

    private static EdgeKey GetEdgeKey(GraphEdge edge)
    {
        return new EdgeKey(
            edge.FromId,
            edge.ToId,
            edge.Type,
            edge.IsExternal,
            edge.Resolution ?? string.Empty);
    }

    private readonly record struct EdgeKey(
        string FromId,
        string ToId,
        EdgeType Type,
        bool IsExternal,
        string Resolution);
}
