using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Metrics;

namespace CodeGraph.Query.Tests;

public class FuzzyMatchTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Meta) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.OrderService"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService", Name = "OrderService",
                Kind = NodeKind.Type, FilePath = "src/OrderService.cs",
                StartLine = 1, EndLine = 50
            },
            ["MyApp.Services.OrderController"] = new GraphNode
            {
                Id = "MyApp.Services.OrderController", Name = "OrderController",
                Kind = NodeKind.Type, FilePath = "src/OrderController.cs",
                StartLine = 1, EndLine = 30
            },
            ["MyApp.Services"] = new GraphNode
            {
                Id = "MyApp.Services", Name = "Services",
                Kind = NodeKind.Namespace
            },
            ["MyApp.Data.UserRepository"] = new GraphNode
            {
                Id = "MyApp.Data.UserRepository", Name = "UserRepository",
                Kind = NodeKind.Type, FilePath = "src/UserRepository.cs",
                StartLine = 1, EndLine = 40
            },
            ["MyApp.Services.OrderService.Process"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService.Process", Name = "Process",
                Kind = NodeKind.Method, FilePath = "src/OrderService.cs",
                StartLine = 10, EndLine = 20
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Services.OrderService.Process", ToId = "MyApp.Data.UserRepository", Type = EdgeType.Calls }
        };

        var meta = new GraphMetadata
        {
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = "Test.sln",
            ProjectsIndexed = new[] { "MyApp" }
        };

        return (nodes, edges, meta);
    }

    [Fact]
    public void FuzzyMatch_TypoInName_ReturnsSuggestions()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "OrdrService" is a typo for "OrderService" (distance = 1)
        var result = engine.Query(new QueryOptions { Pattern = "OrdrService", Depth = 0 });

        Assert.Empty(result.MatchedNodes);
        Assert.NotEmpty(result.Suggestions);
        Assert.Contains("OrderService", result.Suggestions);
    }

    [Fact]
    public void FuzzyMatch_ExactMatch_NoSuggestions()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 0 });

        Assert.NotEmpty(result.MatchedNodes);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void FuzzyMatch_WildcardPattern_NoSuggestions()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // Wildcards should not trigger fuzzy matching
        var result = engine.Query(new QueryOptions { Pattern = "NonExist*", Depth = 0 });

        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void FuzzyMatch_TooDistant_NoSuggestions()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "ZZZZZZZZZ" is too far from anything
        var result = engine.Query(new QueryOptions { Pattern = "ZZZZZZZZZ", Depth = 0 });

        Assert.Empty(result.MatchedNodes);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void FuzzyMatch_CaseInsensitive()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "orderservce" (missing 'i') should match "OrderService"
        var result = engine.Query(new QueryOptions { Pattern = "orderservce", Depth = 0 });

        Assert.Empty(result.MatchedNodes);
        Assert.NotEmpty(result.Suggestions);
        Assert.Contains("OrderService", result.Suggestions);
    }

    [Fact]
    public void FuzzyMatch_OnlyTypesAndNamespaces()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "Procss" is close to "Process" (method) but fuzzy only matches Types/Namespaces
        var result = engine.Query(new QueryOptions { Pattern = "Procss", Depth = 0 });

        Assert.Empty(result.MatchedNodes);
        // Should not suggest "Process" since it's a Method
        Assert.DoesNotContain("Process", result.Suggestions);
    }

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, QueryEngine.LevenshteinDistance("test", "test"));
    }

    [Fact]
    public void LevenshteinDistance_EmptySource_ReturnsTargetLength()
    {
        Assert.Equal(4, QueryEngine.LevenshteinDistance("", "test"));
    }

    [Fact]
    public void LevenshteinDistance_EmptyTarget_ReturnsSourceLength()
    {
        Assert.Equal(4, QueryEngine.LevenshteinDistance("test", ""));
    }

    [Fact]
    public void LevenshteinDistance_SingleSubstitution_ReturnsOne()
    {
        Assert.Equal(1, QueryEngine.LevenshteinDistance("cat", "bat"));
    }

    [Fact]
    public void LevenshteinDistance_SingleInsertion_ReturnsOne()
    {
        Assert.Equal(1, QueryEngine.LevenshteinDistance("cat", "cats"));
    }

    [Fact]
    public void LevenshteinDistance_SingleDeletion_ReturnsOne()
    {
        Assert.Equal(1, QueryEngine.LevenshteinDistance("cats", "cat"));
    }
}
