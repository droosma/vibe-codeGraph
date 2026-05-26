using CodeGraph.Core.Models;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests.Wiki;

public sealed class WikiMutationCoverageTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(AppContext.BaseDirectory, $"wiki-mutation-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    [Fact]
    public void Generate_IndexPage_UsesExactMarkdownLayout()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Type"] = Node("A.Type", "Type", NodeKind.Type, "Alpha"),
            ["A.Method"] = Node("A.Method", "Method", NodeKind.Method, "Alpha"),
            ["B.Type"] = Node("B.Type", "Type", NodeKind.Type, "Beta")
        };

        var metadata = new GraphMetadata
        {
            SolutionName = "TestSolution",
            CommitHash = "abc123",
            GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var markdown = IndexPageGenerator.Generate(nodes, new List<GraphEdge>(), metadata);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# TestSolution — Code Wiki",
            string.Empty,
            "Generated from CodeGraph | 2025-01-01 | abc123",
            string.Empty,
            "## Navigation",
            string.Empty,
            "- [Interfaces](INTERFACES.md)",
            "- [DI Wiring](DI-WIRING.md)",
            string.Empty,
            "## Assemblies",
            string.Empty,
            "| Assembly | Types | Methods | Total |",
            "|----------|------:|--------:|------:|",
            "| [Alpha](assemblies/Alpha.md) | 1 | 1 | 2 |",
            "| [Beta](assemblies/Beta.md) | 1 | 0 | 1 |"
        });

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void Generate_AssemblyPage_UsesExactMarkdownLayout()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Asm.TypeA"] = Node("Asm.TypeA", "TypeA", NodeKind.Type, "Asm", "Ns.Alpha", "src/TypeA.cs"),
            ["Asm.Run"] = Node("Asm.Run", "Run", NodeKind.Method, "Asm", "Ns.Alpha"),
            ["Asm.TypeB"] = Node("Asm.TypeB", "TypeB", NodeKind.Type, "Asm", "Ns.Beta")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Asm.TypeA", ToId = "Asm.TypeB", Type = EdgeType.Calls },
            new() { FromId = "External.One", ToId = "Asm.TypeA", Type = EdgeType.Calls },
            new() { FromId = "External.Two", ToId = "Asm.TypeA", Type = EdgeType.Calls },
            new() { FromId = "Asm.TypeB", ToId = "External.Three", Type = EdgeType.DependsOn }
        };

        var markdown = AssemblyPageGenerator.Generate("Asm", nodes, edges);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# Asm",
            string.Empty,
            "[← Back to Index](../INDEX.md)",
            string.Empty,
            "## Namespaces",
            string.Empty,
            "| Namespace | Types | Methods |",
            "|-----------|------:|--------:|",
            "| Ns.Alpha | 1 | 1 |",
            "| Ns.Beta | 1 | 0 |",
            string.Empty,
            "## Types",
            string.Empty,
            "| Type | In | Out | File |",
            "|------|---:|----:|------|",
            "| TypeA | 2 | 1 | src/TypeA.cs |",
            "| TypeB | 1 | 1 |  |",
            string.Empty,
            "## Cross-Assembly References",
            string.Empty,
            "This assembly has 3 cross-boundary connections."
        });

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void Generate_DiWiringPage_OrdersByDisplayNameAndFallbackId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["2.Service"] = Node("2.Service", "AlphaService", NodeKind.Type, "Asm"),
            ["1.Service"] = Node("1.Service", "ZuluService", NodeKind.Type, "Asm"),
            ["Impl.Alpha"] = Node("Impl.Alpha", "ImplA", NodeKind.Type, "Asm"),
            ["Impl.Zulu"] = Node("Impl.Zulu", "ImplZ", NodeKind.Type, "Asm"),
            ["Impl.Missing"] = Node("Impl.Missing", "ImplMissing", NodeKind.Type, "Asm")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "1.Service", ToId = "Impl.Zulu", Type = EdgeType.ResolvesTo, Resolution = "Transient" },
            new() { FromId = "MiddleMissing", ToId = "Impl.Missing", Type = EdgeType.ResolvesTo, Resolution = "Scoped" },
            new() { FromId = "2.Service", ToId = "Impl.Alpha", Type = EdgeType.ResolvesTo, Resolution = "Singleton" }
        };

        var markdown = DiWiringPageGenerator.Generate(nodes, edges);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "# DI Wiring",
            string.Empty,
            "[← Back to Index](INDEX.md)",
            string.Empty,
            "| Interface | Implementation | Resolution |",
            "|-----------|----------------|------------|",
            "| AlphaService | ImplA | Singleton |",
            "| MiddleMissing | ImplMissing | Scoped |",
            "| ZuluService | ImplZ | Transient |"
        });

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void Generate_WikiIncludesCrossAssemblyEdgesOnAssemblyPages()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Asm.Type"] = Node("Asm.Type", "Type", NodeKind.Type, "Asm", "Ns.Asm"),
            ["Other.Type"] = Node("Other.Type", "OtherType", NodeKind.Type, "Other", "Ns.Other")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "External.Caller", ToId = "Asm.Type", Type = EdgeType.Calls },
            new() { FromId = "Asm.Type", ToId = "Other.Type", Type = EdgeType.DependsOn }
        };
        var metadata = new GraphMetadata
        {
            SolutionName = "WikiSolution",
            CommitHash = "abcdef",
            GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        WikiGenerator.Generate(_outputDir, nodes, edges, metadata);

        var assemblyPage = File.ReadAllText(Path.Combine(_outputDir, "assemblies", "Asm.md"));

        Assert.Contains("## Cross-Assembly References", assemblyPage);
        Assert.Contains("This assembly has 2 cross-boundary connections.", assemblyPage);
    }

    [Theory]
    [InlineData("My:Assembly", "My_Assembly")]
    [InlineData("My|Assembly?", "My_Assembly_")]
    [InlineData("My*Assembly\"Name", "My_Assembly_Name")]
    public void SanitizeFileName_ReplacesMarkdownAndUrlProblemCharacters(string input, string expected)
    {
        Assert.Equal(expected, WikiGenerator.SanitizeFileName(input));
    }

    private static GraphNode Node(
        string id,
        string name,
        NodeKind kind,
        string assembly,
        string? ns = null,
        string? filePath = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = kind,
            AssemblyName = assembly,
            ContainingNamespaceId = ns,
            FilePath = filePath ?? string.Empty
        };
    }
}
