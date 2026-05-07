using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class GraphDiffContextFormatterTests
{
    private static GraphMetadata BaseMeta => new() { CommitHash = "abc1234567", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
    private static GraphMetadata HeadMeta => new() { CommitHash = "def5678901", Branch = "feature", GeneratedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void Format_EmptyDiff_ShowsNoneForAllSections()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>(),
            RemovedNodes = new List<GraphNode>(),
            SignatureChangedNodes = new List<GraphSignatureChange>(),
            AddedEdges = new List<GraphEdge>(),
            RemovedEdges = new List<GraphEdge>()
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: abc1234..def5678", output);
        Assert.Contains("## Added Nodes (0)", output);
        Assert.Contains("## Removed Nodes (0)", output);
        Assert.Contains("## Changed Signatures (0)", output);
        Assert.Contains("## New Edges (0)", output);
        Assert.Contains("## Removed Edges (0)", output);
        // "None" should appear for each empty section
        var noneCount = output.Split("- None").Length - 1;
        Assert.True(noneCount >= 5, $"Expected at least 5 '- None' markers, got {noneCount}");
    }

    [Fact]
    public void Format_AddedNodes_WithFilePath_ShowsFileInfo()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.NewService", Name = "NewService", Kind = NodeKind.Type,
                    FilePath = "src/NewService.cs", StartLine = 5, EndLine = 50 }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- MyApp.NewService (Type) — src/NewService.cs:5-50", output);
    }

    [Fact]
    public void Format_AddedNodes_WithFilePathNoLineInfo_ShowsFileOnly()
    {
        // Targets L63: StartLine > 0 && EndLine > 0 condition
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.Svc", Name = "Svc", Kind = NodeKind.Type,
                    FilePath = "src/Svc.cs", StartLine = 0, EndLine = 0 }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- MyApp.Svc (Type) — src/Svc.cs", output);
        Assert.DoesNotContain(":0-0", output);
    }

    [Fact]
    public void Format_AddedNodes_NoFilePath_ShowsIdAndKindOnly()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.Ns", Name = "Ns", Kind = NodeKind.Namespace, FilePath = "" }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- MyApp.Ns (Namespace)", output);
        Assert.DoesNotContain("—", output.Split("## Added Nodes")[1].Split("##")[0]);
    }

    [Fact]
    public void Format_RemovedNodes_Listed()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            RemovedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.Old", Name = "Old", Kind = NodeKind.Method,
                    FilePath = "src/Old.cs", StartLine = 10, EndLine = 20 }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("## Removed Nodes (1)", output);
        Assert.Contains("- MyApp.Old (Method) — src/Old.cs:10-20", output);
    }

    [Fact]
    public void Format_SignatureChanges_ShowsWasAndNow()
    {
        // Targets L34: statement mutations
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            SignatureChangedNodes = new List<GraphSignatureChange>
            {
                new()
                {
                    Previous = new GraphNode { Id = "M.Do", Name = "Do", Kind = NodeKind.Method, Signature = "void Do()" },
                    Current = new GraphNode { Id = "M.Do", Name = "Do", Kind = NodeKind.Method, Signature = "void Do(int x)" }
                }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("## Changed Signatures (1)", output);
        Assert.Contains("- M.Do", output);
        Assert.Contains("- Was: void Do()", output);
        Assert.Contains("+ Now: void Do(int x)", output);
    }

    [Fact]
    public void Format_AddedEdges_ShowsEdgeDetails()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("## New Edges (1)", output);
        Assert.Contains("- A → B (Calls)", output);
    }

    [Fact]
    public void Format_RemovedEdges_ShowsEdgeDetails()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            RemovedEdges = new List<GraphEdge>
            {
                new() { FromId = "X", ToId = "Y", Type = EdgeType.Inherits }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("## Removed Edges (1)", output);
        Assert.Contains("- X → Y (Inherits)", output);
    }

    [Fact]
    public void Format_EdgeWithResolution_ShowsResolutionSuffix()
    {
        // Targets L85: edge.Resolution is null check
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo,
                    Resolution = "IFoo → Foo (Scoped)" }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- A → B (ResolvesTo, IFoo → Foo (Scoped))", output);
    }

    [Fact]
    public void Format_EdgeWithNullResolution_NoSuffix()
    {
        // Targets L85: null check on Resolution
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Resolution = null }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- A → B (Calls)", output);
        Assert.DoesNotContain(", )", output);
    }

    [Fact]
    public void Format_ShortCommitHash_NotTruncated()
    {
        // Targets L95: commitHash.Length > 7 condition
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "xyz", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: abc..xyz", output);
    }

    [Fact]
    public void Format_EmptyCommitHash_ShowsUnknown()
    {
        // Targets L93 in ShortCommit: empty string check
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: unknown..unknown", output);
    }

    [Fact]
    public void Format_LongCommitHash_TruncatedTo7()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "1234567890abcdef", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "fedcba0987654321", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: 1234567..fedcba0", output);
        Assert.DoesNotContain("1234567890", output);
    }

    [Fact]
    public void Format_Exactly7CharCommitHash_NotTruncated()
    {
        // Length > 7 means exactly 7 is not truncated
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abcdefg", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "1234567", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: abcdefg..1234567", output);
    }

    [Fact]
    public void Format_NodeWhitespaceFilePath_TreatedAsNoFile()
    {
        // Targets L61: !string.IsNullOrWhiteSpace(node.FilePath) check
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method, FilePath = "   " }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("- A (Method)", output);
        Assert.DoesNotContain("—", output.Split("## Added Nodes")[1].Split("##")[0]);
    }

    [Fact]
    public void Format_NodePartialLineInfo_OnlyStartLine()
    {
        // Targets L63: StartLine > 0 && EndLine > 0 — only StartLine set
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method,
                    FilePath = "file.cs", StartLine = 5, EndLine = 0 }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        // When EndLine is 0, should show file only without line range
        Assert.Contains("- A (Method) — file.cs", output);
        Assert.DoesNotContain(":5-0", output);
    }

    [Fact]
    public void Format_OutputNotEmpty_TrimmedEnd()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void Format_FullDiff_AllSectionsPresent()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "New1", Name = "New1", Kind = NodeKind.Type,
                    FilePath = "f1.cs", StartLine = 1, EndLine = 10 },
                new() { Id = "New2", Name = "New2", Kind = NodeKind.Method }
            },
            RemovedNodes = new List<GraphNode>
            {
                new() { Id = "Old1", Name = "Old1", Kind = NodeKind.Property }
            },
            SignatureChangedNodes = new List<GraphSignatureChange>
            {
                new()
                {
                    Previous = new GraphNode { Id = "Changed", Name = "Changed", Kind = NodeKind.Method, Signature = "old" },
                    Current = new GraphNode { Id = "Changed", Name = "Changed", Kind = NodeKind.Method, Signature = "new" }
                }
            },
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "New1", ToId = "New2", Type = EdgeType.Calls },
                new() { FromId = "New1", ToId = "Old1", Type = EdgeType.DependsOn, Resolution = "via DI" }
            },
            RemovedEdges = new List<GraphEdge>
            {
                new() { FromId = "Old1", ToId = "New1", Type = EdgeType.Inherits }
            }
        };

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("## Added Nodes (2)", output);
        Assert.Contains("- New1 (Type) — f1.cs:1-10", output);
        Assert.Contains("- New2 (Method)", output);
        Assert.Contains("## Removed Nodes (1)", output);
        Assert.Contains("- Old1 (Property)", output);
        Assert.Contains("## Changed Signatures (1)", output);
        Assert.Contains("- Was: old", output);
        Assert.Contains("+ Now: new", output);
        Assert.Contains("## New Edges (2)", output);
        Assert.Contains("- New1 → New2 (Calls)", output);
        Assert.Contains("- New1 → Old1 (DependsOn, via DI)", output);
        Assert.Contains("## Removed Edges (1)", output);
        Assert.Contains("- Old1 → New1 (Inherits)", output);
    }
}
