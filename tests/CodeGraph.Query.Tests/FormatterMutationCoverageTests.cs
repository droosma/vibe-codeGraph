using CodeGraph.Core;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public sealed class FormatterMutationCoverageTests : IDisposable
{
    private readonly string _sourceFilePath = Path.Combine(AppContext.BaseDirectory, $"formatter-source-{Guid.NewGuid():N}.cs");

    public void Dispose()
    {
        if (File.Exists(_sourceFilePath))
            File.Delete(_sourceFilePath);
    }

    [Fact]
    public void Format_TextFormatter_WithoutSuggestions_UsesExactLayout()
    {
        var target = MethodNode("Target.Run", "Target", "src/Target.cs", 10, 12, "void Run()");
        var dependency = MethodNode("Dependency.Run", "Dependency");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [dependency.Id] = dependency
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = target.Id, ToId = dependency.Id, Type = EdgeType.Calls }
            }
        };

        var output = TextFormatter.Format(result);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "Target: Target.Run (Method)",
            "  File: src/Target.cs:10-12",
            "  Sig:  void Run()",
            string.Empty,
            "Subgraph: 2 nodes, 1 edges",
            string.Empty,
            "Calls (1):",
            "  Target -> Dependency"
        });

        Assert.Equal(expected, output);
        Assert.DoesNotContain("Did you mean:", output);
    }

    [Fact]
    public void Format_TextFormatter_WithSuggestions_AppendsSuggestionBlock()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Suggestions = new List<string> { "Alpha.Run", "Beta.Run" }
        };

        var output = TextFormatter.Format(result);

        Assert.EndsWith(string.Join(Environment.NewLine, new[]
        {
            "Did you mean:",
            "  - Alpha.Run",
            "  - Beta.Run"
        }), output);
    }

    [Fact]
    public void Format_CompactFormatter_UsesTargetHeaderAndRelatedSpacingExactly()
    {
        var target = MethodNode("Target.Run", "Run");
        var related = MethodNode("Other.Helper", "Helper");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>
            {
                [target.Id] = target,
                [related.Id] = related
            },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata(),
            TotalMatchCount = 1
        };

        var output = CompactFormatter.Format(result);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Target.Run",
            string.Empty,
            "## Target.Run [method]",
            string.Empty,
            "## Related",
            "- Other.Helper [method]"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_CompactFormatter_WithMissingEndLine_ShowsUnavailableWarning()
    {
        File.WriteAllText(_sourceFilePath, "line1" + Environment.NewLine + "line2");
        var target = MethodNode("Target.Run", "Run", _sourceFilePath, 1, 0);
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = Metadata(),
            TotalMatchCount = 1
        };

        var output = CompactFormatter.Format(result, includeSource: true);

        Assert.Contains("  ⚠ source unavailable (no file path or line range)", output);
    }

    [Fact]
    public void Format_ContextFormatter_ExactlySevenCharacterCommitIsNotTruncated()
    {
        var target = MethodNode("Target.Run", "Run");
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode> { [target.Id] = target },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata
            {
                CommitHash = "abcdefg",
                Branch = "main",
                GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var output = ContextFormatter.Format(result);

        Assert.Contains("## Commit: abcdefg (main, 2025-01-01)", output);
    }

    [Fact]
    public void Format_ExplainFormatter_UsesExactSectionLayout()
    {
        var node = new GraphNode
        {
            Id = "MyApp.Service.Run",
            Name = "Run",
            Kind = NodeKind.Method,
            FilePath = "src/Service.cs",
            StartLine = 5,
            EndLine = 8,
            Signature = "void Run()",
            DocComment = "Runs the service.",
            Accessibility = Accessibility.Public
        };
        var members = new List<GraphNode>
        {
            new() { Id = "MyApp.Service.Helper", Name = "Helper", Kind = NodeKind.Method }
        };
        var outgoing = new List<GraphEdge>
        {
            new() { FromId = node.Id, ToId = "MyApp.Dependency.Call", Type = EdgeType.Calls }
        };
        var incoming = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Controller.Post", ToId = node.Id, Type = EdgeType.Calls }
        };
        var tests = new List<GraphNode>
        {
            new() { Id = "Tests.ServiceTests.Run_executes", Name = "Run_executes", Kind = NodeKind.Method }
        };

        var output = ExplainFormatter.Format(new ExplainResult(node, outgoing, incoming, members, tests));

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# MyApp.Service.Run",
            "Kind: Method",
            "File: src/Service.cs:5-8",
            "Signature: void Run()",
            "Doc: Runs the service.",
            string.Empty,
            "## Members (1)",
            "- [Method] Helper",
            string.Empty,
            "## Calls (outgoing, 1)",
            "- → MyApp.Dependency.Call",
            string.Empty,
            "## Calls (incoming, 1)",
            "- ← MyApp.Controller.Post",
            string.Empty,
            "## Test coverage (1)",
            "- Tests.ServiceTests.Run_executes"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_PackageFormatter_DependentsWithSameKindAreOrderedByConsumerId()
    {
        var dependents = new List<PackageDependent>
        {
            new("Pkg.Core", "1.0.0", "App", "App.Zeta", "Zeta", NodeKind.Method, Array.Empty<string>()),
            new("Pkg.Core", "1.0.0", "App", "App.Alpha", "Alpha", NodeKind.Method, Array.Empty<string>()),
            new("Pkg.Core", "1.0.0", "App", "App.Type", "Type", NodeKind.Type, Array.Empty<string>())
        };

        var output = PackageFormatter.FormatDependents(dependents);
        var appSection = output[output.IndexOf("## App", StringComparison.Ordinal)..];

        Assert.True(appSection.IndexOf("- Method: App.Alpha", StringComparison.Ordinal) < appSection.IndexOf("- Method: App.Zeta", StringComparison.Ordinal));
        Assert.True(appSection.IndexOf("- Type: App.Type", StringComparison.Ordinal) < appSection.IndexOf("- Method: App.Alpha", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_ImpactFormatter_UsesExactLayout()
    {
        var target = MethodNode("Root.Run", "Run");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode>
            {
                new() { Id = "Dep.Type", Name = "Type", Kind = NodeKind.Type, FilePath = "src/Dep.cs", StartLine = 12 },
                new() { Id = "Dep.Call", Name = "Call", Kind = NodeKind.Method }
            }, new List<GraphEdge>())
        };

        var output = ImpactFormatter.Format(new ImpactResult("Root.Run", target, layers));

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Impact analysis: Root.Run",
            "Total affected: 2 nodes across 1 layer(s)",
            string.Empty,
            "## Layer 1 (2 nodes)",
            "- [Type] Dep.Type  (src/Dep.cs:12)",
            "- [Method] Dep.Call"
        });

        Assert.Equal(expected, output);
    }

    private static GraphNode MethodNode(
        string id,
        string name,
        string? filePath = null,
        int startLine = 0,
        int endLine = 0,
        string? signature = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Method,
            FilePath = filePath ?? string.Empty,
            StartLine = startLine,
            EndLine = endLine,
            Signature = signature ?? string.Empty,
            Accessibility = Accessibility.Public
        };
    }

    private static GraphMetadata Metadata()
    {
        return new GraphMetadata
        {
            SchemaVersion = 1,
            GeneratedAt = DateTime.UtcNow,
            Solution = "Test.sln",
            SolutionName = "Test",
            CommitHash = "abcdefg",
            Branch = "main",
            ProjectsIndexed = Array.Empty<string>()
        };
    }
}
