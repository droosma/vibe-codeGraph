using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public sealed class RemainingOutputFormatterMutationTests
{
    [Fact]
    public void Format_TargetNodeOverridesFirstMatchAndSuggestionsPresent_UsesTargetHeaderAndExtraBlankLine()
    {
        var target = Node("Target.Run");
        var firstMatch = Node("Other.Run");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { firstMatch, target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = QueryMetadata("abc12345"),
            TotalMatchCount = 2,
            Suggestions = new List<string> { "Alternate.Run" }
        };

        var output = CompactFormatter.Format(result);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Target.Run",
            string.Empty,
            "## Target.Run [method]",
            string.Empty,
            string.Empty,
            "Did you mean:",
            "  - Alternate.Run"
        });

        Assert.Equal(expected, output);
        Assert.DoesNotContain("# Other.Run", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MyApp.Services.OrderService", "MyApp.Storage.OrderStore", "MyApp.")]
    [InlineData("Contoso.Platform.Api.Controller", "Contoso.Platformer.Api.Handler", "Contoso.")]
    public void DetectCommonPrefix_MismatchInsideNamespaceSegment_ReturnsLastCompleteNamespace(string firstId, string secondId, string expectedPrefix)
    {
        var prefix = CompactFormatter.DetectCommonPrefix(new[] { firstId, secondId });

        Assert.Equal(expectedPrefix, prefix);
    }

    [Fact]
    public void Format_EightCharacterCommitHashInvalidEndLineAndSuggestionsPresent_UsesTrimmedCommitAndSkipsSourceSnippet()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"ContextFormatter_{Guid.NewGuid():N}.cs");
        File.WriteAllText(filePath, string.Join(Environment.NewLine, new[] { "line1", "line2" }));

        try
        {
            var target = Node("Target.Node", filePath: filePath, startLine: 1, endLine: 0);
            var result = new QueryResult
            {
                TargetNode = target,
                MatchedNodes = new List<GraphNode> { target },
                Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
                Edges = new List<GraphEdge>(),
                Metadata = QueryMetadata("12345678"),
                Suggestions = new List<string> { "Alternate.Node" }
            };

            var output = ContextFormatter.Format(result, includeSource: true);
            var expected = string.Join(Environment.NewLine, new[]
            {
                "# Subgraph for Target.Node",
                "## Commit: 1234567 (main, 2025-01-01)",
                string.Empty,
                "### Target",
                "- Target.Node",
                $"  File: {filePath}:1-0",
                string.Empty,
                string.Empty,
                "Did you mean:",
                "  - Alternate.Node"
            });

            Assert.Equal(expected, output);
            Assert.DoesNotContain("```csharp", output, StringComparison.Ordinal);
            Assert.DoesNotContain("12345678", output, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void Format_EmptyDiffWithEightCharacterHashes_UsesExactContextLayout()
    {
        var output = GraphDiffContextFormatter.Format(new GraphDiffResult
        {
            BaseMetadata = QueryMetadata("12345678"),
            HeadMetadata = QueryMetadata("abcdefgh")
        });

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Graph Diff: 1234567..abcdefg",
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
    public void Format_EmptyDiffWithEightCharacterHashes_UsesExactGroupedLayout()
    {
        var output = GraphDiffGroupFormatter.Format(new GraphDiffResult
        {
            BaseMetadata = QueryMetadata("12345678"),
            HeadMetadata = QueryMetadata("abcdefgh")
        });

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# PR Review: 1234567..abcdefg",
            string.Empty,
            "**0 changes** across 0 categories"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_EmptyDiffWithEightCharacterHashes_UsesExactTextLayout()
    {
        var output = GraphDiffTextFormatter.Format(new GraphDiffResult
        {
            BaseMetadata = QueryMetadata("12345678"),
            HeadMetadata = QueryMetadata("abcdefgh")
        });

        var expected = string.Join(Environment.NewLine, new[]
        {
            "Graph Diff 1234567..abcdefg",
            "Added nodes: 0",
            "Removed nodes: 0",
            "Signature changes: 0",
            "Added edges: 0",
            "Removed edges: 0"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_TestsOnlyResult_UsesExactExplainLayout()
    {
        var node = Node("Service.Run");
        var tests = new List<GraphNode>
        {
            Node("Tests.ServiceTests.Run_one"),
            Node("Tests.ServiceTests.Run_two")
        };

        var output = ExplainFormatter.Format(new ExplainResult(node, new List<GraphEdge>(), new List<GraphEdge>(), new List<GraphNode>(), tests));
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Service.Run",
            "Kind: Method",
            string.Empty,
            "## Test coverage (2)",
            "- Tests.ServiceTests.Run_one",
            "- Tests.ServiceTests.Run_two"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Apply_NewlineAtBudgetBoundary_KeepsContentUpToBoundary()
    {
        const string input = "1234\n678\nabcdef";

        var output = BudgetTruncator.Apply(input, 2);
        var warningIndex = output.IndexOf("\n\n⚠", StringComparison.Ordinal);

        Assert.True(warningIndex > 0);
        Assert.Equal("1234\n678", output[..warningIndex]);
    }

    [Fact]
    public void Format_MultipleLayersPresent_UsesExactImpactLayout()
    {
        var target = Node("Root.Run");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { Node("Dep.Type", NodeKind.Type, "src\\Dep.cs", 12) }, new List<GraphEdge>()),
            new(2, new List<GraphNode> { Node("Dep.Call") }, new List<GraphEdge>())
        };

        var output = ImpactFormatter.Format(new ImpactResult("Root.Run", target, layers));
        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Impact analysis: Root.Run",
            "Total affected: 2 nodes across 2 layer(s)",
            string.Empty,
            "## Layer 1 (1 nodes)",
            "- [Type] Dep.Type  (src\\Dep.cs:12)",
            string.Empty,
            "## Layer 2 (1 nodes)",
            "- [Method] Dep.Call"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_NoDirectTestsAndOneTransitiveTest_UsesExactContextLayout()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", containingTypeId: "MyApp.OrderService");
        var indirectTest = MethodNode("Tests.FlowTests.FullFlow", "FullFlow", filePath: "tests\\FlowTests.cs", containingTypeId: "Tests.FlowTests");
        var workflow = MethodNode("MyApp.Workflow.Execute", "Execute", containingTypeId: "MyApp.Workflow");
        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>(),
            new List<TestCoverage>
            {
                new(indirectTest, new List<GraphNode> { workflow, target })
            },
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "Affected tests for MyApp.OrderService.PlaceOrder:",
            string.Empty,
            "Direct coverage (tests that call this method):",
            "  (none)",
            string.Empty,
            "Transitive (tests reaching through call chain):",
            "  FlowTests.FullFlow [tests\\FlowTests.cs] via Workflow.Execute → OrderService.PlaceOrder"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_MatchedNodesEdgesAndSuggestionsPresent_UsesExactTextLayout()
    {
        var alpha = Node("Alpha.Id", name: "Alpha");
        var beta = Node("Beta.Id", NodeKind.Type, name: "Beta");
        var gamma = Node("Gamma.Id", name: "Gamma");
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode> { alpha, beta },
            Nodes = new Dictionary<string, GraphNode>
            {
                [alpha.Id] = alpha,
                [beta.Id] = beta,
                [gamma.Id] = gamma
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = alpha.Id, ToId = beta.Id, Type = EdgeType.Calls },
                new() { FromId = beta.Id, ToId = gamma.Id, Type = EdgeType.References }
            },
            Suggestions = new List<string> { "Alternate.Match" }
        };

        var output = TextFormatter.Format(result);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "Matched Nodes (2):",
            "  - Alpha.Id (Method)",
            "  - Beta.Id (Type)",
            string.Empty,
            "Subgraph: 3 nodes, 2 edges",
            string.Empty,
            "Calls (1):",
            "  Alpha -> Beta",
            string.Empty,
            "References (1):",
            "  Beta -> Gamma",
            string.Empty,
            string.Empty,
            "Did you mean:",
            "  - Alternate.Match"
        });

        Assert.Equal(expected, output);
    }

    private static GraphMetadata QueryMetadata(string commitHash)
    {
        return new GraphMetadata
        {
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ProjectsIndexed = Array.Empty<string>(),
            Solution = "CodeGraph.sln",
            SolutionName = "CodeGraph",
            SchemaVersion = 1
        };
    }

    private static GraphNode Node(
        string id,
        NodeKind kind = NodeKind.Method,
        string? filePath = null,
        int startLine = 0,
        int endLine = 0,
        string? name = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name ?? (id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id),
            Kind = kind,
            FilePath = filePath ?? string.Empty,
            StartLine = startLine,
            EndLine = endLine,
            Accessibility = Accessibility.Public
        };
    }

    private static GraphNode MethodNode(string id, string name, string? filePath = null, string? containingTypeId = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Method,
            FilePath = filePath ?? string.Empty,
            ContainingTypeId = containingTypeId,
            Accessibility = Accessibility.Public
        };
    }
}
