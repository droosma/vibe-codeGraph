using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query.Benchmarks;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests;

public class MutationKillerFormattingTests
{
    private static GraphNode Node(string id, string name, NodeKind kind = NodeKind.Type, string assemblyName = "App")
        => new()
        {
            Id = id,
            Name = name,
            Kind = kind,
            AssemblyName = assemblyName,
            Accessibility = Accessibility.Public
        };

    private static QueryResult CreateResult(GraphNode? targetNode, IReadOnlyList<GraphNode> nodes, params GraphEdge[] edges)
        => new()
        {
            TargetNode = targetNode,
            MatchedNodes = targetNode is null ? new List<GraphNode>() : new List<GraphNode> { targetNode },
            Nodes = nodes.ToDictionary(node => node.Id, node => node),
            Edges = edges.ToList(),
            Metadata = new GraphMetadata()
        };

    [Fact]
    public void Generate_NonInterfaceLikeNames_DoNotTriggerResolvesToSuggestion()
    {
        var target = Node("OrderService", "OrderService");
        var imposters = new[]
        {
            Node("Input", "Input"),
            Node("I", "I"),
            Node("iOrderRepository", "iOrderRepository")
        };
        var result = CreateResult(target, new[] { target }.Concat(imposters).ToArray());

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2 });

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_UsesFirstValidInterfaceNameForDiSuggestion()
    {
        var target = Node("OrderService", "OrderService");
        var imposter = Node("Input", "Input");
        var iface = Node("IOrderRepository", "IOrderRepository");
        var result = CreateResult(target, new[] { target, imposter, iface });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2 });

        Assert.Equal(new[] { "codegraph query IOrderRepository --kind resolves-to --format compact" }, suggestions);
    }

    [Fact]
    public void Generate_SessionRecordedCallsOnly_SuppressesCallSuggestion()
    {
        var target = Node("OrderService", "OrderService");
        var helper = Node("Helper", "Helper", NodeKind.Method);
        var session = new QuerySessionTracker();
        session.Record("OrderService --kind calls", depth: 1, resultCount: 1);
        var result = CreateResult(target, new[] { target, helper }, new GraphEdge { FromId = target.Id, ToId = helper.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2 }, session, maxSuggestions: 5);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_SingleAssemblyAndEmptyAssemblies_DoNotTriggerProjectSuggestion()
    {
        var target = Node("OrderService", "OrderService", assemblyName: "Orders.Core");
        var helper = Node("Helper", "Helper", assemblyName: string.Empty);
        var result = CreateResult(target, new[] { target, helper }, new GraphEdge { FromId = target.Id, ToId = helper.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 1 });

        Assert.Equal(new[]
        {
            "codegraph query OrderService --depth 2 --format compact",
            "codegraph query OrderService --depth 3 --kind calls --format compact"
        }, suggestions);
    }

    [Fact]
    public void Generate_ImplementedTypesOnlyUseInterfaceTargets()
    {
        var target = Node("OrderService", "OrderService");
        var iface = Node("IOrderRepository", "IOrderRepository");
        var concrete = Node("RepositoryImpl", "RepositoryImpl");
        var result = CreateResult(
            target,
            new[] { target, iface, concrete },
            new GraphEdge { FromId = target.Id, ToId = iface.Id, Type = EdgeType.Implements },
            new GraphEdge { FromId = target.Id, ToId = concrete.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2, EdgeTypeFilter = EdgeType.Calls });

        Assert.Equal(new[] { "codegraph query IOrderRepository --kind implements --format compact" }, suggestions);
    }

    [Fact]
    public void Generate_MethodOnlyGraph_OmitsHubTypesSection()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["method"] = Node("method", "Execute", NodeKind.Method, "App")
        };

        var brief = BriefGenerator.Generate(new GraphMetadata { SolutionName = "Test" }, nodes, new List<GraphEdge>());

        Assert.DoesNotContain("## Hub Types", brief);
    }

    [Fact]
    public void Generate_ExactlyFifteenDomainClusters_DoesNotShowCappingNote()
    {
        var nodes = Enumerable.Range(1, 15)
            .ToDictionary(
                index => $"T{index}",
                index => Node($"T{index}", $"Type{index}", assemblyName: $"Company.Modules.Domain{index:D2}"));

        var brief = BriefGenerator.Generate(new GraphMetadata { SolutionName = "Test" }, nodes, new List<GraphEdge>());

        Assert.Contains("## Domain Clusters", brief);
        Assert.DoesNotContain("showing top 15 of 15", brief);
    }

    [Fact]
    public void Generate_DomainClusterLine_JoinsAssembliesAndKeyTypesWithCommas()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["finance-one"] = Node("finance-one", "FinanceTypeOne", assemblyName: "Company.Modules.Finance"),
            ["finance-two"] = Node("finance-two", "FinanceTypeTwo", assemblyName: "Company.Api.Finance"),
            ["planning"] = Node("planning", "PlanningType", assemblyName: "Company.Modules.Planning")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "finance-one", ToId = "planning", Type = EdgeType.Calls },
            new() { FromId = "finance-two", ToId = "planning", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, new GraphMetadata { SolutionName = "Test", GeneratedAt = DateTimeOffset.UnixEpoch, CommitHash = "abc" });

        Assert.Contains("| Finance | Modules.Finance, Api.Finance | FinanceTypeOne, FinanceTypeTwo | → Planning (2) |", report);
    }

    [Fact]
    public void Detect_SameDomainEdges_AreExcludedFromCrossDomainConnections()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["finance-one"] = Node("finance-one", "FinanceTypeOne", assemblyName: "Company.Modules.Finance"),
            ["finance-two"] = Node("finance-two", "FinanceTypeTwo", assemblyName: "Company.Api.Finance"),
            ["planning"] = Node("planning", "PlanningType", assemblyName: "Company.Modules.Planning")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "finance-one", ToId = "finance-two", Type = EdgeType.Calls },
            new() { FromId = "finance-one", ToId = "planning", Type = EdgeType.Calls }
        };

        var finance = DomainClusterAnalyzer.Detect(nodes, edges).Single(cluster => cluster.Name == "Finance");

        var connection = Assert.Single(finance.CrossDomainConnections);
        Assert.Equal("Planning", connection.TargetDomain);
    }

    [Fact]
    public void Detect_TwoSegmentAssemblyNames_AreNotShortened()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["core"] = Node("core", "CoreType", assemblyName: "My.Core"),
            ["api"] = Node("api", "ApiType", assemblyName: "My.Api")
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, new List<GraphEdge>());

        Assert.Contains("My.Core", clusters.Single(cluster => cluster.Name == "My.Core").Assemblies);
        Assert.Contains("My.Api", clusters.Single(cluster => cluster.Name == "My.Api").Assemblies);
    }

    [Fact]
    public void ParseScenarios_NullJson_ThrowsInvalidJsonMessage()
    {
        var exception = Assert.Throws<JsonException>(() => BenchmarkRunner.ParseScenarios("null"));

        Assert.Equal("Invalid JSON.", exception.Message);
    }

    [Fact]
    public void ParseScenarios_MissingDescription_UsesEmptyString()
    {
        const string json = """
        {
          "scenarios": [
            {
              "name": "query-only",
              "command": "query",
              "args": { "pattern": "Foo" }
            }
          ]
        }
        """;

        var scenario = Assert.Single(BenchmarkRunner.ParseScenarios(json));

        Assert.Equal(string.Empty, scenario.Description);
    }

    [Fact]
    public void RunIteration_StopsStopwatchAndReturnsCounts()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = Node("App.QueryEngine", "QueryEngine", assemblyName: "App")
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var method = typeof(BenchmarkRunner).GetMethod("RunIteration", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var result = ((TimeSpan Elapsed, (int Nodes, int Edges, int TokenEstimate) Counts))method.Invoke(runner, new object[] { new BenchmarkScenario("query", string.Empty, "query", new JsonObject { ["pattern"] = "QueryEngine", ["depth"] = 0 }) })!;

        Assert.True(result.Elapsed >= TimeSpan.Zero);
        Assert.True(result.Counts.Nodes > 0);
    }

    [Fact]
    public void ExecuteQuery_WithMissingPattern_UsesEmptyStringDefaults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = Node("App.QueryEngine", "QueryEngine", assemblyName: "App")
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteQuery", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject() })!;
        var expected = engine.Query(new QueryOptions { Pattern = string.Empty, Depth = 1, Format = OutputFormat.Compact });

        Assert.Equal(expected.Nodes.Count, counts.Nodes);
        Assert.Equal(expected.Edges.Count, counts.Edges);
    }

    [Fact]
    public void ExecuteSearch_WithMissingQuery_UsesEmptyStringDefaults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = Node("App.QueryEngine", "QueryEngine", assemblyName: "App"),
            ["App.Service"] = Node("App.Service", "Service", assemblyName: "App")
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteSearch", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject() })!;
        var expectedResults = engine.Search(string.Empty, 20);
        var expectedOutput = string.Join("\n", expectedResults.Select(node => $"{node.Kind}: {node.Id}"));

        Assert.Equal(expectedResults.Count, counts.Nodes);
        Assert.Equal((expectedOutput.Length + 3) / 4, counts.TokenEstimate);
    }

    [Fact]
    public void ExecuteImpact_WithMissingSymbol_UsesEmptyStringDefaults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = Node("App.QueryEngine", "QueryEngine", assemblyName: "App")
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteImpact", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject() })!;

        Assert.Equal(0, counts.Nodes);
        Assert.Equal(0, counts.Edges);
        Assert.Equal(("Impact of : 0 affected across 0 layers".Length + 3) / 4, counts.TokenEstimate);
    }

    [Fact]
    public void ExecuteSearch_ReadsQueryPropertyExactly()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = Node("A", "A"),
            ["B"] = Node("B", "B")
        };
        var runner = new BenchmarkRunner(new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata()));
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteSearch", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject { ["query"] = "zzz", ["top"] = 5 } })!;

        Assert.Equal(0, counts.Nodes);
        Assert.Equal(0, counts.TokenEstimate);
    }

    [Fact]
    public void ExecuteSearch_TokenEstimateMatchesExactTwoLineOutput()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = Node("A", "A"),
            ["B"] = Node("B", "B")
        };
        var engine = new QueryEngine(nodes, new List<GraphEdge>(), new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteSearch", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject { ["query"] = string.Empty, ["top"] = 5 } })!;
        var expectedOutput = string.Join("\n", engine.Search(string.Empty, 5).Select(node => $"{node.Kind}: {node.Id}"));

        Assert.Equal((expectedOutput.Length + 3) / 4, counts.TokenEstimate);
    }

    [Fact]
    public void ExecuteListAssemblies_TokenEstimateMatchesExactOutput()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Type"] = Node("A.Type", "Type", assemblyName: "A"),
            ["B.Type"] = Node("B.Type", "Type", assemblyName: "B")
        };
        var edges = new List<GraphEdge>();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, new GraphMetadata()));
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteList", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var listEngine = new ListEngine(nodes, edges);
        var expectedOutput = string.Join("\n", listEngine.ListAssemblies().Select(assembly => $"{assembly.Name}: {assembly.TypeCount} types, {assembly.MethodCount} methods"));

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject { ["scope"] = "assemblies" } })!;

        Assert.Equal((expectedOutput.Length + 3) / 4, counts.TokenEstimate);
    }

    [Fact]
    public void ExecuteListTypes_TokenEstimateMatchesExactOutput()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = Node("A", "A", assemblyName: "Asm"),
            ["B"] = Node("B", "B", assemblyName: "Asm")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, new GraphMetadata()));
        var method = typeof(BenchmarkRunner).GetMethod("ExecuteList", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var listEngine = new ListEngine(nodes, edges);
        var expectedOutput = string.Join("\n", listEngine.ListTypes(top: 2).Types.Select(type => $"{type.Name}: in={type.InDegree} out={type.OutDegree}"));

        var counts = ((int Nodes, int Edges, int TokenEstimate))method.Invoke(runner, new object[] { new JsonObject { ["scope"] = "types", ["top"] = 2 } })!;

        Assert.Equal((expectedOutput.Length + 3) / 4, counts.TokenEstimate);
    }
}
