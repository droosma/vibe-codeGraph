using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class ImpactAnalyzerTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildDependencyGraph()
    {
        // Controller -> Service -> Repository
        // TestClass covers Service
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Repository"] = new GraphNode { Id = "App.Repository", Name = "Repository", Kind = NodeKind.Type },
            ["App.Service"] = new GraphNode { Id = "App.Service", Name = "Service", Kind = NodeKind.Type },
            ["App.Controller"] = new GraphNode { Id = "App.Controller", Name = "Controller", Kind = NodeKind.Type },
            ["App.Tests.ServiceTest"] = new GraphNode { Id = "App.Tests.ServiceTest", Name = "ServiceTest", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "App.Controller", ToId = "App.Service", Type = EdgeType.Calls },
            new GraphEdge { FromId = "App.Service", ToId = "App.Repository", Type = EdgeType.Calls },
            new GraphEdge { FromId = "App.Tests.ServiceTest", ToId = "App.Service", Type = EdgeType.Covers }
        };
        return (nodes, edges);
    }

    [Fact]
    public void Analyze_SingleLayer_FindsDirectDependents()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Repository", maxDepth: 1);

        Assert.NotNull(result.Target);
        Assert.Equal("App.Repository", result.Target!.Id);
        Assert.Single(result.Layers);
        Assert.Single(result.Layers[0].Nodes);
        Assert.Equal("App.Service", result.Layers[0].Nodes[0].Id);
    }

    [Fact]
    public void Analyze_MultiLayer_TraversesTransitiveDependents()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Repository", maxDepth: 3);

        Assert.NotNull(result.Target);
        Assert.Equal(2, result.Layers.Count);
        // Layer 1: Service (calls Repository)
        Assert.Contains(result.Layers[0].Nodes, n => n.Id == "App.Service");
        // Layer 2: Controller (calls Service) + ServiceTest (covers Service)
        Assert.Contains(result.Layers[1].Nodes, n => n.Id == "App.Controller");
        Assert.Contains(result.Layers[1].Nodes, n => n.Id == "App.Tests.ServiceTest");
    }

    [Fact]
    public void Analyze_ExcludesContainsEdges()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Parent"] = new GraphNode { Id = "Parent", Name = "Parent", Kind = NodeKind.Type },
            ["Child"] = new GraphNode { Id = "Child", Name = "Child", Kind = NodeKind.Method },
            ["Caller"] = new GraphNode { Id = "Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "Parent", ToId = "Child", Type = EdgeType.Contains },
            new GraphEdge { FromId = "Caller", ToId = "Child", Type = EdgeType.Calls }
        };
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Child", maxDepth: 2);

        Assert.Single(result.Layers);
        // Only Caller, not Parent (Contains is excluded)
        Assert.Single(result.Layers[0].Nodes);
        Assert.Equal("Caller", result.Layers[0].Nodes[0].Id);
    }

    [Fact]
    public void Analyze_UnknownPattern_ReturnsEmptyResult()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("NonExistent");

        Assert.Null(result.Target);
        Assert.Empty(result.Layers);
        Assert.Equal(0, result.TotalAffected);
    }

    [Fact]
    public void Analyze_NoDependents_ReturnsEmptyLayers()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Controller");

        Assert.NotNull(result.Target);
        Assert.Empty(result.Layers);
        Assert.Equal(0, result.TotalAffected);
    }

    [Fact]
    public void Analyze_TotalAffected_SumsAllLayers()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Repository", maxDepth: 3);

        // Layer 1: Service, Layer 2: Controller + ServiceTest
        Assert.Equal(3, result.TotalAffected);
    }

    [Fact]
    public void Analyze_MatchesByName()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Repository");

        Assert.NotNull(result.Target);
        Assert.Equal("App.Repository", result.Target!.Id);
    }

    [Fact]
    public void Analyze_MaxDepthZero_ReturnsTargetWithoutLayers()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Repository", maxDepth: 0);

        Assert.NotNull(result.Target);
        Assert.Empty(result.Layers);
        Assert.Equal(0, result.TotalAffected);
    }

    [Fact]
    public void Analyze_LayersPreserveExactEdgesPerDepth()
    {
        var (nodes, edges) = BuildDependencyGraph();
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Repository", maxDepth: 3);

        Assert.Collection(result.Layers,
            layer =>
            {
                Assert.Equal(1, layer.Depth);
                var edge = Assert.Single(layer.Edges);
                Assert.Equal(("App.Service", "App.Repository", EdgeType.Calls), (edge.FromId, edge.ToId, edge.Type));
            },
            layer =>
            {
                Assert.Equal(2, layer.Depth);
                Assert.Equal(2, layer.Edges.Count);
                Assert.Contains(layer.Edges, edge => edge.FromId == "App.Controller" && edge.ToId == "App.Service" && edge.Type == EdgeType.Calls);
                Assert.Contains(layer.Edges, edge => edge.FromId == "App.Tests.ServiceTest" && edge.ToId == "App.Service" && edge.Type == EdgeType.Covers);
            });
    }

    [Fact]
    public void Analyze_UnknownCallerNode_PreservesLayerEdgeButOmitsMissingNode()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = new GraphNode { Id = "Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Missing.Caller", ToId = "Target", Type = EdgeType.Calls }
        };
        var analyzer = new ImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target", maxDepth: 1);

        var layer = Assert.Single(result.Layers);
        Assert.Empty(layer.Nodes);
        var edge = Assert.Single(layer.Edges);
        Assert.Equal(("Missing.Caller", "Target"), (edge.FromId, edge.ToId));
        Assert.Equal(0, result.TotalAffected);
    }
}
