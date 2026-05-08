using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Metrics;

namespace CodeGraph.Query.Tests;

public class QueryCostEstimateTests
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
            ["MyApp.Services.OrderService.PlaceOrder"] = new GraphNode
            {
                Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
                Kind = NodeKind.Method, FilePath = "src/OrderService.cs",
                StartLine = 10, EndLine = 30
            },
            ["MyApp.Data.Repository"] = new GraphNode
            {
                Id = "MyApp.Data.Repository", Name = "Repository",
                Kind = NodeKind.Type, FilePath = "src/Repository.cs",
                StartLine = 1, EndLine = 40
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Data.Repository", Type = EdgeType.Calls },
            new() { FromId = "MyApp.Services.OrderService", ToId = "MyApp.Services.OrderService.PlaceOrder", Type = EdgeType.Contains }
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
    public void EstimateCost_MatchingPattern_ReturnsNonZeroEstimate()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var estimate = engine.EstimateCost("OrderService", new QueryOptions { Depth = 1, MaxNodes = 50 });

        Assert.True(estimate.EstimatedNodes > 0);
        Assert.True(estimate.EstimatedEdges > 0);
        Assert.True(estimate.EstimatedTokensCompact > 0);
        Assert.True(estimate.EstimatedTokensContext > 0);
    }

    [Fact]
    public void EstimateCost_NoMatch_ReturnsZero()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var estimate = engine.EstimateCost("NonExistent", new QueryOptions { Depth = 1, MaxNodes = 50 });

        Assert.Equal(0, estimate.EstimatedNodes);
        Assert.Equal(0, estimate.EstimatedEdges);
        Assert.Equal(0, estimate.EstimatedTokensCompact);
        Assert.Equal(0, estimate.EstimatedTokensContext);
    }

    [Fact]
    public void EstimateCost_HigherDepth_ReturnsMoreTokens()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var estimateD1 = engine.EstimateCost("OrderService", new QueryOptions { Depth = 1, MaxNodes = 100 });
        var estimateD3 = engine.EstimateCost("OrderService", new QueryOptions { Depth = 3, MaxNodes = 100 });

        Assert.True(estimateD3.EstimatedTokensCompact >= estimateD1.EstimatedTokensCompact);
    }

    [Fact]
    public void EstimateCost_CappedByMaxNodes()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var estimate = engine.EstimateCost("OrderService", new QueryOptions { Depth = 10, MaxNodes = 2 });

        Assert.True(estimate.EstimatedNodes <= 2);
    }

    [Fact]
    public void EstimateCost_ContextTokensHigherThanCompact()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);

        var estimate = engine.EstimateCost("OrderService", new QueryOptions { Depth = 1, MaxNodes = 50 });

        Assert.True(estimate.EstimatedTokensContext > estimate.EstimatedTokensCompact);
    }
}
