using System.Text.Json;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class GraphDiffMutationCoverageTests
{
    [Fact]
    public void Format_ContextFormatter_EmptyDiff_UsesExactLayout()
    {
        var result = Diff("abcdefg", "1234567");

        var output = GraphDiffContextFormatter.Format(result);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Graph Diff: abcdefg..1234567",
            string.Empty,
            "## Added Nodes (0)",
            "- None",
            string.Empty,
            "## Removed Nodes (0)",
            "- None",
            string.Empty,
            "## Changed Signatures (0)",
            "- None",
            string.Empty,
            "## New Edges (0)",
            "- None",
            string.Empty,
            "## Removed Edges (0)",
            "- None"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_ContextFormatter_WithZeroStartLine_ShowsFileWithoutRange()
    {
        var result = Diff(
            "basehash",
            "headhash",
            addedNodes:
            [
                new GraphNode
                {
                    Id = "MyApp.Service",
                    Name = "Service",
                    Kind = NodeKind.Type,
                    FilePath = "src/Service.cs",
                    StartLine = 0,
                    EndLine = 5
                }
            ]);

        var output = GraphDiffContextFormatter.Format(result);

        Assert.Contains("- MyApp.Service (Type) — src/Service.cs", output);
        Assert.DoesNotContain(":0-5", output);
    }

    [Fact]
    public void Format_GroupFormatter_EmptyDiff_ReportsZeroCategoriesExactly()
    {
        var output = GraphDiffGroupFormatter.Format(Diff("abcdefg", "1234567"));

        Assert.Contains("**0 changes** across 0 categories", output);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void Format_GroupFormatter_UsesExactLayoutForTypeAndOtherChanges()
    {
        var result = Diff(
            "abcdefg",
            "1234567",
            addedNodes:
            [
                new GraphNode { Id = "MyApp.Type", Name = "Type", Kind = NodeKind.Type }
            ],
            addedEdges:
            [
                new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Inherits }
            ]);

        var output = GraphDiffGroupFormatter.Format(result);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# PR Review: abcdefg..1234567",
            string.Empty,
            "**2 changes** across 2 categories",
            string.Empty,
            "## Type Changes (1)",
            "- + MyApp.Type (added)",
            string.Empty,
            "## Other Changes (1)",
            "- + A → B (Inherits)"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_JsonFormatter_UsesIndentedCamelCaseOutput()
    {
        var output = GraphDiffJsonFormatter.Format(Diff(
            "abcdefg",
            "1234567",
            addedNodes:
            [
                new GraphNode { Id = "MyApp.Type", Name = "Type", Kind = NodeKind.Type }
            ]));

        Assert.Contains($"{Environment.NewLine}  \"baseMetadata\": {{", output);
        Assert.Contains("\"addedNodes\"", output);
        Assert.DoesNotContain("\"BaseMetadata\"", output);
    }

    [Fact]
    public void Format_JsonFormatter_NullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GraphDiffJsonFormatter.Format(null!));
    }

    [Fact]
    public void Format_TextFormatter_ExactlySevenCharacterHashesAreNotTruncated()
    {
        var output = GraphDiffTextFormatter.Format(Diff("abcdefg", "1234567"));

        Assert.StartsWith("Graph Diff abcdefg..1234567", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_NullAndEmptyResolution_ProduceSameEdgeKey()
    {
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var headNodes = new Dictionary<string, GraphNode>(baseNodes, StringComparer.Ordinal);
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo, Resolution = null }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo, Resolution = string.Empty }
        };

        var diff = GraphDiffEngine.Compare(Metadata("base"), baseNodes, baseEdges, Metadata("head"), headNodes, headEdges);

        Assert.Empty(diff.AddedEdges);
        Assert.Empty(diff.RemovedEdges);
    }

    private static GraphDiffResult Diff(
        string baseCommit,
        string headCommit,
        List<GraphNode>? addedNodes = null,
        List<GraphNode>? removedNodes = null,
        List<GraphSignatureChange>? signatureChanges = null,
        List<GraphEdge>? addedEdges = null,
        List<GraphEdge>? removedEdges = null)
    {
        return new GraphDiffResult
        {
            BaseMetadata = Metadata(baseCommit),
            HeadMetadata = Metadata(headCommit),
            AddedNodes = addedNodes ?? new List<GraphNode>(),
            RemovedNodes = removedNodes ?? new List<GraphNode>(),
            SignatureChangedNodes = signatureChanges ?? new List<GraphSignatureChange>(),
            AddedEdges = addedEdges ?? new List<GraphEdge>(),
            RemovedEdges = removedEdges ?? new List<GraphEdge>()
        };
    }

    private static GraphMetadata Metadata(string commitHash)
    {
        return new GraphMetadata
        {
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }
}
