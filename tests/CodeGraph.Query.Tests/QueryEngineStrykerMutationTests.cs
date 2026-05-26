using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class QueryEngineStrykerMutationTests
{
    [Theory]
    [InlineData("src/Orders/OrderService")]
    [InlineData("src\\Orders\\OrderService")]
    public void LooksLikeFilePath_PathSeparatorsWithoutExtension_ReturnTrue(string value)
    {
        Assert.True(QueryEngine.LooksLikeFilePath(value));
    }

    [Fact]
    public void Query_DepthOneTruncation_PreservesSeedAndAllDirectNeighbors()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Feature"] = new() { Id = "MyApp.Feature", Name = "Feature", Kind = NodeKind.Namespace },
            ["MyApp.Feature.Alpha"] = new() { Id = "MyApp.Feature.Alpha", Name = "Alpha", Kind = NodeKind.Method },
            ["MyApp.Feature.Beta"] = new() { Id = "MyApp.Feature.Beta", Name = "Beta", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Feature", ToId = "MyApp.Feature.Alpha", Type = EdgeType.Contains },
            new() { FromId = "MyApp.Feature", ToId = "MyApp.Feature.Beta", Type = EdgeType.Contains }
        };
        var engine = new QueryEngine(nodes, edges, new GraphMetadata());

        var result = engine.Query(new QueryOptions
        {
            Pattern = "MyApp.Feature",
            Depth = 1,
            MaxNodes = 2,
            Rank = true
        });

        Assert.True(result.WasTruncated);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Contains("MyApp.Feature", result.Nodes.Keys);
        Assert.Contains("MyApp.Feature.Alpha", result.Nodes.Keys);
        Assert.Contains("MyApp.Feature.Beta", result.Nodes.Keys);
    }

    [Fact]
    public void Query_ExactMatchWithCloseNeighbors_DoesNotGenerateSuggestions()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["OrderService"] = new() { Id = "OrderService", Name = "OrderService", Kind = NodeKind.Type },
            ["OrderServicf"] = new() { Id = "OrderServicf", Name = "OrderServicf", Kind = NodeKind.Type }
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void FindFuzzyMatches_SortsByDistanceThenName_AndExcludesExactAndDistantMatches()
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

        var matches = engine.FindFuzzyMatches("book", maxDistance: 1, maxResults: 10);

        Assert.Equal(new[] { "books", "boon", "cook" }, matches);
    }

    [Theory]
    [InlineData("a", "b", 1)]
    [InlineData("a", "ab", 1)]
    [InlineData("ab", "a", 1)]
    public void LevenshteinDistance_SingleCharacterBoundaries_ReturnExpectedDistance(string source, string target, int expected)
    {
        Assert.Equal(expected, QueryEngine.LevenshteinDistance(source, target));
    }

    [Fact]
    public void EstimateCost_DepthZero_ExcludesEdgesLeavingReachableSet()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = new() { Id = "App.Target", Name = "Target", Kind = NodeKind.Method },
            ["App.Outside"] = new() { Id = "App.Outside", Name = "Outside", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Target", ToId = "App.Outside", Type = EdgeType.Calls }
        };
        var engine = new QueryEngine(nodes, edges, new GraphMetadata());

        var estimate = engine.EstimateCost("Target", new QueryOptions { Depth = 0, MaxNodes = 50 });

        Assert.Equal(1, estimate.EstimatedNodes);
        Assert.Equal(0, estimate.EstimatedEdges);
        Assert.Equal(50, estimate.EstimatedTokensCompact);
        Assert.Equal(150, estimate.EstimatedTokensContext);
    }

    [Fact]
    public void EstimateCost_UsesExactTokenFormula_WhenReachableEdgesExist()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = new() { Id = "App.Target", Name = "Target", Kind = NodeKind.Method },
            ["App.Dependency"] = new() { Id = "App.Dependency", Name = "Dependency", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Target", ToId = "App.Dependency", Type = EdgeType.Calls }
        };
        var engine = new QueryEngine(nodes, edges, new GraphMetadata());

        var estimate = engine.EstimateCost("Target", new QueryOptions { Depth = 1, MaxNodes = 50 });

        Assert.Equal(2, estimate.EstimatedNodes);
        Assert.Equal(1, estimate.EstimatedEdges);
        Assert.Equal(120, estimate.EstimatedTokensCompact);
        Assert.Equal(360, estimate.EstimatedTokensContext);
    }

    [Fact]
    public void FindByFilePath_SubpathMatchNotAtEnd_ReturnsMatchingNode()
    {
        var node = new GraphNode
        {
            Id = "App.Order.Generated",
            Name = "Generated",
            Kind = NodeKind.Type,
            FilePath = "src/Services/Order/OrderService.Generated.cs"
        };
        var engine = new QueryEngine(new Dictionary<string, GraphNode> { [node.Id] = node }, [], new GraphMetadata());

        var results = engine.FindByFilePath("Services/Order");

        var match = Assert.Single(results);
        Assert.Equal(node.Id, match.Id);
    }

    [Fact]
    public void Query_ExactIdMatch_IsRankedAheadOfSuffixMatches()
    {
        var exact = new GraphNode { Id = "OrderService", Name = "OrderService", Kind = NodeKind.Type };
        var suffix = new GraphNode { Id = "App.OrderService", Name = "OrderService", Kind = NodeKind.Type };
        var partial = new GraphNode { Id = "App.OrderServiceExtra", Name = "OrderServiceExtra", Kind = NodeKind.Type };
        var engine = new QueryEngine(
            new Dictionary<string, GraphNode>
            {
                [exact.Id] = exact,
                [suffix.Id] = suffix,
                [partial.Id] = partial
            },
            [],
            new GraphMetadata());

        var result = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 0 });

        Assert.Collection(
            result.MatchedNodes,
            first => Assert.Equal("OrderService", first.Id),
            second => Assert.Equal("App.OrderService", second.Id));
        Assert.DoesNotContain(result.MatchedNodes, node => node.Id == "App.OrderServiceExtra");
    }
}
