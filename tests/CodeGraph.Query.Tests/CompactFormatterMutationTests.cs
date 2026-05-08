using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

/// <summary>
/// Mutation-killing tests for CompactFormatter.
/// Verifies exact formatting, prefix detection, edge type rendering, and all branches.
/// </summary>
public class CompactFormatterMutationTests
{
    private static GraphMetadata DefaultMetadata => new()
    {
        SchemaVersion = 1,
        GeneratedAt = DateTime.UtcNow,
        Solution = "T.sln",
        SolutionName = "T",
        CommitHash = "abc",
        Branch = "main",
        ProjectsIndexed = Array.Empty<string>()
    };

    private static GraphNode MakeNode(
        string id,
        NodeKind kind = NodeKind.Method,
        string? filePath = null,
        int startLine = 0,
        int endLine = 0,
        string? docComment = null) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        FilePath = filePath ?? string.Empty,
        StartLine = startLine,
        EndLine = endLine,
        DocComment = docComment,
        Accessibility = Accessibility.Public
    };

    #region DetectCommonPrefix tests

    [Fact]
    public void DetectCommonPrefix_TwoIds_SharedFullSegment()
    {
        var ids = new[] { "A.B.Foo", "A.B.Bar" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("A.B.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_ThreeIds_DifferentLastSegments()
    {
        var ids = new[] { "Ns.Sub.ClassA", "Ns.Sub.ClassB", "Ns.Sub.ClassC" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("Ns.Sub.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_NoDotInCommonPart_ReturnsEmpty()
    {
        // "Abc" and "Abd" share "Ab" but no dot → empty
        var ids = new[] { "Abc", "Abd" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_SingleItem_ReturnsEmpty()
    {
        var ids = new[] { "Only.One.Item" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_EmptyList_ReturnsEmpty()
    {
        var prefix = CompactFormatter.DetectCommonPrefix(Array.Empty<string>());
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_IdenticalIds_ReturnsUpToLastDot()
    {
        var ids = new[] { "A.B.Same", "A.B.Same" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("A.B.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_SecondShorterThanFirst_HandlesCorrectly()
    {
        var ids = new[] { "Namespace.Long.Name", "Namespace.Lo" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("Namespace.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_FirstShorterThanSecond_HandlesCorrectly()
    {
        var ids = new[] { "A.B", "A.B.LongerName" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal("A.", prefix);
    }

    [Fact]
    public void DetectCommonPrefix_CompletelyDifferent_ReturnsEmpty()
    {
        var ids = new[] { "Foo.Bar", "Xyz.Abc" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    [Fact]
    public void DetectCommonPrefix_DotAtPositionZero_ReturnsEmpty()
    {
        // If the dot is at position 0, lastDot is 0, condition is lastDot > 0, so returns empty
        var ids = new[] { ".Foo", ".Bar" };
        var prefix = CompactFormatter.DetectCommonPrefix(ids);
        Assert.Equal(string.Empty, prefix);
    }

    #endregion

    #region StripPrefix tests

    [Fact]
    public void StripPrefix_MatchingPrefix_StripsIt()
    {
        Assert.Equal("Suffix", CompactFormatter.StripPrefix("Prefix.Suffix", "Prefix."));
    }

    [Fact]
    public void StripPrefix_NonMatchingPrefix_ReturnsOriginal()
    {
        Assert.Equal("Other.Name", CompactFormatter.StripPrefix("Other.Name", "Prefix."));
    }

    [Fact]
    public void StripPrefix_EmptyPrefix_ReturnsOriginal()
    {
        Assert.Equal("Full.Name", CompactFormatter.StripPrefix("Full.Name", ""));
    }

    [Fact]
    public void StripPrefix_NullPrefix_ReturnsOriginal()
    {
        Assert.Equal("Full.Name", CompactFormatter.StripPrefix("Full.Name", null!));
    }

    [Fact]
    public void StripPrefix_ExactMatch_ReturnsEmpty()
    {
        Assert.Equal("", CompactFormatter.StripPrefix("Exact.", "Exact."));
    }

    [Fact]
    public void StripPrefix_CaseSensitive_DoesNotStrip()
    {
        // Ordinal comparison - case matters
        Assert.Equal("prefix.Name", CompactFormatter.StripPrefix("prefix.Name", "Prefix."));
    }

    #endregion

    #region Format header tests

    [Fact]
    public void Format_TargetNode_HeaderUsesStrippedId()
    {
        var target = MakeNode("Ns.Sub.MyClass", NodeKind.Type);
        var other = MakeNode("Ns.Sub.Other", NodeKind.Type);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target, [other.Id] = other },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        // With common prefix "Ns.Sub.", header should be stripped
        Assert.Contains("# MyClass", output);
    }

    [Fact]
    public void Format_NullTargetNode_UsesFirstMatchedNodeId()
    {
        var node = MakeNode("First.Match");
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("# First.Match", output);
    }

    [Fact]
    public void Format_NullTargetNoMatches_FallbackToQuery()
    {
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>(),
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 0
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("# query", output);
    }

    #endregion

    #region AppendNodeBlock tests

    [Fact]
    public void Format_NodeBlock_KindIsLowercase()
    {
        var target = MakeNode("MyMethod", NodeKind.Method, "src/f.cs", 1, 5);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("[method", output);
        Assert.DoesNotContain("[Method", output);
    }

    [Fact]
    public void Format_NodeBlock_WithFile_IncludesFileInfo()
    {
        var target = MakeNode("A", NodeKind.Type, "src/A.cs", 10, 50);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains(", src/A.cs:10-50]", output);
    }

    [Fact]
    public void Format_NodeBlock_WithoutFile_NoCommaFileInfo()
    {
        var target = MakeNode("A", NodeKind.Type);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("## A [type]", output);
        Assert.DoesNotContain(", ", output.Split('\n').First(l => l.StartsWith("## A")));
    }

    [Fact]
    public void Format_NodeBlock_WithDocComment_IndentedBelow()
    {
        var target = MakeNode("A", NodeKind.Type, docComment: "My doc");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("  My doc", output);
    }

    [Fact]
    public void Format_NodeBlock_WithoutDocComment_NoDocLine()
    {
        var target = MakeNode("A", NodeKind.Type);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);
        var lines = output.Split('\n');

        // No indented doc line
        Assert.DoesNotContain(lines, l => l.StartsWith("  ") && !l.StartsWith("  →") && !l.StartsWith("  ←") && l.Trim().Length > 0 && !l.Contains("→") && !l.Contains("←"));
    }

    #endregion

    #region Edge rendering tests

    [Fact]
    public void Format_OutgoingEdge_TwoSpaceArrow()
    {
        var target = MakeNode("A");
        var dep = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = dep },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("  → calls: B", output);
    }

    [Fact]
    public void Format_IncomingEdge_TwoSpaceArrow()
    {
        var target = MakeNode("A");
        var caller = MakeNode("Caller");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "A", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["Caller"] = caller },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("  ← calls: Caller", output);
    }

    [Fact]
    public void Format_MultipleOutgoingEdges_CommaSeparated()
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b, ["C"] = c },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        var callsLine = output.Split('\n').First(l => l.Contains("→ calls:"));
        Assert.Contains(", ", callsLine);
    }

    [Fact]
    public void Format_EdgeConfidence_Verified_NoAnnotation()
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Confidence = EdgeConfidence.Verified }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.DoesNotContain("[verified]", output);
    }

    [Fact]
    public void Format_EdgeConfidence_Inferred_ShowsLowercase()
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Confidence = EdgeConfidence.Inferred }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("[inferred]", output);
        Assert.DoesNotContain("[Inferred]", output);
    }

    [Fact]
    public void Format_EdgeConfidence_Unresolved_ShowsLowercase()
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Confidence = EdgeConfidence.Unresolved }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("[unresolved]", output);
        Assert.DoesNotContain("[Unresolved]", output);
    }

    [Fact]
    public void Format_IncomingEdgeFromTarget_Excluded()
    {
        var target1 = MakeNode("A");
        var target2 = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { target1, target2 },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target1, ["B"] = target2 },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 2
        };
        var output = CompactFormatter.Format(result);

        // B should not show an incoming edge from A since A is also a target
        Assert.DoesNotContain("← calls:", output);
    }

    [Fact]
    public void Format_IncomingEdgeFromNonTarget_Shown()
    {
        var target = MakeNode("A");
        var external = MakeNode("External");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "External", ToId = "A", Type = EdgeType.References }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["External"] = external },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("← references: External", output);
    }

    #endregion

    #region FormatEdgeType tests

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
    public void Format_EdgeType_CorrectlyFormatted(EdgeType edgeType, string expected)
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = edgeType }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains($"→ {expected}: B", output);
    }

    #endregion

    #region Related section tests

    [Fact]
    public void Format_RelatedSection_RendersNonTargetNodes()
    {
        var target = MakeNode("A");
        var related = MakeNode("B", NodeKind.Type);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = related },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("## Related", output);
        Assert.Contains("- B [type]", output);
    }

    [Fact]
    public void Format_RelatedSection_OrderedByKindThenId()
    {
        var target = MakeNode("A");
        // NodeKind enum: Namespace=0, Type=1, Method=2, Property=3, Field=4, Event=5, Constructor=6
        var n1 = MakeNode("Z.Class", NodeKind.Type);      // Kind=1
        var n2 = MakeNode("A.Method", NodeKind.Method);    // Kind=2
        var n3 = MakeNode("B.Class", NodeKind.Type);       // Kind=1
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = target, ["Z.Class"] = n1, ["A.Method"] = n2, ["B.Class"] = n3
            },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        // Type (1) comes before Method (2) in enum order, and within Type: B.Class before Z.Class
        var bClassIdx = output.IndexOf("B.Class");
        var zClassIdx = output.IndexOf("Z.Class");
        var methodIdx = output.IndexOf("A.Method");
        Assert.True(bClassIdx < zClassIdx, "B.Class before Z.Class (alphabetical within same kind)");
        Assert.True(zClassIdx < methodIdx, "Type (Kind=1) should come before Method (Kind=2)");
    }

    [Fact]
    public void Format_NoRelatedNodes_NoRelatedSection()
    {
        var target = MakeNode("Only");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["Only"] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.DoesNotContain("## Related", output);
    }

    [Fact]
    public void Format_CompactNode_KindLowercase()
    {
        var target = MakeNode("A");
        var related = MakeNode("B", NodeKind.Constructor);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = related },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("- B [constructor]", output);
        Assert.DoesNotContain("[Constructor]", output);
    }

    #endregion

    #region Truncation warning tests

    [Fact]
    public void Format_WasTruncated_ShowsWarningWithCount()
    {
        var target = MakeNode("A");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = true, TotalMatchCount = 42
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("⚠ Truncated (42 total matches)", output);
    }

    [Fact]
    public void Format_NotTruncated_NoWarning()
    {
        var target = MakeNode("A");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.DoesNotContain("⚠", output);
        Assert.DoesNotContain("Truncated", output);
    }

    #endregion

    #region Prefix stripping in edge targets tests

    [Fact]
    public void Format_EdgeTargets_PrefixStripped()
    {
        var target = MakeNode("Ns.Sub.Source", NodeKind.Method);
        var dep = MakeNode("Ns.Sub.Target", NodeKind.Method);
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Ns.Sub.Source", ToId = "Ns.Sub.Target", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["Ns.Sub.Source"] = target, ["Ns.Sub.Target"] = dep },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        // With common prefix "Ns.Sub.", edge targets should show just "Target"
        Assert.Contains("→ calls: Target", output);
        Assert.DoesNotContain("Ns.Sub.Target", output);
    }

    [Fact]
    public void Format_IncomingEdgeSources_PrefixStripped()
    {
        var target = MakeNode("Ns.Sub.Dest", NodeKind.Method);
        var caller = MakeNode("Ns.Sub.Caller", NodeKind.Method);
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Ns.Sub.Caller", ToId = "Ns.Sub.Dest", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["Ns.Sub.Dest"] = target, ["Ns.Sub.Caller"] = caller },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("← calls: Caller", output);
        Assert.DoesNotContain("Ns.Sub.Caller", output);
    }

    #endregion

    #region TargetId not in Nodes - continue branch

    [Fact]
    public void Format_TargetIdNotInNodes_SkipsBlock()
    {
        var target = MakeNode("Missing");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>(), // target not in Nodes dict
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        // Should still have header but no ## block for the missing node
        Assert.Contains("# Missing", output);
        Assert.DoesNotContain("## Missing [", output);
    }

    #endregion

    #region Output trimming

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var target = MakeNode("A");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Equal(output, output.TrimEnd());
    }

    #endregion

    #region All NodeKinds in compact format

    [Theory]
    [InlineData(NodeKind.Namespace, "namespace")]
    [InlineData(NodeKind.Type, "type")]
    [InlineData(NodeKind.Method, "method")]
    [InlineData(NodeKind.Property, "property")]
    [InlineData(NodeKind.Field, "field")]
    [InlineData(NodeKind.Event, "event")]
    [InlineData(NodeKind.Constructor, "constructor")]
    public void Format_AllKinds_RenderedLowercase(NodeKind kind, string expected)
    {
        var target = MakeNode("Target");
        var related = MakeNode("Related", kind);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["Target"] = target, ["Related"] = related },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains($"- Related [{expected}]", output);
    }

    #endregion

    #region Multiple matched nodes (TargetNode null)

    [Fact]
    public void Format_MultipleMatchedNodes_AllGetBlocks()
    {
        var n1 = MakeNode("A", NodeKind.Method);
        var n2 = MakeNode("B", NodeKind.Type);
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { n1, n2 },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = n1, ["B"] = n2 },
            Edges = new List<GraphEdge>(),
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 2
        };
        var output = CompactFormatter.Format(result);

        Assert.Contains("## A [method]", output);
        Assert.Contains("## B [type]", output);
    }

    #endregion

    #region Edge grouping and ordering

    [Fact]
    public void Format_EdgesGroupedByType_SortedByKey()
    {
        var target = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Inherits },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls }
        };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { ["A"] = target, ["B"] = b, ["C"] = c },
            Edges = edges,
            Metadata = DefaultMetadata,
            WasTruncated = false, TotalMatchCount = 1
        };
        var output = CompactFormatter.Format(result);

        // Calls (1) before Inherits (2) in enum order
        var callsIdx = output.IndexOf("→ calls:");
        var inheritsIdx = output.IndexOf("→ inherits:");
        Assert.True(callsIdx < inheritsIdx);
    }

    #endregion
}
