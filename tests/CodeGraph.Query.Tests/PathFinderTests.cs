using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class PathFinderTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildLinearGraph()
    {
        // A -> B -> C -> D
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Type },
            ["C"] = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Type },
            ["D"] = new GraphNode { Id = "D", Name = "D", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new GraphEdge { FromId = "B", ToId = "C", Type = EdgeType.Calls },
            new GraphEdge { FromId = "C", ToId = "D", Type = EdgeType.Calls }
        };
        return (nodes, edges);
    }

    [Fact]
    public void FindPath_DirectConnection_ReturnsSingleStep()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "B");

        Assert.NotNull(result);
        Assert.Equal("A", result.FromId);
        Assert.Equal("B", result.ToId);
        Assert.Single(result.Steps);
        Assert.Equal(EdgeType.Calls, result.Steps[0].Edge.Type);
    }

    [Fact]
    public void FindPath_MultiHop_ReturnsShortestPath()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "D");

        Assert.NotNull(result);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("A", result.Steps[0].FromId);
        Assert.Equal("B", result.Steps[0].ToId);
        Assert.Equal("B", result.Steps[1].FromId);
        Assert.Equal("C", result.Steps[1].ToId);
        Assert.Equal("C", result.Steps[2].FromId);
        Assert.Equal("D", result.Steps[2].ToId);
    }

    [Fact]
    public void FindPath_NoPath_ReturnsNull()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        // D has no outgoing edges to A
        var result = finder.FindPath("D", "A");

        Assert.Null(result);
    }

    [Fact]
    public void FindPath_UnknownNode_ReturnsNull()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("X", "A");

        Assert.Null(result);
    }

    [Fact]
    public void FindPath_MaxDepthExceeded_ReturnsNull()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        // Path A->D is 3 hops, but we limit to 2
        var result = finder.FindPath("A", "D", maxDepth: 2);

        Assert.Null(result);
    }

    [Fact]
    public void FindPath_CycleDoesNotLoop()
    {
        // A -> B -> C -> A (cycle), also C -> D
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Type },
            ["C"] = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Type },
            ["D"] = new GraphNode { Id = "D", Name = "D", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new GraphEdge { FromId = "B", ToId = "C", Type = EdgeType.Calls },
            new GraphEdge { FromId = "C", ToId = "A", Type = EdgeType.Calls },
            new GraphEdge { FromId = "C", ToId = "D", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "D");

        Assert.NotNull(result);
        Assert.Equal(3, result.Steps.Count);
    }

    [Fact]
    public void FindPath_MatchesByName()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["NS.ClassA"] = new GraphNode { Id = "NS.ClassA", Name = "ClassA", Kind = NodeKind.Type },
            ["NS.ClassB"] = new GraphNode { Id = "NS.ClassB", Name = "ClassB", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "NS.ClassA", ToId = "NS.ClassB", Type = EdgeType.Calls }
        };
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("ClassA", "ClassB");

        Assert.NotNull(result);
        Assert.Single(result.Steps);
    }

    [Fact]
    public void FindPath_FromNode_PopulatedInResult()
    {
        var (nodes, edges) = BuildLinearGraph();
        var finder = new PathFinder(nodes, edges);

        var result = finder.FindPath("A", "B");

        Assert.NotNull(result);
        Assert.NotNull(result.FromNode);
        Assert.Equal("A", result.FromNode!.Id);
    }
}
