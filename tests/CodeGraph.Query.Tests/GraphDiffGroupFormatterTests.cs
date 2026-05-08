using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class GraphDiffGroupFormatterTests
{
    private static GraphMetadata BaseMeta => new() { CommitHash = "abc1234567", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
    private static GraphMetadata HeadMeta => new() { CommitHash = "def5678901", Branch = "feature", GeneratedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void Format_EmptyDiff_ShowsZeroChanges()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("# PR Review: abc1234..def5678", output);
        Assert.Contains("**0 changes**", output);
    }

    [Fact]
    public void Format_TypeChanges_GroupedTogether()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.NewService", Name = "NewService", Kind = NodeKind.Type }
            },
            RemovedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.OldService", Name = "OldService", Kind = NodeKind.Type }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Type Changes (2)", output);
        Assert.Contains("+ MyApp.NewService (added)", output);
        Assert.Contains("- MyApp.OldService (removed)", output);
    }

    [Fact]
    public void Format_SignatureChanges_Grouped()
    {
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

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Signature Changes (1)", output);
        Assert.Contains("~ M.Do: void Do() → void Do(int x)", output);
    }

    [Fact]
    public void Format_CallGraphEdges_GroupedTogether()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
                new() { FromId = "C", ToId = "D", Type = EdgeType.References },
                new() { FromId = "E", ToId = "F", Type = EdgeType.Overrides }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Call Graph Changes (3)", output);
        Assert.Contains("+ A → B (Calls)", output);
        Assert.Contains("+ C → D (References)", output);
        Assert.Contains("+ E → F (Overrides)", output);
    }

    [Fact]
    public void Format_DiEdges_GroupedTogether()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo, Resolution = "IFoo → Foo" },
                new() { FromId = "C", ToId = "D", Type = EdgeType.DependsOn }
            },
            RemovedEdges = new List<GraphEdge>
            {
                new() { FromId = "E", ToId = "F", Type = EdgeType.ConfiguredBy }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## DI Changes (3)", output);
        Assert.Contains("+ A → B (ResolvesTo, IFoo → Foo)", output);
        Assert.Contains("+ C → D (DependsOn)", output);
        Assert.Contains("- E → F (ConfiguredBy)", output);
    }

    [Fact]
    public void Format_RouteEdges_GroupedTogether()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "Controller", ToId = "/api/v1", Type = EdgeType.HandlesRoute },
                new() { FromId = "App", ToId = "AuthMiddleware", Type = EdgeType.UsesMiddleware }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Route Changes (2)", output);
        Assert.Contains("+ Controller → /api/v1 (HandlesRoute)", output);
        Assert.Contains("+ App → AuthMiddleware (UsesMiddleware)", output);
    }

    [Fact]
    public void Format_NonTypeNodes_GoToOtherChanges()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.NewMethod", Name = "NewMethod", Kind = NodeKind.Method }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Other Changes (1)", output);
        Assert.Contains("+ MyApp.NewMethod (Method, added)", output);
    }

    [Fact]
    public void Format_MixedEdgeTypes_CategorizedCorrectly()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
                new() { FromId = "C", ToId = "D", Type = EdgeType.ResolvesTo },
                new() { FromId = "E", ToId = "F", Type = EdgeType.HandlesRoute },
                new() { FromId = "G", ToId = "H", Type = EdgeType.Inherits }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Call Graph Changes (1)", output);
        Assert.Contains("## DI Changes (1)", output);
        Assert.Contains("## Route Changes (1)", output);
        Assert.Contains("## Other Changes (1)", output);
    }

    [Fact]
    public void Format_EmptyCategories_NotRendered()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "MyApp.Svc", Name = "Svc", Kind = NodeKind.Type }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("## Type Changes", output);
        Assert.DoesNotContain("## Signature Changes", output);
        Assert.DoesNotContain("## Call Graph Changes", output);
        Assert.DoesNotContain("## DI Changes", output);
        Assert.DoesNotContain("## Route Changes", output);
        Assert.DoesNotContain("## Other Changes", output);
    }

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void Format_ShortCommitHashes_NotTruncated()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "xyz", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("# PR Review: abc..xyz", output);
    }

    [Fact]
    public void Format_EmptyCommitHashes_ShowUnknown()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("# PR Review: unknown..unknown", output);
    }

    [Fact]
    public void Format_NullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GraphDiffGroupFormatter.Format(null!));
    }

    [Fact]
    public void Categorize_ReturnsAllSixGroups()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta
        };

        var groups = GraphDiffGroupFormatter.Categorize(diff);

        Assert.Equal(6, groups.Count);
        Assert.Equal("Type Changes", groups[0].Title);
        Assert.Equal("Signature Changes", groups[1].Title);
        Assert.Equal("Call Graph Changes", groups[2].Title);
        Assert.Equal("DI Changes", groups[3].Title);
        Assert.Equal("Route Changes", groups[4].Title);
        Assert.Equal("Other Changes", groups[5].Title);
    }

    [Fact]
    public void Format_FullDiff_ShowsCorrectTotalCount()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            AddedNodes = new List<GraphNode>
            {
                new() { Id = "T1", Name = "T1", Kind = NodeKind.Type },
                new() { Id = "M1", Name = "M1", Kind = NodeKind.Method }
            },
            RemovedNodes = new List<GraphNode>
            {
                new() { Id = "T2", Name = "T2", Kind = NodeKind.Type }
            },
            SignatureChangedNodes = new List<GraphSignatureChange>
            {
                new()
                {
                    Previous = new GraphNode { Id = "S1", Name = "S1", Kind = NodeKind.Method, Signature = "a" },
                    Current = new GraphNode { Id = "S1", Name = "S1", Kind = NodeKind.Method, Signature = "b" }
                }
            },
            AddedEdges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            },
            RemovedEdges = new List<GraphEdge>
            {
                new() { FromId = "C", ToId = "D", Type = EdgeType.ResolvesTo }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        // 2 type + 1 other(method) + 1 sig + 1 call + 1 di = 6
        Assert.Contains("**6 changes**", output);
    }

    [Fact]
    public void Format_RemovedCallEdge_ShowsMinusPrefix()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = BaseMeta,
            HeadMetadata = HeadMeta,
            RemovedEdges = new List<GraphEdge>
            {
                new() { FromId = "X", ToId = "Y", Type = EdgeType.Calls }
            }
        };

        var output = GraphDiffGroupFormatter.Format(diff);

        Assert.Contains("- X → Y (Calls)", output);
    }
}
