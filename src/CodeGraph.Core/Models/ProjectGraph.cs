namespace CodeGraph.Core.Models;

public record ProjectGraph
{
    public string ProjectOrNamespace { get; init; } = string.Empty;
    public Dictionary<string, GraphNode> Nodes { get; init; } = new();
    public List<GraphEdge> Edges { get; init; } = new();
}
