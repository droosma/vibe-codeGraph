using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class ContextFormatterTests
{
    private static GraphMetadata DefaultMeta => new()
    {
        CommitHash = "a1b2c3d4e5f6",
        Branch = "main",
        GeneratedAt = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Format_IncludesTargetSection()
    {
        var target = new GraphNode
        {
            Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
            Kind = NodeKind.Method, FilePath = "src/Services/OrderService.cs",
            StartLine = 42, EndLine = 67,
            Signature = "public async Task<OrderResult> PlaceOrder(OrderRequest req)",
            DocComment = "Places a new order."
        };

        var neighbor = new GraphNode
        {
            Id = "MyApp.Services.InventoryService.Reserve", Name = "Reserve",
            Kind = NodeKind.Method, FilePath = "src/Services/InventoryService.cs",
            StartLine = 23, EndLine = 45,
            Signature = "public async Task<bool> Reserve(int productId, int quantity)"
        };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [neighbor.Id] = neighbor
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = target.Id, ToId = neighbor.Id, Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result, "PlaceOrder --depth 1 --kind all");

        Assert.Contains("# Subgraph for MyApp.Services.OrderService.PlaceOrder", output);
        Assert.Contains("## Commit: a1b2c3d (main, 2026-04-14)", output);
        Assert.Contains("### Target", output);
        Assert.Contains("- MyApp.Services.OrderService.PlaceOrder", output);
        Assert.Contains("Sig:  public async Task<OrderResult> PlaceOrder(OrderRequest req)", output);
        Assert.Contains("Doc:  Places a new order.", output);
        Assert.Contains("### Calls (outgoing)", output);
        Assert.Contains("- MyApp.Services.InventoryService.Reserve", output);
    }

    [Fact]
    public void Format_IncludesIncomingEdges()
    {
        var target = new GraphNode
        {
            Id = "MyApp.PlaceOrder", Name = "PlaceOrder", Kind = NodeKind.Method,
            FilePath = "src/OrderService.cs", StartLine = 1, EndLine = 10
        };

        var caller = new GraphNode
        {
            Id = "MyApp.Controller.Post", Name = "Post", Kind = NodeKind.Method,
            FilePath = "src/Controller.cs", StartLine = 5, EndLine = 15
        };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [caller.Id] = caller
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = caller.Id, ToId = target.Id, Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### Called by (incoming)", output);
        Assert.Contains("- MyApp.Controller.Post", output);
    }

    [Fact]
    public void Format_ShowsTruncationWarning()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta,
            WasTruncated = true,
            TotalMatchCount = 100
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("⚠ Results truncated", output);
        Assert.Contains("100 total matches", output);
    }

    [Fact]
    public void Format_NoTruncationWarning_WhenNotTruncated()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta,
            WasTruncated = false,
            TotalMatchCount = 1
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("⚠", output);
        Assert.DoesNotContain("truncated", output);
    }

    [Fact]
    public void Format_QueryDescription_AppearsInOutput()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result, "my-query --depth 2");

        Assert.Contains("## Query: my-query --depth 2", output);
    }

    [Fact]
    public void Format_NullQueryDescription_NotInOutput()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result, null);

        Assert.DoesNotContain("## Query:", output);
    }

    [Fact]
    public void Format_CommitHash_TruncatedTo7Chars()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata
            {
                CommitHash = "abcdef1234567890",
                Branch = "feat",
                GeneratedAt = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("## Commit: abcdef1 (feat, 2025-01-15)", output);
        Assert.DoesNotContain("abcdef1234567890", output);
    }

    [Fact]
    public void Format_ShortCommitHash_NotTruncated()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata
            {
                CommitHash = "abc",
                Branch = "dev",
                GeneratedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("## Commit: abc (dev, 2025-06-01)", output);
    }

    [Fact]
    public void Format_NullMetadata_NoCommitLine()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = null!
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("## Commit:", output);
    }

    [Fact]
    public void Format_MultipleMatchedNodes_ShowsMatchedNodesSection()
    {
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { a, b },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = a, ["B"] = b },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### Matched Nodes (2)", output);
        Assert.Contains("- A", output);
        Assert.Contains("- B", output);
        Assert.DoesNotContain("### Target", output);
    }

    [Fact]
    public void Format_HeaderUsesFirstMatchedNode_WhenNoTarget()
    {
        var a = new GraphNode { Id = "First.Node", Name = "Node", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "Second.Node", Name = "Node2", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { a, b },
            Nodes = new Dictionary<string, GraphNode> { ["First.Node"] = a, ["Second.Node"] = b },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("# Subgraph for First.Node", output);
    }

    [Fact]
    public void Format_HeaderFallsBackToQuery_WhenNoMatchedNodes()
    {
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>(),
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("# Subgraph for query", output);
    }

    [Fact]
    public void Format_NodeWithFilePath_ShowsFileAndLineRange()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            FilePath = "src/Foo.cs", StartLine = 10, EndLine = 20
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("File: src/Foo.cs:10-20", output);
    }

    [Fact]
    public void Format_NodeWithEmptyFilePath_NoFileLine()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            FilePath = ""
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("File:", output);
    }

    [Fact]
    public void Format_NodeWithSignature_ShowsSig()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            Signature = "void DoWork()"
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("Sig:  void DoWork()", output);
    }

    [Fact]
    public void Format_NodeWithEmptySignature_NoSigLine()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            Signature = ""
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("Sig:", output);
    }

    [Fact]
    public void Format_NodeWithDocComment_ShowsDoc()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            DocComment = "This is documentation."
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("Doc:  This is documentation.", output);
    }

    [Fact]
    public void Format_NodeWithNullDocComment_NoDocLine()
    {
        var node = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            DocComment = null
        };

        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { ["N"] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("Doc:", output);
    }

    [Fact]
    public void Format_EdgeWithResolution_ShowsResolution()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var dep = new GraphNode { Id = "D", Name = "D", Kind = NodeKind.Type };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["D"] = dep },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "D", Type = EdgeType.ResolvesTo,
                    Resolution = "IFoo → Foo (Scoped)" }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("Resolution: IFoo → Foo (Scoped)", output);
    }

    [Fact]
    public void Format_EdgeWithNullResolution_NoResolutionLine()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var dep = new GraphNode { Id = "D", Name = "D", Kind = NodeKind.Type };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["D"] = dep },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "D", Type = EdgeType.Calls, Resolution = null }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.DoesNotContain("Resolution:", output);
    }

    [Theory]
    [InlineData(EdgeType.Calls, "Calls (outgoing)")]
    [InlineData(EdgeType.Inherits, "Inherits")]
    [InlineData(EdgeType.Implements, "Implements")]
    [InlineData(EdgeType.DependsOn, "Depends on")]
    [InlineData(EdgeType.ResolvesTo, "Resolves via IOC")]
    [InlineData(EdgeType.Covers, "Covered by tests")]
    [InlineData(EdgeType.References, "References (outgoing)")]
    [InlineData(EdgeType.Contains, "Contains")]
    [InlineData(EdgeType.Overrides, "Overrides")]
    public void Format_OutgoingEdgeType_ShowsCorrectHeader(EdgeType edgeType, string expectedHeader)
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var neighbor = new GraphNode { Id = "N", Name = "N", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["N"] = neighbor },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "N", Type = edgeType }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains($"### {expectedHeader}", output);
    }

    [Theory]
    [InlineData(EdgeType.Calls, "Called by (incoming)")]
    [InlineData(EdgeType.Inherits, "Inherited by")]
    [InlineData(EdgeType.Implements, "Implemented by")]
    [InlineData(EdgeType.DependsOn, "Depended on by")]
    [InlineData(EdgeType.ResolvesTo, "Resolved from")]
    [InlineData(EdgeType.Covers, "Covers")]
    [InlineData(EdgeType.References, "Referenced by (incoming)")]
    [InlineData(EdgeType.Contains, "Contained in")]
    [InlineData(EdgeType.Overrides, "Overridden by")]
    public void Format_IncomingEdgeType_ShowsCorrectHeader(EdgeType edgeType, string expectedHeader)
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var caller = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["C"] = caller },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "C", ToId = "T", Type = edgeType }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains($"### {expectedHeader}", output);
    }

    [Fact]
    public void Format_UnknownEdgeInOutgoing_ShowsEnumToString()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var neighbor = new GraphNode { Id = "N", Name = "N", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["N"] = neighbor },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "N", Type = EdgeType.CoveredBy }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### CoveredBy", output);
    }

    [Fact]
    public void Format_UnknownEdgeInIncoming_ShowsFallbackHeader()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var caller = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["C"] = caller },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "C", ToId = "T", Type = EdgeType.CoveredBy }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### CoveredBy (incoming)", output);
    }

    [Fact]
    public void Format_OutgoingEdge_NodeNotInDict_ShowsRawId()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "MissingNode", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("- MissingNode", output);
    }

    [Fact]
    public void Format_IncomingEdge_NodeNotInDict_ShowsRawId()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "MissingCaller", ToId = "T", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("- MissingCaller", output);
    }

    [Fact]
    public void Format_MultipleMatchedNodes_OutgoingGroupedByType()
    {
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method };
        var c = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Type };

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { a, b },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = a, ["B"] = b, ["C"] = c },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "C", Type = EdgeType.Calls },
                new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### Calls (outgoing)", output);
        Assert.Contains("- C", output);
    }

    [Fact]
    public void Format_IncomingEdge_ExcludesSelfEdgesFromTargetSet()
    {
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { a, b },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = a, ["B"] = b },
            Edges = new List<GraphEdge>
            {
                // Edge from A to B — both are targets, should appear as outgoing not incoming
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        // A→B is outgoing (FromId is in targetIds)
        Assert.Contains("### Calls (outgoing)", output);
        // Should NOT appear as incoming because FromId (A) is also a target
        Assert.DoesNotContain("Called by (incoming)", output);
    }

    [Fact]
    public void Format_NeighborNodeDetail_ShowsAllFields()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var neighbor = new GraphNode
        {
            Id = "N", Name = "N", Kind = NodeKind.Method,
            FilePath = "src/N.cs", StartLine = 5, EndLine = 15,
            Signature = "void N()",
            DocComment = "Does N things."
        };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["N"] = neighbor },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "N", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("- N", output);
        Assert.Contains("File: src/N.cs:5-15", output);
        Assert.Contains("Sig:  void N()", output);
        Assert.Contains("Doc:  Does N things.", output);
    }

    [Fact]
    public void Format_TruncationWarning_ContainsExactMessage()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta,
            WasTruncated = true,
            TotalMatchCount = 42
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("⚠ Results truncated. Showing subset of 42 total matches.", output);
    }

    [Fact]
    public void Format_DateFormat_IsYYYYMMDD()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["X"] = new() { Id = "X", Name = "X", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata
            {
                CommitHash = "1234567890",
                Branch = "release",
                GeneratedAt = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero)
            }
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("2024-12-31", output);
        Assert.Contains("1234567", output);
        Assert.Contains("release", output);
    }

    [Fact]
    public void Format_TargetUsesTargetNodeId_ForHeader()
    {
        var target = new GraphNode { Id = "MyTarget.Id", Name = "Id", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["MyTarget.Id"] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.StartsWith("# Subgraph for MyTarget.Id", output);
    }

    [Fact]
    public void Format_OutgoingEdges_OrderedByEdgeType()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var n1 = new GraphNode { Id = "N1", Name = "N1", Kind = NodeKind.Method };
        var n2 = new GraphNode { Id = "N2", Name = "N2", Kind = NodeKind.Type };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["N1"] = n1, ["N2"] = n2 },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "N1", Type = EdgeType.References },
                new() { FromId = "T", ToId = "N2", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        // Calls (enum value 1) should appear before References (enum value 8)
        var callsIdx = output.IndexOf("### Calls (outgoing)");
        var refsIdx = output.IndexOf("### References (outgoing)");

        Assert.True(callsIdx >= 0, "Calls header not found");
        Assert.True(refsIdx >= 0, "References header not found");
        Assert.True(callsIdx < refsIdx, "Calls should appear before References (ordered by EdgeType)");
    }

    [Fact]
    public void Format_IncomingEdges_OrderedByEdgeType()
    {
        var target = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method };
        var c1 = new GraphNode { Id = "C1", Name = "C1", Kind = NodeKind.Method };
        var c2 = new GraphNode { Id = "C2", Name = "C2", Kind = NodeKind.Type };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["C1"] = c1, ["C2"] = c2 },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "C1", ToId = "T", Type = EdgeType.References },
                new() { FromId = "C2", ToId = "T", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        var calledByIdx = output.IndexOf("### Called by (incoming)");
        var refdByIdx = output.IndexOf("### Referenced by (incoming)");

        Assert.True(calledByIdx >= 0, "Called by header not found");
        Assert.True(refdByIdx >= 0, "Referenced by header not found");
        Assert.True(calledByIdx < refdByIdx, "Called by should appear before Referenced by (ordered by EdgeType)");
    }

    [Fact]
    public void Format_BlankLinesBetweenSections()
    {
        var target = new GraphNode
        {
            Id = "T", Name = "T", Kind = NodeKind.Method,
            FilePath = "f.cs", StartLine = 1, EndLine = 5
        };
        var neighbor = new GraphNode { Id = "N", Name = "N", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["T"] = target, ["N"] = neighbor },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "T", ToId = "N", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // After the target section, there should be a blank line before the next section
        var targetIdx = Array.FindIndex(lines, l => l == "### Target");
        Assert.True(targetIdx >= 0);

        // Find the blank line after target details
        var callsIdx = Array.FindIndex(lines, targetIdx, l => l.StartsWith("### Calls"));
        Assert.True(callsIdx > targetIdx);

        // There should be a blank line between target section and calls section
        var blankBetween = lines.Skip(targetIdx + 1).Take(callsIdx - targetIdx - 1).Any(l => l == "");
        Assert.True(blankBetween, "Expected blank line between Target and Calls sections");
    }

    [Fact]
    public void Format_MatchedNodesSection_HasBlankLineAfter()
    {
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method };
        var c = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { a, b },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = a, ["B"] = b, ["C"] = c },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "C", Type = EdgeType.Calls }
            },
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Matched Nodes section header exists
        var matchedIdx = Array.FindIndex(lines, l => l.StartsWith("### Matched Nodes"));
        Assert.True(matchedIdx >= 0);

        // Calls section exists after
        var callsIdx = Array.FindIndex(lines, matchedIdx, l => l.StartsWith("### Calls"));
        Assert.True(callsIdx > matchedIdx);

        // There's a blank line between them
        var blankBetween = lines.Skip(matchedIdx + 1).Take(callsIdx - matchedIdx - 1).Any(l => l == "");
        Assert.True(blankBetween);
    }

    [Fact]
    public void Format_NullCoalescing_UsesTargetNodeId_WhenAvailable()
    {
        var target = new GraphNode { Id = "Specific.Id", Name = "Id", Kind = NodeKind.Method };
        var other = new GraphNode { Id = "Other.Id", Name = "Other", Kind = NodeKind.Method };

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target, other },
            Nodes = new Dictionary<string, GraphNode> { ["Specific.Id"] = target, ["Other.Id"] = other },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        // Should use TargetNode.Id, not first matched node
        Assert.Contains("# Subgraph for Specific.Id", output);
    }

    [Fact]
    public void Format_MatchedNodesCount_ShowsExactCount()
    {
        var nodes = Enumerable.Range(1, 5).Select(i => new GraphNode
        {
            Id = $"Node{i}", Name = $"Node{i}", Kind = NodeKind.Method
        }).ToList();

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = nodes,
            Nodes = nodes.ToDictionary(n => n.Id),
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("### Matched Nodes (5)", output);
    }

    [Fact]
    public void Format_MatchedNodesGreaterThanZero_ShowsSection()
    {
        // Testing that count > 0 check works (mutation changes to count >= 0)
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };

        // When MatchedNodes is empty and TargetNode is null, no matched nodes section
        var resultEmpty = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>(),
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMeta
        };

        var outputEmpty = ContextFormatter.Format(resultEmpty);
        Assert.DoesNotContain("### Matched Nodes", outputEmpty);
        Assert.DoesNotContain("### Target", outputEmpty);
    }
}
