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

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

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
    public void Generate_AssembliesCappedAtTop20()
    {
        // Create 30 assemblies — only top 20 should appear
        var defs = new List<(string id, string name, NodeKind kind, string assembly)>();
        for (int i = 1; i <= 30; i++)
        {
            var asm = $"Asm{i:D2}";
            // Add 'i' types to each assembly so ordering is deterministic (Asm30 has most)
            for (int t = 0; t < i; t++)
                defs.Add(($"T{i}_{t}", $"Type{i}_{t}", NodeKind.Type, asm));
        }

        var nodes = CreateNodes(defs.ToArray());
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("(showing top 20 of 30)", brief);
        // Asm30 (highest count) should be shown
        Assert.Contains("Asm30", brief);
        // Asm01 (lowest count) should NOT be shown since we take top 20
        Assert.DoesNotContain("| Asm01 |", brief);
    }

    [Fact]
    public void Generate_EntryPointsCappedAtTop20()
    {
        // Create 25 controllers — only top 20 should appear
        var defs = new List<(string id, string name, NodeKind kind, string assembly)>();
        for (int i = 1; i <= 25; i++)
            defs.Add(($"C{i}", $"Item{i:D2}Controller", NodeKind.Type, "WebApp"));

        var nodes = CreateNodes(defs.ToArray());
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("(showing top 20 of 25)", brief);
        // Count entry point lines
        var entryLines = brief.Split('\n')
            .Where(l => l.StartsWith("- Controller:", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(20, entryLines.Count);
    }

    [Fact]
    public void Generate_NoCappingNoteWhenUnderLimit()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm2"));
        var edges = new List<GraphEdge>();

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.DoesNotContain("showing top", brief);
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

    [Fact]
    public void Generate_FullOutput_MatchesExactFormat()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["finance-order-service"] = new() { Id = "finance-order-service", Name = "OrderService", Kind = NodeKind.Type, AssemblyName = "Company.Modules.Finance" },
            ["finance-order-repository"] = new() { Id = "finance-order-repository", Name = "IOrderRepository", Kind = NodeKind.Type, AssemblyName = "Company.Modules.Finance" },
            ["finance-order-service-ctor"] = new() { Id = "finance-order-service-ctor", Name = ".ctor", Kind = NodeKind.Constructor, AssemblyName = "Company.Modules.Finance" },
            ["app-home-controller"] = new() { Id = "app-home-controller", Name = "HomeController", Kind = NodeKind.Type, AssemblyName = "App" },
            ["app-program"] = new() { Id = "app-program", Name = "Program", Kind = NodeKind.Type, AssemblyName = "App" },
            ["app-worker"] = new() { Id = "app-worker", Name = "WorkerHostedService", Kind = NodeKind.Type, AssemblyName = "App" },
            ["pricing-calculator"] = new() { Id = "pricing-calculator", Name = "PricingCalculator", Kind = NodeKind.Type, AssemblyName = "Company.Modules.Pricing" },
            ["order-service-test"] = new() { Id = "order-service-test", Name = "OrderServiceTests.CoversOrderService", Kind = NodeKind.Method, AssemblyName = "Tests" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "app-home-controller", ToId = "finance-order-service", Type = EdgeType.Calls },
            new() { FromId = "finance-order-service", ToId = "finance-order-repository", Type = EdgeType.Implements },
            new() { FromId = "finance-order-service", ToId = "pricing-calculator", Type = EdgeType.Calls },
            new() { FromId = "finance-order-service", ToId = "order-service-test", Type = EdgeType.CoveredBy }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        var expected = string.Join("\n", new[]
        {
            "# TestSolution — 4 projects, 8 nodes, 4 edges",
            string.Empty,
            "## Assemblies",
            string.Empty,
            "| Assembly | Types | Methods |",
            "|----------|------:|--------:|",
            "| Company.Modules.Finance | 2 | 1 |",
            "| App | 3 | 0 |",
            "| Company.Modules.Pricing | 1 | 0 |",
            "| Tests | 0 | 1 |",
            string.Empty,
            "## Hub Types",
            string.Empty,
            "| Type | In | Out | Total |",
            "|------|---:|----:|------:|",
            "| OrderService | 1 | 3 | 4 |",
            "| IOrderRepository | 1 | 0 | 1 |",
            "| HomeController | 0 | 1 | 1 |",
            "| PricingCalculator | 1 | 0 | 1 |",
            "| Program | 0 | 0 | 0 |",
            "| WorkerHostedService | 0 | 0 | 0 |",
            string.Empty,
            "## Key Interfaces",
            string.Empty,
            "| Interface | Implementations |",
            "|-----------|----------------:|",
            "| IOrderRepository | 1 |",
            string.Empty,
            "## Domain Clusters",
            string.Empty,
            "| Domain | Types | Key Types |",
            "|--------|------:|-----------|",
            "| App | 3 | HomeController |",
            "| Finance | 2 | OrderService, IOrderRepository |",
            "| Pricing | 1 | PricingCalculator |",
            string.Empty,
            "## Entry Points",
            string.Empty,
            "- Controller: HomeController (App)",
            "- HostedService: WorkerHostedService (App)",
            "- Program: App",
            string.Empty,
            "## Test Coverage",
            string.Empty,
            "| Assembly | Types | Covered | Coverage |",
            "|----------|------:|--------:|---------:|",
            $"| Company.Modules.Finance | 2 | 1 | {50.0:F1}% |",
            string.Empty,
            "## Suggested Queries",
            string.Empty,
            "- `codegraph query OrderService --depth 2 --mode focused`",
            "- `codegraph query OrderService --depth 1 --kind calls`",
            "- `codegraph list interfaces`",
            "- `codegraph list types --assembly Company.Modules.Finance`",
            "- `codegraph stats`"
        });

        Assert.Equal(expected, NormalizeLineEndings(brief));
    }

    [Fact]
    public void Generate_SuggestionsWithoutHubsOrAssemblies_OnlyIncludesBaseCommands()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["test-method"] = new() { Id = "test-method", Name = "Execute", Kind = NodeKind.Method, AssemblyName = string.Empty }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, new List<GraphEdge>());

        var suggestionLines = brief.Split('\n')
            .Where(line => line.StartsWith("- `codegraph", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(new[]
        {
            "- `codegraph list interfaces`",
            "- `codegraph stats`"
        }, suggestionLines);
    }

    [Fact]
    public void Generate_SingleDomain_OmitsDomainClustersSection()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Only.Assembly"),
            ("B", "TypeB", NodeKind.Type, "Only.Assembly"));

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, new List<GraphEdge>());

        Assert.DoesNotContain("## Domain Clusters", brief);
    }

    [Fact]
    public void Generate_ExactlyTwentyAssemblies_DoesNotShowCappingNote()
    {
        var definitions = Enumerable.Range(1, 20)
            .Select(i => ($"T{i}", $"Type{i}", NodeKind.Type, $"Asm{i:D2}"))
            .ToArray();

        var brief = BriefGenerator.Generate(CreateMetadata(), CreateNodes(definitions), new List<GraphEdge>());

        Assert.DoesNotContain("showing top 20", brief);
        Assert.Contains("| Asm20 | 1 | 0 |", brief);
    }

    [Fact]
    public void Generate_ExactlyTwentyEntryPoints_DoesNotShowCappingNote()
    {
        var definitions = Enumerable.Range(1, 20)
            .Select(i => ($"C{i}", $"Item{i:D2}Controller", NodeKind.Type, "WebApp"))
            .ToArray();

        var brief = BriefGenerator.Generate(CreateMetadata(), CreateNodes(definitions), new List<GraphEdge>());

        Assert.DoesNotContain("showing top 20 of 20", brief);
        Assert.Equal(20, brief.Split('\n').Count(line => line.StartsWith("- Controller:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Generate_ExactlyTenCoverageRows_DoesNotShowCappingNote()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();

        foreach (var index in Enumerable.Range(1, 10))
        {
            var typeId = $"type-{index}";
            var testId = $"test-{index}";
            nodes[typeId] = new GraphNode { Id = typeId, Name = $"Type{index}", Kind = NodeKind.Type, AssemblyName = $"Asm{index:D2}" };
            nodes[testId] = new GraphNode { Id = testId, Name = $"Test{index}", Kind = NodeKind.Method, AssemblyName = "Tests" };
            edges.Add(new GraphEdge { FromId = typeId, ToId = testId, Type = EdgeType.CoveredBy });
        }

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.DoesNotContain("showing top 10 of 10", brief);
        Assert.Equal(10, brief.Split('\n').Count(line => line.StartsWith("| Asm", StringComparison.Ordinal) && line.Contains('%', StringComparison.Ordinal)));
    }

    [Fact]
    public void FindEntryPoints_MetadataHostedService_IsDetectedAndSorted()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["controller"] = new() { Id = "controller", Name = "OrdersController", Kind = NodeKind.Type, AssemblyName = "Api" },
            ["program"] = new() { Id = "program", Name = "Program", Kind = NodeKind.Type, AssemblyName = "Api" },
            ["worker"] = new() { Id = "worker", Name = "BackgroundWorker", Kind = NodeKind.Type, AssemblyName = "Api", Metadata = new Dictionary<string, string> { ["IsHostedService"] = "true" } }
        };

        var result = BriefGenerator.FindEntryPoints(nodes);

        Assert.Equal(new[]
        {
            "Controller: OrdersController (Api)",
            "HostedService: BackgroundWorker (Api)",
            "Program: Api"
        }, result);
    }

    [Fact]
    public void FindTopInterfaces_IgnoresEdgesToMissingInterfaceNodes()
    {
        var nodes = CreateNodes(
            ("I1", "IFoo", NodeKind.Type, "Asm1"),
            ("A", "FooA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "I1", Type = EdgeType.Implements },
            new() { FromId = "A", ToId = "Missing", Type = EdgeType.Implements }
        };

        var result = BriefGenerator.FindTopInterfaces(nodes, edges, 10);

        var iface = Assert.Single(result);
        Assert.Equal(("IFoo", 1), (iface.Name, iface.Count));
    }

    [Fact]
    public void Generate_ProjectCount_IgnoresNodesWithoutAssemblyNames()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["with-assembly"] = new() { Id = "with-assembly", Name = "TypeA", Kind = NodeKind.Type, AssemblyName = "Asm1" },
            ["without-assembly"] = new() { Id = "without-assembly", Name = "Helper", Kind = NodeKind.Method, AssemblyName = string.Empty }
        };

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, new List<GraphEdge>());

        Assert.Contains("# TestSolution — 1 projects, 2 nodes, 0 edges", brief);
    }

    [Fact]
    public void Generate_DomainClustersCappedAtTop15()
    {
        var definitions = Enumerable.Range(1, 16)
            .Select(index => ($"T{index}", $"Type{index}", NodeKind.Type, $"Company.Modules.Domain{index:D2}"))
            .ToArray();

        var brief = BriefGenerator.Generate(CreateMetadata(), CreateNodes(definitions), new List<GraphEdge>());

        Assert.Contains("_(showing top 15 of 16)_", brief);
        Assert.Contains("| Domain01 | 1 | — |", brief);
        Assert.DoesNotContain("| Domain16 |", brief);
    }

    [Fact]
    public void Generate_CoverageRowsCappedAtTop10()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();

        foreach (var index in Enumerable.Range(1, 11))
        {
            var typeId = $"covered-type-{index}";
            var testId = $"covered-test-{index}";
            nodes[typeId] = new GraphNode { Id = typeId, Name = $"Type{index}", Kind = NodeKind.Type, AssemblyName = $"Asm{index:D2}" };
            nodes[testId] = new GraphNode { Id = testId, Name = $"Test{index}", Kind = NodeKind.Method, AssemblyName = "Tests" };
            edges.Add(new GraphEdge { FromId = typeId, ToId = testId, Type = EdgeType.CoveredBy });
        }

        var brief = BriefGenerator.Generate(CreateMetadata(), nodes, edges);

        Assert.Contains("_(showing top 10 of 11)_", brief);
        Assert.Contains($"| Asm01 | 1 | 1 | {100.0:F1}% |", brief);
        Assert.DoesNotContain($"| Asm11 | 1 | 1 | {100.0:F1}% |", brief);
        Assert.Equal(10, brief.Split('\n').Count(line => line.StartsWith("| Asm", StringComparison.Ordinal) && line.Contains('%', StringComparison.Ordinal)));
    }
}
