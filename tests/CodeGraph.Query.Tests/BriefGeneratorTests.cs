using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class BriefGeneratorTests
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

    [Fact]
    public void Generate_ContainsExpectedSections()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("M1", "DoWork", NodeKind.Method, "Asm1"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("# TestSolution", brief);
        Assert.Contains("## Assemblies", brief);
        Assert.Contains("## Suggested Queries", brief);
    }

    [Fact]
    public void Generate_HeaderIncludesProjectCountAndNodeEdgeCounts()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm2"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("2 projects", brief);
        Assert.Contains("2 nodes", brief);
        Assert.Contains("1 edges", brief);
    }

    [Fact]
    public void Generate_AssemblyTableShowsTypesAndMethods()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("M1", "DoWork", NodeKind.Method, "Asm1"),
            ("M2", "Init", NodeKind.Constructor, "Asm1"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("| Assembly | Types | Methods |", brief);
        Assert.Contains("| Asm1 | 1 | 2 |", brief);
    }

    [Fact]
    public void Generate_HubTypesShown()
    {
        var nodes = CreateNodes(
            ("A", "HubType", NodeKind.Type, "Asm1"),
            ("B", "LeafType", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("## Hub Types", brief);
        Assert.Contains("HubType", brief);
    }

    [Fact]
    public void Generate_InterfacesWithImplementations()
    {
        var nodes = CreateNodes(
            ("I", "IService", NodeKind.Type, "Asm1"),
            ("A", "ServiceA", NodeKind.Type, "Asm1"),
            ("B", "ServiceB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "I", Type = EdgeType.Implements },
            new() { FromId = "B", ToId = "I", Type = EdgeType.Implements }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("## Key Interfaces", brief);
        Assert.Contains("| IService | 2 |", brief);
    }

    [Fact]
    public void Generate_NoInterfacesSection_WhenNone()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.DoesNotContain("## Key Interfaces", brief);
    }

    [Fact]
    public void Generate_EntryPointsDetected()
    {
        var nodes = CreateNodes(
            ("C1", "HomeController", NodeKind.Type, "WebApp"),
            ("P1", "Program", NodeKind.Type, "WebApp"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("## Entry Points", brief);
        Assert.Contains("Controller: HomeController (WebApp)", brief);
        Assert.Contains("Program: WebApp", brief);
    }

    [Fact]
    public void Generate_NoEntryPointsSection_WhenNone()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.DoesNotContain("## Entry Points", brief);
    }

    [Fact]
    public void Generate_TestCoverageSummary()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("T", "TestA", NodeKind.Type, "Tests"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("## Test Coverage", brief);
        Assert.Contains("Asm1", brief);
    }

    [Fact]
    public void Generate_NoCoverageSection_WhenNone()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, new List<GraphEdge>());

        Assert.DoesNotContain("## Test Coverage", brief);
    }

    [Fact]
    public void Generate_SuggestedQueriesPresent()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("## Suggested Queries", brief);
        Assert.Contains("codegraph query", brief);
        Assert.Contains("codegraph list", brief);
        Assert.Contains("codegraph stats", brief);
    }

    [Fact]
    public void Generate_SuggestionsLimitedToFive()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        var lines = brief.Split('\n')
            .Where(l => l.StartsWith("- `codegraph", StringComparison.Ordinal))
            .ToList();

        Assert.True(lines.Count <= 5, $"Expected at most 5 suggestions but got {lines.Count}");
    }

    [Fact]
    public void FindTopInterfaces_ReturnsOrderedByCount()
    {
        var nodes = CreateNodes(
            ("I1", "IFoo", NodeKind.Type, "Asm1"),
            ("I2", "IBar", NodeKind.Type, "Asm1"),
            ("A", "FooA", NodeKind.Type, "Asm1"),
            ("B", "FooB", NodeKind.Type, "Asm1"),
            ("C", "BarA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "I1", Type = EdgeType.Implements },
            new() { FromId = "B", ToId = "I1", Type = EdgeType.Implements },
            new() { FromId = "C", ToId = "I2", Type = EdgeType.Implements }
        };

        var result = BriefGenerator.FindTopInterfaces(nodes, edges, 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("IFoo", result[0].Name);
        Assert.Equal(2, result[0].Count);
        Assert.Equal("IBar", result[1].Name);
        Assert.Equal(1, result[1].Count);
    }

    [Fact]
    public void FindEntryPoints_DetectsControllersProgramAndHostedServices()
    {
        var nodes = CreateNodes(
            ("C1", "OrderController", NodeKind.Type, "Api"),
            ("P1", "Program", NodeKind.Type, "Api"),
            ("H1", "WorkerHostedService", NodeKind.Type, "Api"),
            ("T1", "RegularType", NodeKind.Type, "Api"));

        var result = BriefGenerator.FindEntryPoints(nodes);

        Assert.Equal(3, result.Count);
        Assert.Contains("Controller: OrderController (Api)", result);
        Assert.Contains("Program: Api", result);
        Assert.Contains("HostedService: WorkerHostedService (Api)", result);
    }
}
