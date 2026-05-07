using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class CompactFormatterTests
{
    [Fact]
    public void Format_SingleTarget_ShowsArrowNotation()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);

        Assert.Contains("→ calls:", output);
        Assert.Contains("← calls:", output);
    }

    [Fact]
    public void Format_GroupsMultipleCallsToSameType()
    {
        var result = BuildResultWithMultipleCalls();
        var output = CompactFormatter.Format(result);

        // Multiple targets should be comma-separated on one line
        var lines = output.Split('\n');
        var callsLine = lines.FirstOrDefault(l => l.Contains("→ calls:"));
        Assert.NotNull(callsLine);
        Assert.Contains(",", callsLine);
    }

    [Fact]
    public void Format_StripsCommonNamespacePrefix()
    {
        var result = BuildResultWithSharedNamespace();
        var output = CompactFormatter.Format(result);

        // Should not contain the full repeated prefix
        Assert.DoesNotContain("MyApp.Services.MyApp.Services", output);
    }

    [Fact]
    public void Format_ProducesFewerTokensThanContext()
    {
        var result = BuildSimpleResult();
        var compactOutput = CompactFormatter.Format(result);
        var contextOutput = ContextFormatter.Format(result);

        // Compact should be significantly shorter
        Assert.True(compactOutput.Length < contextOutput.Length * 0.8,
            $"Compact ({compactOutput.Length} chars) should be <80% of context ({contextOutput.Length} chars)");
    }

    [Fact]
    public void DetectCommonPrefix_FindsSharedNamespace()
    {
        var ids = new[] { "MyApp.Services.OrderService", "MyApp.Services.InventoryService", "MyApp.Services.PaymentService" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("MyApp.Services.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_EmptyForSingleId()
    {
        var ids = new[] { "MyApp.Services.OrderService" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_EmptyWhenNoCommonPrefix()
    {
        var ids = new[] { "Alpha.Service", "Beta.Controller", "Gamma.Repository" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void StripPrefix_RemovesPrefix()
    {
        var result = CompactFormatter.StripPrefix("MyApp.Services.OrderService", "MyApp.Services.");
        Assert.Equal("OrderService", result);
    }

    [Fact]
    public void StripPrefix_ReturnsFullId_WhenNoPrefixMatch()
    {
        var result = CompactFormatter.StripPrefix("Other.Thing", "MyApp.Services.");
        Assert.Equal("Other.Thing", result);
    }

    [Fact]
    public void Format_ShowsTruncationWarning()
    {
        var result = BuildSimpleResult() with { WasTruncated = true, TotalMatchCount = 150 };
        var output = CompactFormatter.Format(result);
        Assert.Contains("⚠", output);
        Assert.Contains("150", output);
    }

    private static QueryResult BuildSimpleResult()
    {
        var target = new GraphNode
        {
            Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
            Kind = NodeKind.Method, FilePath = "src/Services/OrderService.cs",
            StartLine = 42, EndLine = 67,
            Signature = "public async Task<OrderResult> PlaceOrder(OrderRequest req)",
            ContainingTypeId = "MyApp.Services.OrderService",
            ContainingNamespaceId = "MyApp.Services",
            Accessibility = Accessibility.Public
        };

        var nodes = new Dictionary<string, GraphNode>
        {
            [target.Id] = target,
            ["MyApp.Services.InventoryService.Reserve"] = new GraphNode
            {
                Id = "MyApp.Services.InventoryService.Reserve", Name = "Reserve",
                Kind = NodeKind.Method, FilePath = "src/Services/InventoryService.cs",
                StartLine = 23, EndLine = 45,
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public
            },
            ["MyApp.Controllers.OrderController.Post"] = new GraphNode
            {
                Id = "MyApp.Controllers.OrderController.Post", Name = "Post",
                Kind = NodeKind.Method, FilePath = "src/Controllers/OrderController.cs",
                StartLine = 18, EndLine = 30,
                ContainingNamespaceId = "MyApp.Controllers",
                Accessibility = Accessibility.Public
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = target.Id, ToId = "MyApp.Services.InventoryService.Reserve", Type = EdgeType.Calls },
            new() { FromId = "MyApp.Controllers.OrderController.Post", ToId = target.Id, Type = EdgeType.Calls }
        };

        return new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                SchemaVersion = 1,
                GeneratedAt = DateTime.UtcNow,
                Solution = "Test.sln",
                SolutionName = "Test",
                CommitHash = "abc1234",
                Branch = "main",
                ProjectsIndexed = new[] { "MyApp" }
            },
            WasTruncated = false,
            TotalMatchCount = 1
        };
    }

    private static QueryResult BuildResultWithMultipleCalls()
    {
        var target = new GraphNode
        {
            Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
            Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services",
            Accessibility = Accessibility.Public
        };

        var nodes = new Dictionary<string, GraphNode>
        {
            [target.Id] = target,
            ["MyApp.Services.InventoryService.Reserve"] = new GraphNode { Id = "MyApp.Services.InventoryService.Reserve", Name = "Reserve", Kind = NodeKind.Method, Accessibility = Accessibility.Public },
            ["MyApp.Services.PaymentService.Charge"] = new GraphNode { Id = "MyApp.Services.PaymentService.Charge", Name = "Charge", Kind = NodeKind.Method, Accessibility = Accessibility.Public }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = target.Id, ToId = "MyApp.Services.InventoryService.Reserve", Type = EdgeType.Calls },
            new() { FromId = target.Id, ToId = "MyApp.Services.PaymentService.Charge", Type = EdgeType.Calls }
        };

        return new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "Test.sln", SolutionName = "Test", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false,
            TotalMatchCount = 1
        };
    }

    private static QueryResult BuildResultWithSharedNamespace()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.OrderService"] = new GraphNode { Id = "MyApp.Services.OrderService", Name = "OrderService", Kind = NodeKind.Type, ContainingNamespaceId = "MyApp.Services", Accessibility = Accessibility.Public },
            ["MyApp.Services.InventoryService"] = new GraphNode { Id = "MyApp.Services.InventoryService", Name = "InventoryService", Kind = NodeKind.Type, ContainingNamespaceId = "MyApp.Services", Accessibility = Accessibility.Public },
            ["MyApp.Services.PaymentService"] = new GraphNode { Id = "MyApp.Services.PaymentService", Name = "PaymentService", Kind = NodeKind.Type, ContainingNamespaceId = "MyApp.Services", Accessibility = Accessibility.Public }
        };

        return new QueryResult
        {
            TargetNode = nodes.Values.First(),
            MatchedNodes = new List<GraphNode> { nodes.Values.First() },
            Nodes = nodes,
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "Test.sln", SolutionName = "Test", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false,
            TotalMatchCount = 1
        };
    }
}
