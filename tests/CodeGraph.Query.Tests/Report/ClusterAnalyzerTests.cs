using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class ClusterAnalyzerTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string assembly)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, assembly) in defs)
            nodes[id] = new GraphNode { Id = id, Name = id, Kind = NodeKind.Type, AssemblyName = assembly };
        return nodes;
    }

    [Fact]
    public void Analyze_CountsInternalAndExternalEdges()
    {
        var nodes = CreateNodes(
            ("A", "Assembly1"),
            ("B", "Assembly1"),
            ("C", "Assembly2"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.DependsOn }
        };

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        var asm1 = clusters.Single(c => c.Assembly == "Assembly1");
        Assert.Equal(2, asm1.NodeCount);
        Assert.Equal(1, asm1.InternalEdges);
        Assert.Equal(1, asm1.ExternalEdges);

        var asm2 = clusters.Single(c => c.Assembly == "Assembly2");
        Assert.Equal(1, asm2.NodeCount);
        Assert.Equal(0, asm2.InternalEdges);
        Assert.Equal(1, asm2.ExternalEdges);
    }

    [Fact]
    public void Analyze_GroupsByAssembly()
    {
        var nodes = CreateNodes(
            ("A", "Alpha"),
            ("B", "Alpha"),
            ("C", "Beta"));

        var edges = new List<GraphEdge>();

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Assembly == "Alpha" && c.NodeCount == 2);
        Assert.Contains(clusters, c => c.Assembly == "Beta" && c.NodeCount == 1);
    }

    [Fact]
    public void Analyze_ExcludesNodesWithEmptyAssembly()
    {
        var nodes = CreateNodes(("A", "Assembly1"), ("B", ""));

        var clusters = ClusterAnalyzer.Analyze(nodes, new List<GraphEdge>());

        Assert.Single(clusters);
        Assert.Equal("Assembly1", clusters[0].Assembly);
    }

    [Fact]
    public void Analyze_EmptyGraph_ReturnsEmptyList()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        Assert.Empty(clusters);
    }

    [Fact]
    public void Analyze_SingleNode_ZeroEdges()
    {
        var nodes = CreateNodes(("A", "Assembly1"));
        var edges = new List<GraphEdge>();

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        Assert.Single(clusters);
        Assert.Equal(1, clusters[0].NodeCount);
        Assert.Equal(0, clusters[0].InternalEdges);
        Assert.Equal(0, clusters[0].ExternalEdges);
    }

    [Fact]
    public void Analyze_SortedByNodeCountDescending()
    {
        var nodes = CreateNodes(
            ("A", "Small"),
            ("B", "Big"),
            ("C", "Big"),
            ("D", "Big"));
        var edges = new List<GraphEdge>();

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        Assert.Equal("Big", clusters[0].Assembly);
        Assert.Equal(3, clusters[0].NodeCount);
        Assert.Equal("Small", clusters[1].Assembly);
        Assert.Equal(1, clusters[1].NodeCount);
    }

    [Fact]
    public void Analyze_BidirectionalCrossEdge_CountedForBothAssemblies()
    {
        var nodes = CreateNodes(("A", "Asm1"), ("B", "Asm2"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        var asm1 = clusters.Single(c => c.Assembly == "Asm1");
        Assert.Equal(1, asm1.ExternalEdges);
        Assert.Equal(0, asm1.InternalEdges);

        var asm2 = clusters.Single(c => c.Assembly == "Asm2");
        Assert.Equal(1, asm2.ExternalEdges);
        Assert.Equal(0, asm2.InternalEdges);
    }

    [Fact]
    public void Analyze_MultipleInternalEdges_CountedCorrectly()
    {
        var nodes = CreateNodes(("A", "Asm1"), ("B", "Asm1"), ("C", "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.DependsOn },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Implements }
        };

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);

        Assert.Single(clusters);
        Assert.Equal(3, clusters[0].InternalEdges);
        Assert.Equal(0, clusters[0].ExternalEdges);
    }
}
