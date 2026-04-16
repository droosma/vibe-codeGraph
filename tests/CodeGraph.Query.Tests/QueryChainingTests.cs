using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class QueryChainingTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Meta) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.OrderService"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService", Name = "OrderService",
                Kind = NodeKind.Type, FilePath = "src/Services/OrderService.cs",
                StartLine = 10, EndLine = 100,
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Services.OrderService.PlaceOrder"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
                Kind = NodeKind.Method, FilePath = "src/Services/OrderService.cs",
                StartLine = 42, EndLine = 67,
                ContainingTypeId = "MyApp.Services.OrderService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Services.InventoryService.Reserve"] = new GraphNode
            {
                Id = "MyApp.Services.InventoryService.Reserve", Name = "Reserve",
                Kind = NodeKind.Method, FilePath = "src/Services/InventoryService.cs",
                StartLine = 23, EndLine = 45,
                ContainingTypeId = "MyApp.Services.InventoryService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Controllers.OrderController.Post"] = new GraphNode
            {
                Id = "MyApp.Controllers.OrderController.Post", Name = "Post",
                Kind = NodeKind.Method, FilePath = "src/Controllers/OrderController.cs",
                StartLine = 18, EndLine = 30,
                ContainingTypeId = "MyApp.Controllers.OrderController",
                ContainingNamespaceId = "MyApp.Controllers",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Tests.OrderServiceTests.PlaceOrder_Succeeds"] = new GraphNode
            {
                Id = "MyApp.Tests.OrderServiceTests.PlaceOrder_Succeeds", Name = "PlaceOrder_Succeeds",
                Kind = NodeKind.Method, FilePath = "tests/OrderServiceTests.cs",
                StartLine = 12, EndLine = 35,
                ContainingTypeId = "MyApp.Tests.OrderServiceTests",
                ContainingNamespaceId = "MyApp.Tests",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Data.SqlOrderRepository"] = new GraphNode
            {
                Id = "MyApp.Data.SqlOrderRepository", Name = "SqlOrderRepository",
                Kind = NodeKind.Type, FilePath = "src/Data/SqlOrderRepository.cs",
                StartLine = 5, EndLine = 50,
                ContainingNamespaceId = "MyApp.Data",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Services"] = new GraphNode
            {
                Id = "MyApp.Services", Name = "Services",
                Kind = NodeKind.Namespace
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Services.InventoryService.Reserve", Type = EdgeType.Calls },
            new() { FromId = "MyApp.Controllers.OrderController.Post", ToId = "MyApp.Services.OrderService.PlaceOrder", Type = EdgeType.Calls },
            new() { FromId = "MyApp.Tests.OrderServiceTests.PlaceOrder_Succeeds", ToId = "MyApp.Services.OrderService.PlaceOrder", Type = EdgeType.Covers },
            new() { FromId = "MyApp.Services.OrderService", ToId = "MyApp.Services.OrderService.PlaceOrder", Type = EdgeType.Contains },
            new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Data.SqlOrderRepository", Type = EdgeType.ResolvesTo,
                Resolution = "IOrderRepository → SqlOrderRepository (registered: Scoped)" }
        };

        var meta = new GraphMetadata
        {
            CommitHash = "a1b2c3d4e5f6",
            Branch = "main",
            GeneratedAt = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero),
            IndexerVersion = "1.0.0",
            Solution = "MyApp.sln",
            ProjectsIndexed = new[] { "MyApp.Services", "MyApp.Controllers", "MyApp.Data", "MyApp.Tests" }
        };

        return (nodes, edges, meta);
    }

    // --- QueryFromResult tests ---

    [Fact]
    public void QueryFromResult_UsesProvidedSeedsInsteadOfPattern()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void QueryFromResult_ExpandsFromSeeds_Depth1()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 1 });

        // PlaceOrder has connections to: InventoryService.Reserve (calls), OrderController.Post (called by),
        // OrderServiceTests (covers), OrderService (contains), SqlOrderRepository (resolves to)
        Assert.True(result.Nodes.Count > 1);
        Assert.Contains("MyApp.Services.OrderService.PlaceOrder", result.Nodes.Keys);
        Assert.Contains("MyApp.Services.InventoryService.Reserve", result.Nodes.Keys);
    }

    [Fact]
    public void QueryFromResult_WithEdgeTypeFilter_OnlyIncludesMatchingEdges()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions
        {
            Depth = 1,
            EdgeTypeFilter = EdgeType.Calls
        });

        Assert.All(result.Edges, e => Assert.Equal(EdgeType.Calls, e.Type));
        Assert.True(result.Edges.Count >= 1);
    }

    [Fact]
    public void QueryFromResult_WithNamespaceFilter_FiltersSeedNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[]
        {
            "MyApp.Services.OrderService.PlaceOrder",
            "MyApp.Controllers.OrderController.Post"
        };
        var result = engine.QueryFromResult(seedIds, new QueryOptions
        {
            Depth = 0,
            NamespaceFilter = "MyApp.Services*"
        });

        // Only the Services node should pass the namespace filter
        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void QueryFromResult_IgnoresNonexistentSeedIds()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "NonExistent.Node", "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void QueryFromResult_EmptySeeds_ReturnsEmptyResult()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = Array.Empty<string>();
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 1 });

        Assert.Empty(result.MatchedNodes);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
    }

    // --- Chaining tests (query → follow-up from result) ---

    [Fact]
    public void Chaining_InitialQuery_ThenFollowUpFromResult()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // Step 1: Find OrderService.PlaceOrder
        var initial = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        Assert.Single(initial.MatchedNodes);

        // Step 2: Use result as seeds, expand by calls
        var followUp = engine.QueryFromResult(
            initial.Nodes.Keys.ToList(),
            new QueryOptions { Depth = 1, EdgeTypeFilter = EdgeType.Calls });

        Assert.Contains("MyApp.Services.OrderService.PlaceOrder", followUp.Nodes.Keys);
        Assert.All(followUp.Edges, e => Assert.Equal(EdgeType.Calls, e.Type));
    }

    [Fact]
    public void Chaining_TwoSteps_ExpandsThroughGraph()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // Step 1: Find OrderService.PlaceOrder
        var step1 = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        // Step 2: Expand via calls
        var step2 = engine.QueryFromResult(
            step1.Nodes.Keys.ToList(),
            new QueryOptions { Depth = 1, EdgeTypeFilter = EdgeType.Calls });

        // Step 3: From step2 results, expand via resolves-to
        var step3 = engine.QueryFromResult(
            step2.Nodes.Keys.ToList(),
            new QueryOptions { Depth = 1, EdgeTypeFilter = EdgeType.ResolvesTo });

        // Should include SqlOrderRepository through resolves-to from PlaceOrder
        Assert.True(step3.Nodes.Count > 0);
    }

    [Fact]
    public void Chaining_GraphLoadedOnce_EngineReusedAcrossQueries()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // Run multiple queries on the same engine - graph loaded once
        var r1 = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 1 });
        var r2 = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var r3 = engine.QueryFromResult(r1.Nodes.Keys.ToList(), new QueryOptions { Depth = 1 });

        // All should produce valid results from the same loaded graph
        Assert.True(r1.Nodes.Count > 0);
        Assert.True(r2.Nodes.Count > 0);
        Assert.True(r3.Nodes.Count > 0);
    }

    // --- Set operation tests ---

    [Fact]
    public void Union_CombinesNodesAndEdges()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var union = QueryResult.Union(resultA, resultB);

        Assert.Contains("MyApp.Services.OrderService.PlaceOrder", union.Nodes.Keys);
        Assert.Contains("MyApp.Services.InventoryService.Reserve", union.Nodes.Keys);
        Assert.Equal(2, union.MatchedNodes.Count);
    }

    [Fact]
    public void Union_DeduplicatesOverlappingNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var resultB = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });

        var union = QueryResult.Union(resultA, resultB);

        // Same query results unioned should not duplicate
        Assert.Equal(resultA.Nodes.Count, union.Nodes.Count);
        Assert.Equal(resultA.Edges.Count, union.Edges.Count);
    }

    [Fact]
    public void Union_DeduplicatesOverlappingEdges()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var resultB = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });

        var union = QueryResult.Union(resultA, resultB);

        // Edges should be deduplicated by (FromId, ToId, Type)
        var edgeKeys = union.Edges.Select(e => (e.FromId, e.ToId, e.Type)).ToList();
        Assert.Equal(edgeKeys.Count, edgeKeys.Distinct().Count());
    }

    [Fact]
    public void Union_PreservesMetadataFromFirstResult()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var union = QueryResult.Union(resultA, resultB);

        Assert.Same(resultA.Metadata, union.Metadata);
    }

    [Fact]
    public void Intersect_KeepsOnlyCommonNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // PlaceOrder depth 1 expands to multiple nodes
        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        // Depth 0 is just PlaceOrder
        var resultB = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        var intersect = QueryResult.Intersect(resultA, resultB);

        // Intersection should only contain PlaceOrder (the single common node)
        Assert.Single(intersect.Nodes);
        Assert.Contains("MyApp.Services.OrderService.PlaceOrder", intersect.Nodes.Keys);
    }

    [Fact]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var intersect = QueryResult.Intersect(resultA, resultB);

        Assert.Empty(intersect.Nodes);
        Assert.Empty(intersect.Edges);
    }

    [Fact]
    public void Intersect_EdgesOnlyBetweenCommonNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var resultB = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 1 });

        var intersect = QueryResult.Intersect(resultA, resultB);

        // All edges must have both endpoints in the intersection
        var nodeIds = new HashSet<string>(intersect.Nodes.Keys);
        Assert.All(intersect.Edges, e =>
        {
            Assert.Contains(e.FromId, nodeIds);
            Assert.Contains(e.ToId, nodeIds);
        });
    }

    [Fact]
    public void Difference_RemovesNodesFromSecondResult()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var diff = QueryResult.Difference(resultA, resultB);

        Assert.DoesNotContain("MyApp.Services.InventoryService.Reserve", diff.Nodes.Keys);
        Assert.Contains("MyApp.Services.OrderService.PlaceOrder", diff.Nodes.Keys);
    }

    [Fact]
    public void Difference_RemovesEdgesWithRemovedEndpoints()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var diff = QueryResult.Difference(resultA, resultB);

        var remainingIds = new HashSet<string>(diff.Nodes.Keys);
        Assert.All(diff.Edges, e =>
        {
            Assert.Contains(e.FromId, remainingIds);
            Assert.Contains(e.ToId, remainingIds);
        });
    }

    [Fact]
    public void Difference_NoOverlap_ReturnsOriginal()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        var diff = QueryResult.Difference(resultA, resultB);

        Assert.Equal(resultA.Nodes.Count, diff.Nodes.Count);
    }

    // --- Output format compatibility ---

    [Fact]
    public void QueryFromResult_WorksWithJsonFormat()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 1, Format = OutputFormat.Json });

        var json = OutputFormatters.JsonFormatter.Format(result);
        Assert.NotEmpty(json);
        Assert.Contains("PlaceOrder", json);
    }

    [Fact]
    public void QueryFromResult_WorksWithTextFormat()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 1, Format = OutputFormat.Text });

        var text = OutputFormatters.TextFormatter.Format(result);
        Assert.NotEmpty(text);
        Assert.Contains("PlaceOrder", text);
    }

    [Fact]
    public void QueryFromResult_WorksWithContextFormat()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 1, Format = OutputFormat.Context });

        var ctx = OutputFormatters.ContextFormatter.Format(result, "from-result test");
        Assert.NotEmpty(ctx);
        Assert.Contains("PlaceOrder", ctx);
    }

    [Fact]
    public void Union_WorksWithAllOutputFormats()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var resultA = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });
        var resultB = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });
        var union = QueryResult.Union(resultA, resultB);

        var json = OutputFormatters.JsonFormatter.Format(union);
        Assert.Contains("PlaceOrder", json);
        Assert.Contains("Reserve", json);

        var text = OutputFormatters.TextFormatter.Format(union);
        Assert.Contains("PlaceOrder", text);

        var ctx = OutputFormatters.ContextFormatter.Format(union, "union test");
        Assert.Contains("PlaceOrder", ctx);
    }

    // --- QueryFromResult with project filter ---

    [Fact]
    public void QueryFromResult_WithProjectFilter_FiltersSeedNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[]
        {
            "MyApp.Services.OrderService.PlaceOrder",
            "MyApp.Controllers.OrderController.Post"
        };
        var result = engine.QueryFromResult(seedIds, new QueryOptions
        {
            Depth = 0,
            ProjectFilter = "MyApp.Controllers"
        });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Controllers.OrderController.Post", result.TargetNode!.Id);
    }

    // --- QueryFromResult preserves metadata ---

    [Fact]
    public void QueryFromResult_PreservesGraphMetadata()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var seedIds = new[] { "MyApp.Services.OrderService.PlaceOrder" };
        var result = engine.QueryFromResult(seedIds, new QueryOptions { Depth = 0 });

        Assert.NotNull(result.Metadata);
        Assert.Equal("a1b2c3d4e5f6", result.Metadata.CommitHash);
    }
}
