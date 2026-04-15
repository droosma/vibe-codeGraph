using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class DepthFilterTests
{
    private static (Dictionary<string, List<GraphEdge>> Outgoing, Dictionary<string, List<GraphEdge>> Incoming) BuildAdjacency(
        List<GraphEdge> edges)
    {
        var outgoing = new Dictionary<string, List<GraphEdge>>();
        var incoming = new Dictionary<string, List<GraphEdge>>();

        foreach (var edge in edges)
        {
            if (!outgoing.TryGetValue(edge.FromId, out var outList))
            {
                outList = new List<GraphEdge>();
                outgoing[edge.FromId] = outList;
            }
            outList.Add(edge);

            if (!incoming.TryGetValue(edge.ToId, out var inList))
            {
                inList = new List<GraphEdge>();
                incoming[edge.ToId] = inList;
            }
            inList.Add(edge);
        }

        return (outgoing, incoming);
    }

    [Fact]
    public void Depth0_ReturnsOnlySeedNodes()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 0, false);

        Assert.Single(result);
        Assert.Contains("A", result);
    }

    [Fact]
    public void Depth1_IncludesDirectNeighbors()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 1, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
        Assert.DoesNotContain("C", result);
    }

    [Fact]
    public void Depth2_IncludesTransitiveNeighbors()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 2, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
        Assert.Contains("C", result);
    }

    [Fact]
    public void Traverse_FollowsIncomingEdges()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B", ToId = "A", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 1, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
    }

    [Fact]
    public void Traverse_ExcludesExternalEdges_WhenNotIncluded()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "Ext", Type = EdgeType.Calls, IsExternal = true },
            new() { FromId = "A", ToId = "Int", Type = EdgeType.Calls, IsExternal = false }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 1, includeExternal: false);

        Assert.Contains("Int", result);
        Assert.DoesNotContain("Ext", result);
    }

    [Fact]
    public void Traverse_IncludesExternalEdges_WhenRequested()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "Ext", Type = EdgeType.Calls, IsExternal = true }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 1, includeExternal: true);

        Assert.Contains("Ext", result);
    }

    [Fact]
    public void Traverse_MultipleSeeds()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "D", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A", "B" }, outgoing, incoming, 1, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
        Assert.Contains("C", result);
        Assert.Contains("D", result);
    }

    [Fact]
    public void Traverse_CyclicGraph_DoesNotLoop()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "A", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 10, false);

        Assert.Equal(2, result.Count);
        Assert.Contains("A", result);
        Assert.Contains("B", result);
    }

    [Fact]
    public void Traverse_NoEdges_ReturnsOnlySeeds()
    {
        var outgoing = new Dictionary<string, List<GraphEdge>>();
        var incoming = new Dictionary<string, List<GraphEdge>>();

        var result = DepthFilter.Traverse(new[] { "Isolated" }, outgoing, incoming, 5, false);

        Assert.Single(result);
        Assert.Contains("Isolated", result);
    }

    [Fact]
    public void Traverse_ExternalIncoming_ExcludedWhenNotRequested()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Ext", ToId = "A", Type = EdgeType.Calls, IsExternal = true }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 1, includeExternal: false);

        Assert.Single(result);
        Assert.Contains("A", result);
    }

    [Fact]
    public void Traverse_DepthBoundary_ExactDepth()
    {
        // A -> B -> C -> D (chain of 3 hops)
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls },
            new() { FromId = "C", ToId = "D", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        // Depth 2 should reach A, B, C but NOT D
        var result = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 2, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
        Assert.Contains("C", result);
        Assert.DoesNotContain("D", result);

        // Depth 3 should include D
        var result3 = DepthFilter.Traverse(new[] { "A" }, outgoing, incoming, 3, false);
        Assert.Contains("D", result3);
    }

    [Fact]
    public void Traverse_DuplicateSeedIds_HandledGracefully()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var (outgoing, incoming) = BuildAdjacency(edges);

        var result = DepthFilter.Traverse(new[] { "A", "A" }, outgoing, incoming, 1, false);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
    }
}
