using CodeGraph.Core;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class QueryEngineMutationCoverageTests
{
    private static QueryEngine CreateSearchEngine()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Search.Helper"] = new()
            {
                Id = "App.Search.Helper",
                Name = "Helper",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "App.Ordering"
            },
            ["App.Search.OrderService"] = new()
            {
                Id = "App.Search.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "App.Search"
            },
            ["App.Search.OrderCoordinator"] = new()
            {
                Id = "App.Search.OrderCoordinator",
                Name = "OrderCoordinator",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "App.Search"
            },
            ["App.Search.Order"] = new()
            {
                Id = "App.Search.Order",
                Name = "Order",
                Kind = NodeKind.Method,
                ContainingNamespaceId = "App.Search"
            }
        };

        return new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
    }

    private static QueryEngine CreateTraversalEngine()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = new() { Id = "App.Target", Name = "Target", Kind = NodeKind.Method },
            ["App.Dependency"] = new() { Id = "App.Dependency", Name = "Dependency", Kind = NodeKind.Method },
            ["App.Controller"] = new() { Id = "App.Controller", Name = "Controller", Kind = NodeKind.Method },
            ["App.Api"] = new() { Id = "App.Api", Name = "Api", Kind = NodeKind.Method }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Target", ToId = "App.Dependency", Type = EdgeType.Calls },
            new() { FromId = "App.Controller", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "App.Api", ToId = "App.Controller", Type = EdgeType.Calls }
        };

        return new QueryEngine(nodes, edges, new GraphMetadata());
    }

    [Fact]
    public void Search_SortsNameMatchesTypesAndShorterNamesFirst()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 4);

        Assert.Collection(results,
            node => Assert.Equal("App.Search.OrderService", node.Id),
            node => Assert.Equal("App.Search.OrderCoordinator", node.Id),
            node => Assert.Equal("App.Search.Order", node.Id),
            node => Assert.Equal("App.Search.Helper", node.Id));
    }

    [Fact]
    public void Search_MaxResults_ReturnsOnlyHighestRankedNodes()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 2);

        Assert.Collection(results,
            node => Assert.Equal("App.Search.OrderService", node.Id),
            node => Assert.Equal("App.Search.OrderCoordinator", node.Id));
    }

    [Fact]
    public void Search_KindFilter_ReturnsOnlyMatchingNodes()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 5, kindFilter: NodeKind.Method);

        var match = Assert.Single(results);
        Assert.Equal("App.Search.Order", match.Id);
    }

    [Fact]
    public void Query_PatternMatching_PrefersExactSuffixMatchesOverPartialMatches()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.PlaceOrder"] = new() { Id = "App.PlaceOrder", Name = "PlaceOrder", Kind = NodeKind.Method },
            ["App.PlaceOrderAsync"] = new() { Id = "App.PlaceOrderAsync", Name = "PlaceOrderAsync", Kind = NodeKind.Method }
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        var match = Assert.Single(result.MatchedNodes);
        Assert.Equal("App.PlaceOrder", match.Id);
        Assert.Equal("App.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void Query_DepthTraversal_DepthOneReturnsOnlyDirectEdges()
    {
        var engine = CreateTraversalEngine();

        var result = engine.Query(new QueryOptions { Pattern = "Target", Depth = 1, MaxNodes = 10, Rank = false });

        Assert.Equal(2, result.Edges.Count);
        Assert.Contains(result.Edges, edge => edge.FromId == "App.Target" && edge.ToId == "App.Dependency");
        Assert.Contains(result.Edges, edge => edge.FromId == "App.Controller" && edge.ToId == "App.Target");
        Assert.DoesNotContain(result.Edges, edge => edge.FromId == "App.Api" && edge.ToId == "App.Controller");
    }

    [Fact]
    public void Query_DepthTraversal_DepthTwoAddsSecondLevelEdges()
    {
        var engine = CreateTraversalEngine();

        var result = engine.Query(new QueryOptions { Pattern = "Target", Depth = 2, MaxNodes = 10, Rank = false });

        Assert.Equal(3, result.Edges.Count);
        Assert.Contains(result.Edges, edge => edge.FromId == "App.Api" && edge.ToId == "App.Controller");
    }
}
