using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class ContextFormatterContractTests
{
    private static readonly GraphMetadata Metadata = new()
    {
        CommitHash = "12345678",
        Branch = "main",
        GeneratedAt = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static GraphNode Node(string id, NodeKind kind = NodeKind.Method, string? filePath = null, int startLine = 0, int endLine = 0) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        FilePath = filePath ?? string.Empty,
        StartLine = startLine,
        EndLine = endLine,
        Accessibility = Accessibility.Public
    };

    [Theory]
    [InlineData(EdgeType.HandlesRoute, "Handled by", "Handles route")]
    [InlineData(EdgeType.BindsConfiguration, "Binds configuration", "Configuration bound by")]
    [InlineData(EdgeType.UsesMiddleware, "Uses middleware", "Middleware used by")]
    [InlineData(EdgeType.MapsToTable, "Maps to table", "Table mapped from")]
    [InlineData(EdgeType.NavigatesTo, "Navigates to", "Navigated from")]
    [InlineData(EdgeType.ConfiguredBy, "Configured by", "Configures")]
    public void Format_WithExtendedEdgeTypes_UsesExpectedSectionHeaders(EdgeType edgeType, string outgoingHeader, string incomingHeader)
    {
        var target = Node("Target.Node");
        var outgoing = Node("Outgoing.Node");
        var incoming = Node("Incoming.Node");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [outgoing.Id] = outgoing,
                [incoming.Id] = incoming
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = target.Id, ToId = outgoing.Id, Type = edgeType },
                new() { FromId = incoming.Id, ToId = target.Id, Type = edgeType }
            },
            Metadata = Metadata
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains($"### {outgoingHeader}", output);
        Assert.Contains($"### {incomingHeader}", output);
    }

    [Fact]
    public void Format_WithTargetAndQuery_UsesHeaderSpacingAndOmitsMatchedNodesSection()
    {
        var target = Node("Target.Node");
        var other = Node("Other.Node");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { other, target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [other.Id] = other
            },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata
        };

        var output = ContextFormatter.Format(result, "target --depth 1");
        var expectedPrefix = string.Join(Environment.NewLine, new[]
        {
            "# Subgraph for Target.Node",
            "## Commit: 1234567 (main, 2026-04-14)",
            "## Query: target --depth 1",
            string.Empty,
            "### Target"
        });

        Assert.StartsWith(expectedPrefix, output);
        Assert.DoesNotContain("### Matched Nodes", output);
        Assert.DoesNotContain("Did you mean:", output);
    }

    [Fact]
    public void Format_WithSuggestions_AppendsExactSuggestionBlock()
    {
        var match = Node("Match.Node");
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode> { match },
            Nodes = new Dictionary<string, GraphNode> { [match.Id] = match },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata,
            Suggestions = new List<string> { "First.Suggestion", "Second.Suggestion" }
        };

        var output = ContextFormatter.Format(result);
        var expectedSuffix = string.Join(Environment.NewLine, new[]
        {
            "Did you mean:",
            "  - First.Suggestion",
            "  - Second.Suggestion"
        });

        Assert.EndsWith(expectedSuffix, output);
    }

    [Fact]
    public void Format_WithSourceAtExactLimit_EmitsCompleteCodeFenceWithoutTruncation()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"ContextFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, new[] { "line1", "line2", "line3" }));

        try
        {
            var target = Node("Target.Node", filePath: filePath, startLine: 1, endLine: 3);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata
            };

            var output = ContextFormatter.Format(result, includeSource: true, sourceMaxLines: 3);
            var expectedFragment = string.Join(Environment.NewLine, new[]
            {
                "  ```csharp",
                "  line1",
                "  line2",
                "  line3",
                "  ```"
            });

            Assert.Contains(expectedFragment, output);
            Assert.DoesNotContain("// ...", output);
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
        var filePath = Path.Combine(AppContext.BaseDirectory, $"ContextFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, Enumerable.Range(1, 6).Select(i => $"line{i}")));

        try
        {
            var target = Node("Target.Node", filePath: filePath, startLine: 2, endLine: 3);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata
            };

            var output = ContextFormatter.Format(result, includeSource: true, sourceMaxLines: 10);

            Assert.Contains("  line2", output);
            Assert.Contains("  line3", output);
            Assert.DoesNotContain("  line4", output);
            Assert.DoesNotContain("  line5", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_WithInvalidStartLine_DoesNotEmitSourceSnippet()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"ContextFormatterContract_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, new[] { "line1", "line2", "line3" }));

        try
        {
            var target = Node("Target.Node", filePath: filePath, startLine: 0, endLine: 2);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = Metadata
            };

            var output = ContextFormatter.Format(result, includeSource: true, sourceMaxLines: 5);

            Assert.DoesNotContain("```csharp", output);
            Assert.DoesNotContain("  line1", output);
            Assert.DoesNotContain("  line2", output);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_WithOutgoingAndIncomingSections_KeepsBlankLineBetweenSections()
    {
        var target = Node("Target.Node");
        var dependency = Node("Dependency.Node");
        var caller = Node("Caller.Node");

        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [dependency.Id] = dependency,
                [caller.Id] = caller
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = target.Id, ToId = dependency.Id, Type = EdgeType.Calls },
                new() { FromId = caller.Id, ToId = target.Id, Type = EdgeType.Calls }
            },
            Metadata = Metadata
        };

        var output = ContextFormatter.Format(result);
        var expectedFragment = string.Join(Environment.NewLine, new[]
        {
            "### Calls (outgoing)",
            "- Dependency.Node",
            string.Empty,
            "### Called by (incoming)"
        });

        Assert.Contains(expectedFragment, output);
    }
}
