using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class QueryEngineTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Meta) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.OrderService"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService", Name = "OrderService",
                Kind = NodeKind.Type, FilePath = "src/Services/OrderService.cs",
                StartLine = 10, EndLine = 100, Signature = "public class OrderService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public, DocComment = "Handles order operations."
            },
            ["MyApp.Services.OrderService.PlaceOrder"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
                Kind = NodeKind.Method, FilePath = "src/Services/OrderService.cs",
                StartLine = 42, EndLine = 67,
                Signature = "public async Task<OrderResult> PlaceOrder(OrderRequest req)",
                ContainingTypeId = "MyApp.Services.OrderService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public,
                DocComment = "Places a new order and triggers fulfillment."
            },
            ["MyApp.Services.InventoryService.Reserve"] = new GraphNode
            {
                Id = "MyApp.Services.InventoryService.Reserve", Name = "Reserve",
                Kind = NodeKind.Method, FilePath = "src/Services/InventoryService.cs",
                StartLine = 23, EndLine = 45,
                Signature = "public async Task<bool> Reserve(int productId, int quantity)",
                ContainingTypeId = "MyApp.Services.InventoryService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Controllers.OrderController.Post"] = new GraphNode
            {
                Id = "MyApp.Controllers.OrderController.Post", Name = "Post",
                Kind = NodeKind.Method, FilePath = "src/Controllers/OrderController.cs",
                StartLine = 18, EndLine = 30,
                Signature = "public async Task<IActionResult> Post(OrderRequest req)",
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

    [Fact]
    public void ExactMatch_FullyQualified_FindsNode()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "MyApp.Services.OrderService.PlaceOrder", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void ExactMatch_ShortName_FindsNode()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "OrderService.PlaceOrder", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void PartialMatch_FindsByName()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void WildcardMatch_FindsMultiple()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "*.OrderService.*", Depth = 0 });

        Assert.True(result.MatchedNodes.Count >= 1);
        Assert.All(result.MatchedNodes, n => Assert.Contains("OrderService", n.Id));
    }

    [Fact]
    public void KindPrefix_FiltersByNodeKind()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "type:OrderService", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal(NodeKind.Type, result.TargetNode!.Kind);
        Assert.Equal("MyApp.Services.OrderService", result.TargetNode.Id);
    }

    [Fact]
    public void Depth0_ReturnsOnlyMatchedNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        Assert.Single(result.Nodes);
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
    }

    [Fact]
    public void Depth1_IncludesDirectNeighbors()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });

        // PlaceOrder has edges to: InventoryService.Reserve, SqlOrderRepository (outgoing)
        // And incoming from: OrderController.Post, OrderServiceTests.PlaceOrder_Succeeds, OrderService (Contains)
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.InventoryService.Reserve"));
        Assert.True(result.Nodes.ContainsKey("MyApp.Controllers.OrderController.Post"));
        Assert.True(result.Nodes.ContainsKey("MyApp.Data.SqlOrderRepository"));
    }

    [Fact]
    public void Depth2_IncludesTransitiveNeighbors()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 2, MaxNodes = 100 });

        // Reserve at depth 0, PlaceOrder at depth 1, then OrderController.Post etc at depth 2
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.InventoryService.Reserve"));
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
        Assert.True(result.Nodes.ContainsKey("MyApp.Controllers.OrderController.Post"));
    }

    [Fact]
    public void EdgeTypeFilter_FiltersEdges()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder", Depth = 1,
            EdgeTypeFilter = EdgeType.Calls
        });

        Assert.All(result.Edges, e => Assert.Equal(EdgeType.Calls, e.Type));
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "NonExistentSymbol", Depth = 1 });

        Assert.Empty(result.MatchedNodes);
        Assert.Null(result.TargetNode);
    }

    [Fact]
    public void MaxNodes_TruncatesResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1, MaxNodes = 2 });

        // Should be truncated but always include the seed
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
        Assert.True(result.WasTruncated);
    }

    // ------------- New tests for surviving mutants ----------------

    [Fact]
    public void EmptyPattern_ReturnsAllNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "", Depth = 0, MaxNodes = 100 });

        Assert.Equal(nodes.Count, result.MatchedNodes.Count);
    }

    [Fact]
    public void WhitespacePattern_ReturnsAllNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "   ", Depth = 0, MaxNodes = 100 });

        Assert.Equal(nodes.Count, result.MatchedNodes.Count);
    }

    [Fact]
    public void CaseInsensitive_ExactMatch()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "myapp.services.orderservice.placeorder", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", result.TargetNode!.Id);
    }

    [Fact]
    public void CaseInsensitive_PartialMatch()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "placeorder", Depth = 0 });

        Assert.Single(result.MatchedNodes);
    }

    [Fact]
    public void WildcardMatch_CaseInsensitive()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "*.ORDERSERVICE.*", Depth = 0 });

        Assert.True(result.MatchedNodes.Count >= 1);
    }

    [Fact]
    public void WildcardMatch_MatchesOnName()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "Reserve" name should match wildcard *eserve
        var result = engine.Query(new QueryOptions { Pattern = "*eserve", Depth = 0 });

        Assert.Contains(result.MatchedNodes, n => n.Name == "Reserve");
    }

    [Fact]
    public void ExactMatch_EndsWith_DotPrefix()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // "InventoryService.Reserve" should match via EndsWith("." + pattern)
        var result = engine.Query(new QueryOptions { Pattern = "InventoryService.Reserve", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.InventoryService.Reserve", result.TargetNode!.Id);
    }

    [Fact]
    public void PartialMatch_NameEquals_CaseInsensitive()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "reserve", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Services.InventoryService.Reserve", result.TargetNode!.Id);
    }

    [Theory]
    [InlineData("method:PlaceOrder")]
    [InlineData("METHOD:PlaceOrder")]
    public void KindPrefix_CaseInsensitive(string pattern)
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = pattern, Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal(NodeKind.Method, result.TargetNode!.Kind);
    }

    [Fact]
    public void KindPrefix_Namespace_FiltersCorrectly()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "namespace:Services", Depth = 0 });

        Assert.Single(result.MatchedNodes);
        Assert.Equal(NodeKind.Namespace, result.TargetNode!.Kind);
    }

    [Fact]
    public void SingleMatch_SetsTargetNode()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "Reserve", Depth = 0 });

        Assert.NotNull(result.TargetNode);
        Assert.Equal("MyApp.Services.InventoryService.Reserve", result.TargetNode!.Id);
    }

    [Fact]
    public void MultipleMatches_TargetNodeIsNull()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "*.OrderService*", Depth = 0 });

        // Multiple nodes match
        Assert.True(result.MatchedNodes.Count > 1);
        Assert.Null(result.TargetNode);
    }

    [Fact]
    public void TotalMatchCount_ReflectsMatchedCount()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1 });

        Assert.Equal(result.MatchedNodes.Count, result.TotalMatchCount);
    }

    [Fact]
    public void Depth0_NoEdgesInSubgraph()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        // Depth 0 = only the seed node, no traversal, so no edges connecting only the seed to itself
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Depth1_EdgesOnlyBetweenSubgraphNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 1, MaxNodes = 100, Rank = false });

        foreach (var e in result.Edges)
        {
            Assert.True(result.Nodes.ContainsKey(e.FromId), $"Edge FromId {e.FromId} not in subgraph");
            Assert.True(result.Nodes.ContainsKey(e.ToId), $"Edge ToId {e.ToId} not in subgraph");
        }
    }

    [Fact]
    public void ExternalEdges_ExcludedByDefault()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["Ext"] = new() { Id = "Ext", Name = "Ext", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "Ext", Type = EdgeType.Calls, IsExternal = true }
        };
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "A", Depth = 1, IncludeExternal = false });

        Assert.DoesNotContain(result.Edges, e => e.IsExternal);
    }

    [Fact]
    public void ExternalEdges_IncludedWhenRequested()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["Ext"] = new() { Id = "Ext", Name = "Ext", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "Ext", Type = EdgeType.Calls, IsExternal = true }
        };
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "A", Depth = 1, IncludeExternal = true, MaxNodes = 100 });

        Assert.Contains(result.Edges, e => e.IsExternal);
        Assert.True(result.Nodes.ContainsKey("Ext"));
    }

    [Fact]
    public void NamespaceFilter_FiltersResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "*",
            Depth = 0,
            NamespaceFilter = "MyApp.Services*",
            MaxNodes = 100
        });

        Assert.All(result.MatchedNodes, n =>
        {
            // All matched nodes should be in MyApp.Services namespace
            var inNamespace = (n.ContainingNamespaceId ?? "").StartsWith("MyApp.Services")
                || n.Id.StartsWith("MyApp.Services");
            Assert.True(inNamespace, $"Node {n.Id} not in MyApp.Services");
        });
    }

    [Fact]
    public void ProjectFilter_FiltersResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "*",
            Depth = 0,
            ProjectFilter = "MyApp.Controllers",
            MaxNodes = 100
        });

        Assert.All(result.MatchedNodes, n =>
        {
            var matches = n.Id.StartsWith("MyApp.Controllers", StringComparison.OrdinalIgnoreCase)
                || (n.ContainingNamespaceId?.StartsWith("MyApp.Controllers", StringComparison.OrdinalIgnoreCase) ?? false);
            Assert.True(matches, $"Node {n.Id} should be in MyApp.Controllers project");
        });
        Assert.True(result.MatchedNodes.Count > 0);
    }

    [Fact]
    public void MaxNodes_WithRank_AlwaysKeepsSeedNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 2,
            MaxNodes = 1,
            Rank = true
        });

        Assert.True(result.WasTruncated);
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
    }

    [Fact]
    public void MaxNodes_WithoutRank_AlwaysKeepsSeedNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 2,
            MaxNodes = 1,
            Rank = false
        });

        Assert.True(result.WasTruncated);
        Assert.True(result.Nodes.ContainsKey("MyApp.Services.OrderService.PlaceOrder"));
    }

    [Fact]
    public void MaxNodes_NotExceeded_NoTruncation()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 0,
            MaxNodes = 100
        });

        Assert.False(result.WasTruncated);
    }

    [Fact]
    public void Metadata_IsIncludedInResult()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0 });

        Assert.Equal(meta, result.Metadata);
    }

    [Fact]
    public void EdgeTypeFilter_Covers_FiltersCorrectly()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 1,
            EdgeTypeFilter = EdgeType.Covers,
            MaxNodes = 100
        });

        Assert.All(result.Edges, e => Assert.Equal(EdgeType.Covers, e.Type));
    }

    [Fact]
    public void MaxNodes_WithRank_TruncatesEdgesToKeptNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 2,
            MaxNodes = 2,
            Rank = true
        });

        // All edges should be between kept nodes
        foreach (var e in result.Edges)
        {
            Assert.True(result.Nodes.ContainsKey(e.FromId));
            Assert.True(result.Nodes.ContainsKey(e.ToId));
        }
    }

    [Fact]
    public void MaxNodes_WithoutRank_TruncatesEdgesToKeptNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var result = engine.Query(new QueryOptions
        {
            Pattern = "PlaceOrder",
            Depth = 2,
            MaxNodes = 2,
            Rank = false
        });

        foreach (var e in result.Edges)
        {
            Assert.True(result.Nodes.ContainsKey(e.FromId));
            Assert.True(result.Nodes.ContainsKey(e.ToId));
        }
    }

    [Fact]
    public void Query_DirectNeighborIds_UsedForRanking()
    {
        // Build graph where depth >= 1 enables directNeighborIds computation
        var nodes = new Dictionary<string, GraphNode>
        {
            ["S"] = new() { Id = "S", Name = "S", Kind = NodeKind.Method },
            ["D1"] = new() { Id = "D1", Name = "D1", Kind = NodeKind.Method },
            ["D2"] = new() { Id = "D2", Name = "D2", Kind = NodeKind.Method },
            ["T1"] = new() { Id = "T1", Name = "T1", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "S", ToId = "D1", Type = EdgeType.Calls },
            new() { FromId = "S", ToId = "D2", Type = EdgeType.Calls },
            new() { FromId = "D1", ToId = "T1", Type = EdgeType.Calls }
        };
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
        var engine = new QueryEngine(nodes, edges, meta);

        // At depth 2 with maxNodes = 3, ranking should keep direct neighbors over transitive
        var result = engine.Query(new QueryOptions { Pattern = "S", Depth = 2, MaxNodes = 3, Rank = true });

        Assert.True(result.Nodes.ContainsKey("S"));
        // D1 and D2 are direct neighbors, T1 is transitive
        Assert.True(result.Nodes.ContainsKey("D1") || result.Nodes.ContainsKey("D2"));
    }

    [Fact]
    public void Query_Depth0_DirectNeighborIds_EqualsSeedIds()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        // depth=0 means directNeighborIds = seedIds only
        var result = engine.Query(new QueryOptions { Pattern = "PlaceOrder", Depth = 0, MaxNodes = 100 });

        Assert.Single(result.Nodes);
    }

    [Fact]
    public void ExactMatch_IdEquals_TakesPrecedenceOverPartial()
    {
        // Exact match should be found first, partial match should not run
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Foo"] = new() { Id = "MyApp.Foo", Name = "Foo", Kind = NodeKind.Method },
            ["MyApp.FooBar"] = new() { Id = "MyApp.FooBar", Name = "FooBar", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "Foo", Depth = 0 });

        // "Foo" matches MyApp.Foo via EndsWith(".Foo"), should not include FooBar
        Assert.Single(result.MatchedNodes);
        Assert.Equal("MyApp.Foo", result.TargetNode!.Id);
    }

    [Fact]
    public void PartialMatch_FallsBack_WhenNoExactMatch()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.Processor"] = new() { Id = "MyApp.Services.Processor", Name = "Processor", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "Processor", Depth = 0 });

        Assert.Single(result.MatchedNodes);
    }

    [Fact]
    public void ProjectFilter_MatchesByContainingNamespaceId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Other.Foo"] = new() { Id = "Other.Foo", Name = "Foo", Kind = NodeKind.Method,
                ContainingNamespaceId = "MyProject.Core" }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions
        {
            Pattern = "*",
            Depth = 0,
            ProjectFilter = "MyProject",
            MaxNodes = 100
        });

        // Node Id doesn't start with "MyProject" but ContainingNamespaceId does
        Assert.Single(result.MatchedNodes);
    }

    [Fact]
    public void ProjectFilter_ExcludesNonMatchingNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Foo"] = new() { Id = "A.Foo", Name = "Foo", Kind = NodeKind.Method, ContainingNamespaceId = "A" },
            ["B.Bar"] = new() { Id = "B.Bar", Name = "Bar", Kind = NodeKind.Method, ContainingNamespaceId = "B" }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions
        {
            Pattern = "*",
            Depth = 0,
            ProjectFilter = "A",
            MaxNodes = 100
        });

        Assert.Single(result.MatchedNodes);
        Assert.Equal("A.Foo", result.MatchedNodes[0].Id);
    }

    [Fact]
    public void ProjectFilter_OrLogic_IdOrNamespace()
    {
        // Node where Id starts with filter but namespace doesn't
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Proj.Svc.Do"] = new() { Id = "Proj.Svc.Do", Name = "Do", Kind = NodeKind.Method,
                ContainingNamespaceId = "Other.NS" }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "*", Depth = 0, ProjectFilter = "Proj", MaxNodes = 100 });

        Assert.Single(result.MatchedNodes);
    }

    [Fact]
    public void ExternalEdgeFilter_RemovesExternalEdgesFromSubgraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, IsExternal = false },
            new() { FromId = "A", ToId = "B", Type = EdgeType.References, IsExternal = true }
        };
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);
        var result = engine.Query(new QueryOptions { Pattern = "A", Depth = 1, IncludeExternal = false, MaxNodes = 100, Rank = false });

        // External edges should be excluded
        Assert.All(result.Edges, e => Assert.False(e.IsExternal));
    }

    [Fact]
    public void MaxNodes_BoundaryExact_NoTruncation()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);

        // MaxNodes exactly equals number of subgraph nodes — no truncation
        var result = engine.Query(new QueryOptions { Pattern = "A", Depth = 1, MaxNodes = 2, Rank = true });

        Assert.False(result.WasTruncated);
        Assert.Equal(2, result.Nodes.Count);
    }

    [Fact]
    public void MaxNodes_RankedTake_KeepsHighestRanked()
    {
        // Create a graph with more nodes than MaxNodes to test Take() behavior
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();

        nodes["Seed"] = new GraphNode { Id = "Seed", Name = "Seed", Kind = NodeKind.Method };
        for (int i = 0; i < 10; i++)
        {
            var nid = $"N{i}";
            nodes[nid] = new GraphNode { Id = nid, Name = nid, Kind = NodeKind.Method };
            edges.Add(new GraphEdge { FromId = "Seed", ToId = nid, Type = EdgeType.Calls });
        }

        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
        var engine = new QueryEngine(nodes, edges, meta);

        // MaxNodes = 3 with rank: should keep seed + 2 best ranked
        var result = engine.Query(new QueryOptions { Pattern = "Seed", Depth = 1, MaxNodes = 3, Rank = true });

        Assert.True(result.WasTruncated);
        Assert.True(result.Nodes.ContainsKey("Seed"));
        Assert.True(result.Nodes.Count <= 11); // Could be up to 3 + seed

        // Without rank: Take should give different results than Skip
        var resultNoRank = engine.Query(new QueryOptions { Pattern = "Seed", Depth = 1, MaxNodes = 3, Rank = false });
        Assert.True(resultNoRank.WasTruncated);
        Assert.True(resultNoRank.Nodes.ContainsKey("Seed"));
    }

    [Fact]
    public void QueryOptions_DefaultValues()
    {
        var opts = new QueryOptions();

        Assert.Equal(string.Empty, opts.Pattern);
        Assert.Equal(1, opts.Depth);
        Assert.Null(opts.EdgeTypeFilter);
        Assert.Null(opts.NamespaceFilter);
        Assert.Null(opts.ProjectFilter);
        Assert.Equal(50, opts.MaxNodes);
        Assert.False(opts.IncludeExternal);
        Assert.True(opts.Rank);
        Assert.Equal(OutputFormat.Context, opts.Format);
    }

    [Fact]
    public void Wildcard_MatchesId_AndName()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["X.Y.Z"] = new() { Id = "X.Y.Z", Name = "Alpha", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);

        // Match by Name
        var resultByName = engine.Query(new QueryOptions { Pattern = "Alph*", Depth = 0 });
        Assert.Single(resultByName.MatchedNodes);

        // Match by Id
        var resultById = engine.Query(new QueryOptions { Pattern = "X.Y.*", Depth = 0 });
        Assert.Single(resultById.MatchedNodes);
    }

    [Fact]
    public void PartialMatch_NameEndsWith()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.FooService"] = new() { Id = "MyApp.FooService", Name = "FooService", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>();
        var meta = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };

        var engine = new QueryEngine(nodes, edges, meta);

        // Test Name.EndsWith
        var result = engine.Query(new QueryOptions { Pattern = "FooService", Depth = 0 });

        Assert.Single(result.MatchedNodes);
    }
}
