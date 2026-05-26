using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class CompactFormatterContractTests
{
    private static readonly GraphMetadata Metadata = new()
    {
        SchemaVersion = 1,
        GeneratedAt = DateTime.UtcNow,
        Solution = "T.sln",
        SolutionName = "T",
        CommitHash = "abc1234",
        Branch = "main",
        ProjectsIndexed = Array.Empty<string>()
    };

    private static GraphNode Node(string id, string? docComment = null, string? filePath = null, int startLine = 0, int endLine = 0, NodeKind kind = NodeKind.Method) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        DocComment = docComment,
        FilePath = filePath ?? string.Empty,
        StartLine = startLine,
        EndLine = endLine,
        Accessibility = Accessibility.Public
    };

    [Fact]
    public void Format_WithTargetAndDifferentFirstMatch_UsesTargetHeaderAndTargetDocumentation()
    {
        var other = Node("Other.Match", docComment: "Other doc.");
        var target = Node("Target.Run", docComment: "Target doc.");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { other, target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [other.Id] = other,
                [target.Id] = target
            },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata,
            WasTruncated = false,
            TotalMatchCount = 2
        };

        var output = CompactFormatter.Format(result);
        var expectedPrefix = string.Join(Environment.NewLine, new[]
        {
            "# Target.Run",
            string.Empty,
            "## Target.Run [method] // Target doc."
        });

        Assert.StartsWith(expectedPrefix, output);
        Assert.DoesNotContain("## Target.Run [method] // Other doc.", output);
    }

    [Fact]
    public void Format_WithMultipleMatchedNodesAndNoTarget_ShowsDocumentationOnlyOnFirstMatchedNode()
    {
        var first = Node("Alpha.Match", docComment: "First doc.");
        var second = Node("Beta.Match", docComment: "Second doc.");

        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode> { first, second },
            Nodes = new Dictionary<string, GraphNode>
            {
                [first.Id] = first,
                [second.Id] = second
            },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata,
            WasTruncated = false,
            TotalMatchCount = 2
        };

        var output = CompactFormatter.Format(result);
        var lines = output.Split(Environment.NewLine);

        Assert.Contains("## Alpha.Match [method] // First doc.", output);
        Assert.Equal("## Beta.Match [method]", lines.Single(line => line == "## Beta.Match [method]"));
    }

    [Fact]
    public void Format_WithSuggestions_AppendsExactSuggestionBlock()
    {
        var target = Node("Target.Run");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata,
            WasTruncated = false,
            TotalMatchCount = 1,
            Suggestions = new List<string> { "First.Match", "Second.Match" }
        };

        var output = CompactFormatter.Format(result);
        var expectedSuffix = string.Join(Environment.NewLine, new[]
        {
            "Did you mean:",
            "  - First.Match",
            "  - Second.Match"
        });

        Assert.EndsWith(expectedSuffix, output);
    }

    [Fact]
    public void Format_WithMultipleRelatedNodes_WritesRelatedHeaderOnceAfterBlankLine()
    {
        var target = Node("Target.Run");
        var firstRelated = Node("Related.One");
        var secondRelated = Node("Related.Two");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [firstRelated.Id] = firstRelated,
                [secondRelated.Id] = secondRelated
            },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata,
            WasTruncated = false,
            TotalMatchCount = 1
        };

        var output = CompactFormatter.Format(result);

        Assert.Single(output.Split(Environment.NewLine).Where(line => line == "## Related"));
        Assert.Contains(string.Join(Environment.NewLine, new[]
        {
            string.Empty,
            "## Related",
            "- Related.One [method]"
        }), output);
    }

    [Fact]
    public void Format_WithMultipleIncomingGroups_OrdersGroupsAscendingByEdgeType()
    {
        var target = Node("Target.Run");
        var caller = Node("Caller.Run");
        var reference = Node("Reference.Run");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [caller.Id] = caller,
                [reference.Id] = reference
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = reference.Id, ToId = target.Id, Type = EdgeType.References },
                new() { FromId = caller.Id, ToId = target.Id, Type = EdgeType.Calls }
            },
            Metadata = Metadata,
            WasTruncated = false,
            TotalMatchCount = 1
        };

        var output = CompactFormatter.Format(result);

        Assert.True(output.IndexOf("← calls:", StringComparison.Ordinal) < output.IndexOf("← references:", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_WithSourceAtExactLimit_UsesExactFenceAndNoTruncationMessage()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"CompactFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, new[] { "line1", "line2", "line3" }));

        try
        {
            var target = Node("Target.Run", filePath: filePath, startLine: 1, endLine: 3);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata,
                WasTruncated = false,
                TotalMatchCount = 1
            };

            var output = CompactFormatter.Format(result, includeSource: true, sourceMaxLines: 3);
            var expectedFragment = string.Join(Environment.NewLine, new[]
            {
                $"  ```csharp  // {filePath}:1-3",
                "  line1",
                "  line2",
                "  line3",
                "  ```"
            });

            Assert.Contains(expectedFragment, output);
            Assert.DoesNotContain("truncated", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_WithRequestedSourceRange_ReadsOnlyRequestedLines()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"CompactFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, Enumerable.Range(1, 8).Select(i => $"line{i}")));

        try
        {
            var target = Node("Target.Run", filePath: filePath, startLine: 2, endLine: 4);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata,
                WasTruncated = false,
                TotalMatchCount = 1
            };

            var output = CompactFormatter.Format(result, includeSource: true, sourceMaxLines: 10);

            Assert.Contains("  line2", output);
            Assert.Contains("  line3", output);
            Assert.Contains("  line4", output);
            Assert.DoesNotContain("  line5", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_WithTruncatedSource_UsesExactReadRangeInWarning()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"CompactFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, Enumerable.Range(1, 5).Select(i => $"line{i}")));

        try
        {
            var target = Node("Target.Run", filePath: filePath, startLine: 2, endLine: 5);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata,
                WasTruncated = false,
                TotalMatchCount = 1
            };

            var output = CompactFormatter.Format(result, includeSource: true, sourceMaxLines: 2);

            Assert.Contains($"  // ... truncated (2 more lines) — read {filePath}:4-5 for full source", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_WithInvalidSourceRange_ShowsUnavailableWarning()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"CompactFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, new[] { "line1", "line2", "line3" }));

        try
        {
            var target = Node("Target.Run", filePath: filePath, startLine: 0, endLine: 2);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata,
                WasTruncated = false,
                TotalMatchCount = 1
            };

            var output = CompactFormatter.Format(result, includeSource: true, sourceMaxLines: 5);

            Assert.Contains("  ⚠ source unavailable (no file path or line range)", output);
            Assert.DoesNotContain("```csharp", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void ExtractSummary_WithExactlyOneHundredTwentyCharacters_DoesNotAddEllipsis()
    {
        var text = new string('a', 120);

        var summary = CompactFormatter.ExtractSummary(text);

        Assert.Equal(text, summary);
        Assert.DoesNotContain('…', summary);
    }
}
