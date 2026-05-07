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

    [Fact]
    public void Format_NotTruncated_NoWarning()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        Assert.DoesNotContain("⚠", output);
    }

    [Fact]
    public void Format_TargetNodeShownAsHeader()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        Assert.Contains("PlaceOrder", output);
    }

    [Fact]
    public void Format_TargetNodeBlock_ContainsKindAndFile()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);

        Assert.Contains("[method", output);
        Assert.Contains("src/Services/OrderService.cs:42-67", output);
    }

    [Fact]
    public void Format_RelatedSection_ShowsNonTargetNodes()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);

        Assert.Contains("## Related", output);
    }

    [Fact]
    public void Format_NoRelatedNodes_OmitsRelatedSection()
    {
        var target = new GraphNode
        {
            Id = "Only.Node", Name = "Node",
            Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.DoesNotContain("## Related", output);
    }

    [Fact]
    public void Format_EdgeConfidenceInferred_ShowsAnnotation()
    {
        var target = new GraphNode
        {
            Id = "A.Run", Name = "Run", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var dep = new GraphNode
        {
            Id = "B.Helper", Name = "Helper", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Run", ToId = "B.Helper", Type = EdgeType.Calls, Confidence = EdgeConfidence.Inferred }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target, [dep.Id] = dep },
            Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains("[inferred]", output);
    }

    [Fact]
    public void Format_EdgeConfidenceVerified_NoAnnotation()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        Assert.DoesNotContain("[verified]", output);
    }

    [Fact]
    public void Format_DocComment_ShownUnderNode()
    {
        var target = new GraphNode
        {
            Id = "A.Run", Name = "Run", Kind = NodeKind.Method,
            DocComment = "Does important stuff.", Accessibility = Accessibility.Public
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains("Does important stuff.", output);
    }

    [Fact]
    public void Format_IncomingEdgeFromTarget_NotDuplicated()
    {
        // When an edge is between two targets, incoming section should skip it
        var target1 = new GraphNode
        {
            Id = "A.Run", Name = "Run", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var target2 = new GraphNode
        {
            Id = "A.Helper", Name = "Helper", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Run", ToId = "A.Helper", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { target1, target2 },
            Nodes = new Dictionary<string, GraphNode> { [target1.Id] = target1, [target2.Id] = target2 },
            Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 2
        };
        var output = CompactFormatter.Format(result);

        // The outgoing edge for A.Run → A.Helper should show
        Assert.Contains("→ calls:", output);
        // But A.Helper should NOT show incoming from A.Run (since A.Run is a target)
        Assert.DoesNotContain("← calls:", output);
    }

    [Fact]
    public void Format_CompactNodeInRelated_ShowsKindInBrackets()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        var lines = output.Split('\n');

        // Related nodes should use "- name [kind]" format
        var relatedLines = lines.Where(l => l.StartsWith("- ")).ToList();
        Assert.True(relatedLines.Count > 0);
        Assert.All(relatedLines, l => Assert.Contains("[method]", l));
    }

    [Fact]
    public void Format_MultipleEdgeTypes_AllFormatted()
    {
        var target = new GraphNode
        {
            Id = "A.Type", Name = "Type", Kind = NodeKind.Type, Accessibility = Accessibility.Public
        };
        var nodes = new Dictionary<string, GraphNode>
        {
            [target.Id] = target,
            ["B.Base"] = new GraphNode { Id = "B.Base", Name = "Base", Kind = NodeKind.Type, Accessibility = Accessibility.Public },
            ["C.IFace"] = new GraphNode { Id = "C.IFace", Name = "IFace", Kind = NodeKind.Type, Accessibility = Accessibility.Public }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Type", ToId = "B.Base", Type = EdgeType.Inherits },
            new() { FromId = "A.Type", ToId = "C.IFace", Type = EdgeType.Implements }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = nodes, Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("→ inherits:", output);
        Assert.Contains("→ implements:", output);
    }

    [Fact]
    public void Format_TargetNodeNull_UsesFirstMatchedNodeId()
    {
        var node = new GraphNode
        {
            Id = "First.Match", Name = "Match", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains("# First.Match", output);
    }

    [Fact]
    public void Format_NoTargetNoMatches_FallsBackToQuery()
    {
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>(),
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 0
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains("# query", output);
    }

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void DetectCommonPrefix_EmptyCollection_ReturnsEmpty()
    {
        var prefix = CompactFormatter.DetectCommonPrefix(Enumerable.Empty<string>());
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_PartialSegmentMatch_TrimsToLastDot()
    {
        // "A.B.Cx" and "A.B.Cy" share "A.B.C" but that's not a full segment → "A.B."
        var ids = new[] { "A.B.Cx", "A.B.Cy" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("A.B.", prefix);
    }

    [Fact]
    public void StripPrefix_EmptyPrefix_ReturnsOriginal()
    {
        var result = CompactFormatter.StripPrefix("Some.Id", "");
        Assert.Equal("Some.Id", result);
    }

    [Fact]
    public void StripPrefix_NullPrefix_ReturnsOriginal()
    {
        var result = CompactFormatter.StripPrefix("Some.Id", null!);
        Assert.Equal("Some.Id", result);
    }

    [Theory]
    [InlineData(EdgeType.Calls, "calls")]
    [InlineData(EdgeType.Inherits, "inherits")]
    [InlineData(EdgeType.Implements, "implements")]
    [InlineData(EdgeType.DependsOn, "depends-on")]
    [InlineData(EdgeType.ResolvesTo, "resolves-to")]
    [InlineData(EdgeType.Covers, "covers")]
    [InlineData(EdgeType.CoveredBy, "covered-by")]
    [InlineData(EdgeType.References, "references")]
    [InlineData(EdgeType.Contains, "contains")]
    [InlineData(EdgeType.Overrides, "overrides")]
    public void Format_EdgeType_FormattedCorrectly(EdgeType edgeType, string expected)
    {
        var target = new GraphNode
        {
            Id = "A", Name = "A", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var dep = new GraphNode
        {
            Id = "B", Name = "B", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = edgeType }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = dep },
            Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains($"→ {expected}:", output);
    }

    [Fact]
    public void Format_EdgeConfidenceUnresolved_ShowsAnnotation()
    {
        var target = new GraphNode
        {
            Id = "A", Name = "A", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var dep = new GraphNode
        {
            Id = "B", Name = "B", Kind = NodeKind.Method, Accessibility = Accessibility.Public
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Confidence = EdgeConfidence.Unresolved }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = dep },
            Edges = edges,
            Metadata = new GraphMetadata { SchemaVersion = 1, GeneratedAt = DateTime.UtcNow, Solution = "T.sln", SolutionName = "T", CommitHash = "abc", Branch = "main", ProjectsIndexed = Array.Empty<string>() },
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        Assert.Contains("[unresolved]", output);
    }

    [Fact]
    public void Format_IncomingEdgeFromNonTarget_Shown()
    {
        var result = BuildSimpleResult();
        var output = CompactFormatter.Format(result);
        Assert.Contains("← calls:", output);
        Assert.Contains("OrderController.Post", output);
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
