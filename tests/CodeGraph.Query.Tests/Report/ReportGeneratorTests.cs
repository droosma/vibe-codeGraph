using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class ReportGeneratorTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string name, NodeKind kind, string assembly)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, name, kind, assembly) in defs)
            nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = assembly };
        return nodes;
    }

    private static GraphMetadata CreateMetadata() => new()
    {
        SolutionName = "TestSolution",
        CommitHash = "abc123",
        GeneratedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
    };

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    [Fact]
    public void Generate_ContainsExpectedSections()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();
        var metadata = CreateMetadata();

        var report = ReportGenerator.Generate(nodes, edges, metadata);

        Assert.Contains("# CodeGraph Report", report);
        Assert.Contains("## Hub Types (highest connectivity)", report);
        Assert.Contains("## Assemblies", report);
        Assert.Contains("## Suggested Queries", report);
    }

    [Fact]
    public void Generate_IncludesHubTypes()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("TypeA", report);
        Assert.Contains("TypeB", report);
    }

    [Fact]
    public void Generate_IncludesAssemblyTable()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "MyAssembly"),
            ("B", "TypeB", NodeKind.Type, "MyAssembly"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("MyAssembly", report);
        Assert.Contains("| Assembly |", report);
    }

    [Fact]
    public void Generate_IncludesMetadata()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var metadata = CreateMetadata();

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), metadata);

        Assert.Contains("**Solution:** TestSolution", report);
        Assert.Contains("**Commit:** abc123", report);
    }

    [Fact]
    public void Generate_IncludesCoverageWhenPresent()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("T", "TestA", NodeKind.Type, "Tests"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("## Test Coverage (by assembly)", report);
    }

    [Fact]
    public void Generate_OmitsCoverageWhenNonePresent()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), CreateMetadata());

        Assert.DoesNotContain("## Test Coverage", report);
    }

    [Fact]
    public void Generate_MetadataFormat_ContainsSolutionPrefix()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var metadata = CreateMetadata();

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), metadata);

        Assert.Contains("**Solution:** TestSolution", report);
        Assert.Contains("**Generated:** 2025-01-01 12:00", report);
        Assert.Contains("**Commit:** abc123", report);
    }

    [Fact]
    public void Generate_NodeAndEdgeCounts_Displayed()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("**Nodes:** 2", report);
        Assert.Contains("**Edges:** 1", report);
    }

    [Fact]
    public void Generate_HubTable_ExactHeaders()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("| Type | In | Out | Total |", report);
        Assert.Contains("|------|---:|----:|------:|", report);
    }

    [Fact]
    public void Generate_HubTable_ShowsDegrees()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.DependsOn }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        // TypeA has 0 in, 2 out, 2 total
        Assert.Contains("| TypeA | 0 | 2 | 2 |", report);
        // TypeB has 2 in, 0 out, 2 total
        Assert.Contains("| TypeB | 2 | 0 | 2 |", report);
    }

    [Fact]
    public void Generate_AssemblyTable_ExactHeaders()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("| Assembly | Nodes | Internal Edges | Cross-boundary Edges |", report);
        Assert.Contains("|----------|------:|---------------:|--------------------:|", report);
    }

    [Fact]
    public void Generate_AssemblyTable_ShowsClusterData()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"),
            ("C", "TypeC", NodeKind.Type, "Asm2"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.DependsOn }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("| Asm1 | 2 | 1 | 1 |", report);
        Assert.Contains("| Asm2 | 1 | 0 | 1 |", report);
    }

    [Fact]
    public void Generate_CoverageTable_ExactHeaders()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("T", "TestA", NodeKind.Type, "Tests"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("| Assembly | Types | Covered | Coverage |", report);
        Assert.Contains("|----------|------:|--------:|---------:|", report);
    }

    [Fact]
    public void Generate_CoverageTable_ShowsPercentage()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"),
            ("T", "TestA", NodeKind.Type, "Tests"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        // Asm1: 2 types, 1 covered -> 50.0% (locale-dependent decimal separator)
        var expected = $"{50.0:F1}%";
        Assert.Contains(expected, report);
    }

    [Fact]
    public void Generate_SuggestedQueries_InCodeBlocks()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("```", report);
        Assert.Contains("codegraph query", report);
    }

    [Fact]
    public void Generate_SuggestedQueries_ContainHubNames()
    {
        var nodes = CreateNodes(
            ("A", "HubType", NodeKind.Type, "Asm1"),
            ("B", "Leaf", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("codegraph query HubType --depth 1 --mode focused", report);
        Assert.Contains("codegraph query HubType --depth 2 --kind calls", report);
    }

    [Fact]
    public void Generate_ReportTitle_Exact()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.StartsWith("# CodeGraph Report", report);
    }

    [Fact]
    public void Generate_ReportWithCoverageAndDomains_MatchesExactOutput()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["order-service"] = new() { Id = "order-service", Name = "OrderService", Kind = NodeKind.Type, AssemblyName = "Company.Modules.Finance" },
            ["planning-service"] = new() { Id = "planning-service", Name = "PlanningService", Kind = NodeKind.Type, AssemblyName = "Company.Modules.Planning" },
            ["smoke-test"] = new() { Id = "smoke-test", Name = "SmokeTests.CoversOrderService", Kind = NodeKind.Method, AssemblyName = "Tests" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "order-service", ToId = "planning-service", Type = EdgeType.Calls },
            new() { FromId = "order-service", ToId = "smoke-test", Type = EdgeType.CoveredBy }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        var expected = string.Join("\n", new[]
        {
            "# CodeGraph Report",
            string.Empty,
            "**Solution:** TestSolution",
            "**Generated:** 2025-01-01 12:00",
            "**Commit:** abc123",
            "**Nodes:** 3 | **Edges:** 2",
            string.Empty,
            "## Hub Types (highest connectivity)",
            string.Empty,
            "| Type | In | Out | Total |",
            "|------|---:|----:|------:|",
            "| OrderService | 0 | 2 | 2 |",
            "| PlanningService | 1 | 0 | 1 |",
            string.Empty,
            "## Assemblies",
            string.Empty,
            "| Assembly | Nodes | Internal Edges | Cross-boundary Edges |",
            "|----------|------:|---------------:|--------------------:|",
            "| Company.Modules.Finance | 1 | 0 | 2 |",
            "| Company.Modules.Planning | 1 | 0 | 1 |",
            "| Tests | 1 | 0 | 1 |",
            string.Empty,
            "## Domain Clusters",
            string.Empty,
            "| Domain | Assemblies | Key Types | Cross-domain connections |",
            "|--------|-----------|-----------|------------------------|",
            "| Finance | Modules.Finance | OrderService | → Planning (1), → Tests (1) |",
            "| Planning | Modules.Planning | PlanningService | → Finance (1) |",
            string.Empty,
            "## Test Coverage (by assembly)",
            string.Empty,
            "| Assembly | Types | Covered | Coverage |",
            "|----------|------:|--------:|---------:|",
            $"| Company.Modules.Planning | 1 | 0 | {0.0:F1}% |",
            $"| Company.Modules.Finance | 1 | 1 | {100.0:F1}% |",
            string.Empty,
            "## Suggested Queries",
            string.Empty,
            "```",
            "codegraph query OrderService --depth 1 --mode focused",
            "```",
            "```",
            "codegraph query OrderService --depth 2 --kind calls",
            "```",
            "```",
            "codegraph query PlanningService --depth 1 --mode focused",
            "```",
            "```",
            "codegraph query PlanningService --depth 2 --kind calls",
            "```",
            "```",
            "codegraph list types --assembly Company.Modules.Finance",
            "```",
            "```",
            "codegraph list types --assembly Company.Modules.Planning",
            "```"
        });

        Assert.Equal(expected, NormalizeLineEndings(report));
    }

    [Fact]
    public void Generate_SingleDomain_OmitsDomainClustersSection()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Company.Modules.Finance"));

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), CreateMetadata());

        Assert.DoesNotContain("## Domain Clusters", report);
    }

    [Fact]
    public void Generate_DomainClusterWithoutEdges_UsesEmDashPlaceholders()
    {
        var nodes = CreateNodes(
            ("A", "FinanceType", NodeKind.Type, "Company.Modules.Finance"),
            ("B", "PlanningType", NodeKind.Type, "Company.Modules.Planning"));

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), CreateMetadata());

        Assert.Contains("| Finance | Modules.Finance | — | — |", report);
        Assert.Contains("| Planning | Modules.Planning | — | — |", report);
    }

    [Fact]
    public void Generate_MethodOnlyGraph_LeavesSuggestedQueriesSectionWithoutCodeBlocks()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["method"] = new() { Id = "method", Name = "Execute", Kind = NodeKind.Method, AssemblyName = string.Empty }
        };

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), CreateMetadata());

        Assert.Contains("## Suggested Queries", report);
        Assert.DoesNotContain("```", report);
        Assert.EndsWith("## Suggested Queries", NormalizeLineEndings(report));
    }

    [Fact]
    public void Generate_CoverageSection_IncludesUncoveredAssembliesWhenAnyCoverageExists()
    {
        var nodes = CreateNodes(
            ("A", "CoveredType", NodeKind.Type, "Asm1"),
            ("B", "UncoveredType", NodeKind.Type, "Asm2"),
            ("T", "Tests", NodeKind.Type, "Tests"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains($"| Asm2 | 1 | 0 | {0.0:F1}% |", report);
        Assert.Contains($"| Asm1 | 1 | 1 | {100.0:F1}% |", report);
    }
}
