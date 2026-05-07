using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class HubAnalyzerTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string name, NodeKind kind)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, name, kind) in defs)
            nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = "TestAssembly" };
        return nodes;
    }

    [Fact]
    public void FindHubs_ReturnsCorrectDegreeCounts()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type),
            ("B", "TypeB", NodeKind.Type),
            ("C", "TypeC", NodeKind.Type));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.DependsOn }
        };

        var hubs = HubAnalyzer.FindHubs(nodes, edges, topN: 10);

        var hubA = hubs.Single(h => h.Id == "A");
        Assert.Equal(0, hubA.InDegree);
        Assert.Equal(2, hubA.OutDegree);
        Assert.Equal(2, hubA.TotalDegree);

        var hubC = hubs.Single(h => h.Id == "C");
        Assert.Equal(2, hubC.InDegree);
        Assert.Equal(0, hubC.OutDegree);
        Assert.Equal(2, hubC.TotalDegree);
    }

    [Fact]
    public void FindHubs_ExcludesContainsEdges()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type),
            ("B", "TypeB", NodeKind.Type));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Contains },
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var hubs = HubAnalyzer.FindHubs(nodes, edges);

        var hubA = hubs.Single(h => h.Id == "A");
        Assert.Equal(1, hubA.OutDegree);
        Assert.Equal(1, hubA.TotalDegree);
    }

    [Fact]
    public void FindHubs_ReturnsTopN()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type),
            ("B", "TypeB", NodeKind.Type),
            ("C", "TypeC", NodeKind.Type));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.DependsOn }
        };

        var hubs = HubAnalyzer.FindHubs(nodes, edges, topN: 1);

        Assert.Single(hubs);
    }

    [Fact]
    public void FindHubs_OnlyIncludesTypeNodes()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type),
            ("M", "MethodM", NodeKind.Method));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "M", ToId = "A", Type = EdgeType.Calls }
        };

        var hubs = HubAnalyzer.FindHubs(nodes, edges);

        Assert.All(hubs, h => Assert.Equal(NodeKind.Type, h.Kind));
    }
}
