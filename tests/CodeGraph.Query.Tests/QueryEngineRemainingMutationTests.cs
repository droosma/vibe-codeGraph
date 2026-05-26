using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class QueryEngineRemainingMutationTests
{
    private static QueryEngine CreateSearchEngine()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Order"] = new() { Id = "App.Order", Name = "Order", Kind = NodeKind.Type, ContainingNamespaceId = "App" },
            ["App.OrderService"] = new() { Id = "App.OrderService", Name = "OrderService", Kind = NodeKind.Type, ContainingNamespaceId = "App" },
            ["App.ProcessOrder"] = new() { Id = "App.ProcessOrder", Name = "ProcessOrder", Kind = NodeKind.Method, ContainingNamespaceId = "App" },
            ["App.Search.Helper"] = new() { Id = "App.Search.Helper", Name = "Helper", Kind = NodeKind.Type, ContainingNamespaceId = "Order.Tools" },
            ["App.FileWorker"] = new() { Id = "App.FileWorker", Name = "Worker", Kind = NodeKind.Method, FilePath = "src/Order/Worker.cs", ContainingNamespaceId = "App" },
            ["Token.OrderId"] = new() { Id = "Token.OrderId", Name = "Utility", Kind = NodeKind.Type, FilePath = "src/Infra/Utility.cs", ContainingNamespaceId = "Infra" }
        };

        return new QueryEngine(nodes, [], new GraphMetadata());
    }

    [Fact]
    public void Search_RanksNameMatchesBeforeNamespaceAndFilePathMatches()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 5);

        Assert.Collection(results,
            node => Assert.Equal("App.Order", node.Id),
            node => Assert.Equal("App.OrderService", node.Id),
            node => Assert.Equal("App.ProcessOrder", node.Id),
            node => Assert.Equal("App.Search.Helper", node.Id),
            node => Assert.Equal("Token.OrderId", node.Id));
    }

    [Fact]
    public void Search_MaxResultsZero_ReturnsEmpty()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 0);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_MaxResultsOne_ReturnsHighestRankedNode()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("Order", maxResults: 1);

        var match = Assert.Single(results);
        Assert.Equal("App.Order", match.Id);
    }

    [Fact]
    public void Search_IdMatches_WhenOtherFieldsDoNotMatch()
    {
        var engine = CreateSearchEngine();

        var results = engine.Search("OrderId", maxResults: 5);

        var match = Assert.Single(results);
        Assert.Equal("Token.OrderId", match.Id);
    }

    [Fact]
    public void Query_WildcardPattern_UsesAnchoredRegex()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.OrderService"] = new() { Id = "App.OrderService", Name = "OrderService", Kind = NodeKind.Type },
            ["App.PreOrderService"] = new() { Id = "App.PreOrderService", Name = "PreOrderService", Kind = NodeKind.Type }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "Order*", Depth = 0 });

        var match = Assert.Single(result.MatchedNodes);
        Assert.Equal("App.OrderService", match.Id);
    }

    [Fact]
    public void Query_KindPrefix_WithWildcard_ReturnsOnlyRequestedKind()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.OrderService"] = new() { Id = "App.OrderService", Name = "OrderService", Kind = NodeKind.Type },
            ["App.OrderService.Run"] = new() { Id = "App.OrderService.Run", Name = "OrderRun", Kind = NodeKind.Method }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "method:Order*", Depth = 0 });

        var match = Assert.Single(result.MatchedNodes);
        Assert.Equal(NodeKind.Method, match.Kind);
        Assert.Equal("App.OrderService.Run", match.Id);
    }

    [Fact]
    public void FindFuzzyMatches_DifferentDistances_AreOrderedNearestFirst()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["book"] = new() { Id = "book", Name = "book", Kind = NodeKind.Type },
            ["books"] = new() { Id = "books", Name = "books", Kind = NodeKind.Type },
            ["boon"] = new() { Id = "boon", Name = "boon", Kind = NodeKind.Type },
            ["cook"] = new() { Id = "cook", Name = "cook", Kind = NodeKind.Type },
            ["back"] = new() { Id = "back", Name = "back", Kind = NodeKind.Type }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var matches = engine.FindFuzzyMatches("book", maxDistance: 2, maxResults: 10);

        Assert.Equal(new[] { "books", "boon", "cook", "back" }, matches);
    }

    [Fact]
    public void LevenshteinDistance_MultipleDeletions_UsesFirstColumnInitialization()
    {
        Assert.Equal(2, QueryEngine.LevenshteinDistance("aa", "b"));
    }

    [Fact]
    public void LevenshteinDistance_MultipleInsertions_UsesFirstRowInitialization()
    {
        Assert.Equal(2, QueryEngine.LevenshteinDistance("a", "bb"));
    }

    [Fact]
    public void Query_WildcardPattern_RequiresEndAnchor()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.PreOrder"] = new() { Id = "App.PreOrder", Name = "PreOrder", Kind = NodeKind.Type },
            ["App.PreOrderService"] = new() { Id = "App.PreOrderService", Name = "PreOrderService", Kind = NodeKind.Type }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "*Order", Depth = 0 });

        var match = Assert.Single(result.MatchedNodes);
        Assert.Equal("App.PreOrder", match.Id);
    }

    [Fact]
    public void Query_ExactSuffixMatch_ExcludesNonSegmentIdSuffixMatches()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Ns.Exact"] = new() { Id = "Ns.Exact", Name = "Mismatch", Kind = NodeKind.Type },
            ["FooExact"] = new() { Id = "FooExact", Name = "AlsoMismatch", Kind = NodeKind.Type }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "Exact", Depth = 0 });

        var match = Assert.Single(result.MatchedNodes);
        Assert.Equal("Ns.Exact", match.Id);
    }

    [Fact]
    public void Query_RankFalse_TruncationUsesInsertionOrderInsteadOfRanking()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Seed"] = new() { Id = "Seed", Name = "Seed", Kind = NodeKind.Method },
            ["LowPriority.Namespace"] = new() { Id = "LowPriority.Namespace", Name = "Namespace", Kind = NodeKind.Namespace },
            ["HighPriority.Method"] = new() { Id = "HighPriority.Method", Name = "Method", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Seed", ToId = "LowPriority.Namespace", Type = EdgeType.Calls },
            new() { FromId = "Seed", ToId = "HighPriority.Method", Type = EdgeType.Calls }
        };
        var engine = new QueryEngine(nodes, edges, new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "Seed", Depth = 1, MaxNodes = 2, Rank = false });

        Assert.True(result.WasTruncated);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains("Seed", result.Nodes.Keys);
        Assert.Contains("LowPriority.Namespace", result.Nodes.Keys);
        Assert.DoesNotContain("HighPriority.Method", result.Nodes.Keys);
    }
}
