using System.Reflection;
using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class PathFinderRemainingMutationTests
{
    [Fact]
    public void FindPath_SourceAndTargetSameNode_ReturnsNull()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "A");

        Assert.Null(result);
    }

    [Fact]
    public void FindPath_MaxDepthExactBoundary_ReturnsPath()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "C", maxDepth: 2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("B", result.Steps[0].ToId);
        Assert.Equal("C", result.Steps[1].ToId);
    }

    [Fact]
    public void FindPath_ReconstructPath_PopulatesStepTargetNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "C");

        Assert.NotNull(result);
        Assert.Collection(result.Steps,
            step =>
            {
                Assert.NotNull(step.ToNode);
                Assert.Equal("B", step.ToNode!.Id);
            },
            step =>
            {
                Assert.NotNull(step.ToNode);
                Assert.Equal("C", step.ToNode!.Id);
            });
    }

    [Fact]
    public void FindPath_MatchesByQualifiedSuffix()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Root.Feature.Start"] = new() { Id = "Root.Feature.Start", Name = "Start", Kind = NodeKind.Type },
            ["Root.Feature.Target"] = new() { Id = "Root.Feature.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Root.Feature.Start", ToId = "Root.Feature.Target", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("Feature.Start", "Feature.Target");

        Assert.NotNull(result);
        var step = Assert.Single(result.Steps);
        Assert.Equal("Root.Feature.Start", step.FromId);
        Assert.Equal("Root.Feature.Target", step.ToId);
    }

    [Fact]
    public void Constructor_PopulatesIncomingAdjacencyForTargetNode()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type }
        };
        var edge = new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls };
        var finder = new PathFinder(nodes, new List<GraphEdge> { edge });

        var incomingField = typeof(PathFinder).GetField("_incoming", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(incomingField);

        var incoming = Assert.IsType<Dictionary<string, List<GraphEdge>>>(incomingField!.GetValue(finder));
        var incomingEdges = Assert.Single(incoming["B"]);
        Assert.Same(edge, incomingEdges);
    }

    [Fact]
    public void FindPath_NonSegmentSuffix_DoesNotMatchQualifiedId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Root.Feature.Start"] = new() { Id = "Root.Feature.Start", Name = "Start", Kind = NodeKind.Type },
            ["Root.Feature.Target"] = new() { Id = "Root.Feature.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Root.Feature.Start", ToId = "Root.Feature.Target", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("ture.Start", "Feature.Target");

        Assert.Null(result);
    }
}
