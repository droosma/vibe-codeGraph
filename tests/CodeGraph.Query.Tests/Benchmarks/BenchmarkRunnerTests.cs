using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query.Benchmarks;

namespace CodeGraph.Query.Tests.Benchmarks;

public class BenchmarkRunnerTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Meta) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = new GraphNode
            {
                Id = "App.QueryEngine", Name = "QueryEngine",
                Kind = NodeKind.Type, FilePath = "src/QueryEngine.cs",
                AssemblyName = "App", ContainingNamespaceId = "App",
                Accessibility = Accessibility.Public
            },
            ["App.GraphNode"] = new GraphNode
            {
                Id = "App.GraphNode", Name = "GraphNode",
                Kind = NodeKind.Type, FilePath = "src/GraphNode.cs",
                AssemblyName = "App", ContainingNamespaceId = "App",
                Accessibility = Accessibility.Public
            },
            ["App.GraphEdge"] = new GraphNode
            {
                Id = "App.GraphEdge", Name = "GraphEdge",
                Kind = NodeKind.Type, FilePath = "src/GraphEdge.cs",
                AssemblyName = "App", ContainingNamespaceId = "App",
                Accessibility = Accessibility.Public
            },
            ["App.Service"] = new GraphNode
            {
                Id = "App.Service", Name = "Service",
                Kind = NodeKind.Type, FilePath = "src/Service.cs",
                AssemblyName = "App", ContainingNamespaceId = "App",
                Accessibility = Accessibility.Public
            },
            ["App.QueryEngine.Execute"] = new GraphNode
            {
                Id = "App.QueryEngine.Execute", Name = "Execute",
                Kind = NodeKind.Method, FilePath = "src/QueryEngine.cs",
                AssemblyName = "App", ContainingNamespaceId = "App",
                ContainingTypeId = "App.QueryEngine",
                Accessibility = Accessibility.Public
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.QueryEngine", ToId = "App.GraphNode", Type = EdgeType.DependsOn },
            new() { FromId = "App.QueryEngine", ToId = "App.GraphEdge", Type = EdgeType.DependsOn },
            new() { FromId = "App.QueryEngine", ToId = "App.QueryEngine.Execute", Type = EdgeType.Contains },
            new() { FromId = "App.Service", ToId = "App.QueryEngine", Type = EdgeType.Calls }
        };

        var meta = new GraphMetadata
        {
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = "App.sln",
            ProjectsIndexed = new[] { "App" }
        };

        return (nodes, edges, meta);
    }

    [Fact]
    public void RunScenario_Query_ReturnsTimingAndCounts()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "query-test",
            "Test query scenario",
            "query",
            new JsonObject
            {
                ["pattern"] = "QueryEngine",
                ["depth"] = 1,
                ["format"] = "compact"
            });

        var result = runner.RunScenario(scenario, iterations: 3);

        Assert.Equal("query-test", result.ScenarioName);
        Assert.Equal(3, result.Iterations);
        Assert.True(result.Min <= result.Median);
        Assert.True(result.Median <= result.Max);
        Assert.True(result.Mean > TimeSpan.Zero);
        Assert.True(result.ResultNodeCount > 0);
        Assert.True(result.OutputTokenEstimate > 0);
    }

    [Fact]
    public void RunScenario_Search_ReturnsResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "search-test",
            "Test search scenario",
            "search",
            new JsonObject { ["query"] = "QueryEngine", ["top"] = 10 });

        var result = runner.RunScenario(scenario, iterations: 2);

        Assert.Equal("search-test", result.ScenarioName);
        Assert.True(result.ResultNodeCount > 0);
    }

    [Fact]
    public void RunScenario_ListAssemblies_ReturnsResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "list-assemblies-test",
            "Test list assemblies",
            "list",
            new JsonObject { ["scope"] = "assemblies" });

        var result = runner.RunScenario(scenario, iterations: 2);

        Assert.Equal("list-assemblies-test", result.ScenarioName);
        Assert.True(result.ResultNodeCount > 0);
    }

    [Fact]
    public void RunScenario_ListTypes_ReturnsResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "list-types-test",
            "Test list types",
            "list",
            new JsonObject { ["scope"] = "types", ["top"] = 100 });

        var result = runner.RunScenario(scenario, iterations: 2);

        Assert.Equal("list-types-test", result.ScenarioName);
        Assert.True(result.ResultNodeCount > 0);
    }

    [Fact]
    public void RunScenario_Impact_ReturnsResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "impact-test",
            "Test impact analysis",
            "impact",
            new JsonObject { ["symbol"] = "QueryEngine", ["depth"] = 3 });

        var result = runner.RunScenario(scenario, iterations: 2);

        Assert.Equal("impact-test", result.ScenarioName);
        Assert.True(result.ResultNodeCount >= 0);
    }

    [Fact]
    public void RunScenario_Batch_ReturnsAggregatedResults()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var symbolsArray = new JsonArray("QueryEngine", "GraphNode");
        var scenario = new BenchmarkScenario(
            "batch-test",
            "Test batch query",
            "batch",
            new JsonObject { ["symbols"] = symbolsArray, ["depth"] = 1 });

        var result = runner.RunScenario(scenario, iterations: 2);

        Assert.Equal("batch-test", result.ScenarioName);
        Assert.True(result.ResultNodeCount > 0);
    }

    [Fact]
    public void RunAll_MultipleScenarios_ReturnsResultForEach()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenarios = new List<BenchmarkScenario>
        {
            new("s1", "Scenario 1", "query", new JsonObject { ["pattern"] = "QueryEngine", ["depth"] = 1 }),
            new("s2", "Scenario 2", "search", new JsonObject { ["query"] = "Service", ["top"] = 20 }),
            new("s3", "Scenario 3", "list", new JsonObject { ["scope"] = "assemblies" })
        };

        var results = runner.RunAll(scenarios, iterations: 2);

        Assert.Equal(3, results.Count);
        Assert.Equal("s1", results[0].ScenarioName);
        Assert.Equal("s2", results[1].ScenarioName);
        Assert.Equal("s3", results[2].ScenarioName);
    }

    [Fact]
    public void ParseScenarios_ValidJson_ReturnsScenarios()
    {
        const string json = """
        {
          "scenarios": [
            {
              "name": "test-scenario",
              "description": "A test",
              "command": "query",
              "args": { "pattern": "Foo", "depth": 1 }
            },
            {
              "name": "another",
              "description": "Another test",
              "command": "search",
              "args": { "query": "Bar", "top": 10 }
            }
          ]
        }
        """;

        var scenarios = BenchmarkRunner.ParseScenarios(json);

        Assert.Equal(2, scenarios.Count);
        Assert.Equal("test-scenario", scenarios[0].Name);
        Assert.Equal("query", scenarios[0].Command);
        Assert.Equal("A test", scenarios[0].Description);
        Assert.Equal("Foo", scenarios[0].Args["pattern"]?.GetValue<string>());
        Assert.Equal("another", scenarios[1].Name);
        Assert.Equal("search", scenarios[1].Command);
    }

    [Fact]
    public void RunScenario_SingleIteration_MinEqualsMax()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "single-iter",
            "Single iteration test",
            "query",
            new JsonObject { ["pattern"] = "QueryEngine", ["depth"] = 0 });

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(result.Min, result.Max);
        Assert.Equal(result.Min, result.Median);
    }

    [Fact]
    public void FormatMarkdown_ContainsExpectedColumns()
    {
        var results = new List<BenchmarkResult>
        {
            new("test-scenario", 5,
                TimeSpan.FromMilliseconds(1.5), TimeSpan.FromMilliseconds(3.2),
                TimeSpan.FromMilliseconds(2.1), TimeSpan.FromMilliseconds(2.3),
                10, 15, 200)
        };

        var markdown = BenchmarkFormatter.FormatMarkdown(results);

        Assert.Contains("Scenario", markdown);
        Assert.Contains("Iterations", markdown);
        Assert.Contains("Min", markdown);
        Assert.Contains("Max", markdown);
        Assert.Contains("Median", markdown);
        Assert.Contains("Mean", markdown);
        Assert.Contains("Nodes", markdown);
        Assert.Contains("Edges", markdown);
        Assert.Contains("Tokens", markdown);
        Assert.Contains("test-scenario", markdown);
        Assert.Contains("10", markdown);
        Assert.Contains("15", markdown);
        Assert.Contains("200", markdown);
    }

    [Fact]
    public void FormatJson_ContainsExpectedFields()
    {
        var results = new List<BenchmarkResult>
        {
            new("json-test", 3,
                TimeSpan.FromMilliseconds(1.0), TimeSpan.FromMilliseconds(5.0),
                TimeSpan.FromMilliseconds(2.5), TimeSpan.FromMilliseconds(3.0),
                8, 12, 150)
        };

        var json = BenchmarkFormatter.FormatJson(results);

        Assert.Contains("\"scenario\"", json);
        Assert.Contains("\"json-test\"", json);
        Assert.Contains("\"iterations\"", json);
        Assert.Contains("\"min_ms\"", json);
        Assert.Contains("\"max_ms\"", json);
        Assert.Contains("\"median_ms\"", json);
        Assert.Contains("\"mean_ms\"", json);
        Assert.Contains("\"result_nodes\"", json);
        Assert.Contains("\"result_edges\"", json);
        Assert.Contains("\"output_tokens\"", json);
    }

    [Fact]
    public void FormatDuration_SubMillisecond_ShowsMicroseconds()
    {
        var ts = TimeSpan.FromMicroseconds(500);
        var formatted = BenchmarkFormatter.FormatDuration(ts);
        Assert.Contains("µs", formatted);
    }

    [Fact]
    public void FormatDuration_Milliseconds_ShowsMs()
    {
        var ts = TimeSpan.FromMilliseconds(42.7);
        var formatted = BenchmarkFormatter.FormatDuration(ts);
        Assert.Contains("ms", formatted);
        Assert.DoesNotContain("µs", formatted);
    }

    [Fact]
    public void FormatDuration_Seconds_ShowsSeconds()
    {
        var ts = TimeSpan.FromSeconds(1.5);
        var formatted = BenchmarkFormatter.FormatDuration(ts);
        Assert.Contains("s", formatted);
        Assert.DoesNotContain("ms", formatted);
    }

    [Fact]
    public void RunScenario_UnknownCommand_Throws()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "bad-cmd", "Unknown command", "nonexistent", new JsonObject());

        Assert.Throws<NotSupportedException>(() => runner.RunScenario(scenario, iterations: 1));
    }

    [Fact]
    public void RunScenario_ZeroIterations_Throws()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);

        var scenario = new BenchmarkScenario(
            "zero-iter", "Zero iterations", "query",
            new JsonObject { ["pattern"] = "QueryEngine" });

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunScenario(scenario, iterations: 0));
    }
}
