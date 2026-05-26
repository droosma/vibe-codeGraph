using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query.Benchmarks;
using CodeGraph.Query.OutputFormatters;

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

    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Meta) BuildImpactGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.QueryEngine"] = new() { Id = "App.QueryEngine", Name = "QueryEngine", Kind = NodeKind.Type, AssemblyName = "App", ContainingNamespaceId = "App" },
            ["App.Service"] = new() { Id = "App.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "App", ContainingNamespaceId = "App" },
            ["App.Controller"] = new() { Id = "App.Controller", Name = "Controller", Kind = NodeKind.Type, AssemblyName = "App", ContainingNamespaceId = "App" },
            ["App.Gateway"] = new() { Id = "App.Gateway", Name = "Gateway", Kind = NodeKind.Type, AssemblyName = "App", ContainingNamespaceId = "App" }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Service", ToId = "App.QueryEngine", Type = EdgeType.Calls },
            new() { FromId = "App.Controller", ToId = "App.Service", Type = EdgeType.Calls },
            new() { FromId = "App.Gateway", ToId = "App.Controller", Type = EdgeType.Calls }
        };

        var meta = new GraphMetadata
        {
            CommitHash = "impact123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = "Impact.sln",
            ProjectsIndexed = new[] { "App" }
        };

        return (nodes, edges, meta);
    }

    private static int EstimateTokens(string text) => (text.Length + 3) / 4;

    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    private static BenchmarkResult InvokeCreateBenchmarkResult(
        string scenarioName,
        int iterations,
        List<TimeSpan> timings,
        (int Nodes, int Edges, int TokenEstimate) finalCounts)
    {
        var method = typeof(BenchmarkRunner).GetMethod("CreateBenchmarkResult", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (BenchmarkResult)method!.Invoke(null, new object[] { scenarioName, iterations, timings, finalCounts })!;
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

    [Fact]
    public void RunScenario_Query_UsesPatternDepthAndCompactTokenEstimate()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "query-depth-zero",
            "Exact query benchmark",
            "query",
            new JsonObject { ["pattern"] = "QueryEngine", ["depth"] = 0 });
        var expectedQuery = engine.Query(new QueryOptions { Pattern = "QueryEngine", Depth = 0, Format = OutputFormat.Compact });
        var expectedOutput = CompactFormatter.Format(expectedQuery);

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(expectedQuery.Nodes.Count, result.ResultNodeCount);
        Assert.Equal(expectedQuery.Edges.Count, result.ResultEdgeCount);
        Assert.Equal(EstimateTokens(expectedOutput), result.OutputTokenEstimate);
    }

    [Fact]
    public void RunScenario_Search_UsesTopAndSearchFormatting()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "search-top-one",
            "Exact search benchmark",
            "search",
            new JsonObject { ["query"] = "Graph", ["top"] = 1 });
        var expectedResults = engine.Search("Graph", 1);
        var expectedOutput = string.Join("\n", expectedResults.Select(node => $"{node.Kind}: {node.Id}"));

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(1, result.ResultNodeCount);
        Assert.Equal(0, result.ResultEdgeCount);
        Assert.Equal(EstimateTokens(expectedOutput), result.OutputTokenEstimate);
    }

    [Fact]
    public void RunScenario_ListAssemblies_UsesAssemblyFormattingForTokenEstimate()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "list-assemblies-exact",
            "Exact list assemblies benchmark",
            "list",
            new JsonObject { ["scope"] = "assemblies" });
        var listEngine = new ListEngine(nodes, edges);
        var assemblies = listEngine.ListAssemblies();
        var expectedOutput = string.Join("\n", assemblies.Select(assembly => $"{assembly.Name}: {assembly.TypeCount} types, {assembly.MethodCount} methods"));

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(assemblies.Count, result.ResultNodeCount);
        Assert.Equal(0, result.ResultEdgeCount);
        Assert.Equal(EstimateTokens(expectedOutput), result.OutputTokenEstimate);
    }

    [Fact]
    public void RunScenario_ListTypes_UsesScopeTopAndTypeFormatting()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "list-types-exact",
            "Exact list types benchmark",
            "list",
            new JsonObject { ["scope"] = "types", ["top"] = 1 });
        var listEngine = new ListEngine(nodes, edges);
        var typeResult = listEngine.ListTypes(top: 1);
        var expectedOutput = string.Join("\n", typeResult.Types.Select(type => $"{type.Name}: in={type.InDegree} out={type.OutDegree}"));

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(1, result.ResultNodeCount);
        Assert.Equal(0, result.ResultEdgeCount);
        Assert.Equal(EstimateTokens(expectedOutput), result.OutputTokenEstimate);
    }

    [Fact]
    public void RunScenario_Impact_UsesSymbolDepthAndAggregatesAllLayers()
    {
        var (nodes, edges, meta) = BuildImpactGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "impact-exact",
            "Exact impact benchmark",
            "impact",
            new JsonObject { ["symbol"] = "QueryEngine", ["depth"] = 2 });
        var analysis = new ImpactAnalyzer(nodes, edges).Analyze("QueryEngine", 2);
        var expectedNodeCount = analysis.Layers.Sum(layer => layer.Nodes.Count);
        var expectedEdgeCount = analysis.Layers.Sum(layer => layer.Edges.Count);
        var expectedOutput = $"Impact of QueryEngine: {analysis.TotalAffected} affected across {analysis.Layers.Count} layers";

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(2, analysis.TotalAffected);
        Assert.Equal(2, analysis.Layers.Count);
        Assert.Equal(expectedNodeCount, result.ResultNodeCount);
        Assert.Equal(expectedEdgeCount, result.ResultEdgeCount);
        Assert.Equal(EstimateTokens(expectedOutput), result.OutputTokenEstimate);
    }

    [Fact]
    public void RunScenario_Batch_IgnoresEmptySymbolsAndAccumulatesCountsAndTokens()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var engine = new QueryEngine(nodes, edges, meta);
        var runner = new BenchmarkRunner(engine);
        var scenario = new BenchmarkScenario(
            "batch-exact",
            "Exact batch benchmark",
            "batch",
            new JsonObject
            {
                ["symbols"] = new JsonArray("QueryEngine", string.Empty, JsonValue.Create<string?>(null), "GraphNode"),
                ["depth"] = 0
            });
        var symbols = new[] { "QueryEngine", "GraphNode" };
        var expectedNodeCount = 0;
        var expectedEdgeCount = 0;
        var expectedTokens = 0;

        foreach (var symbol in symbols)
        {
            var query = engine.Query(new QueryOptions { Pattern = symbol, Depth = 0, Format = OutputFormat.Compact });
            expectedNodeCount += query.Nodes.Count;
            expectedEdgeCount += query.Edges.Count;
            expectedTokens += EstimateTokens(CompactFormatter.Format(query));
        }

        var result = runner.RunScenario(scenario, iterations: 1);

        Assert.Equal(expectedNodeCount, result.ResultNodeCount);
        Assert.Equal(expectedEdgeCount, result.ResultEdgeCount);
        Assert.Equal(expectedTokens, result.OutputTokenEstimate);
    }

    [Fact]
    public void CreateBenchmarkResult_UsesSortedTimingsAndAverageMean()
    {
        var timings = new List<TimeSpan>
        {
            TimeSpan.FromTicks(90),
            TimeSpan.FromTicks(10),
            TimeSpan.FromTicks(30)
        };

        var result = InvokeCreateBenchmarkResult("mean-test", 3, timings, (7, 11, 13));

        Assert.Equal(TimeSpan.FromTicks(10), result.Min);
        Assert.Equal(TimeSpan.FromTicks(90), result.Max);
        Assert.Equal(TimeSpan.FromTicks(30), result.Median);
        Assert.Equal(TimeSpan.FromTicks(43), result.Mean);
        Assert.Equal(7, result.ResultNodeCount);
        Assert.Equal(11, result.ResultEdgeCount);
        Assert.Equal(13, result.OutputTokenEstimate);
    }

    [Fact]
    public void FormatMarkdown_ReturnsExactMarkdownTable()
    {
        var markdown = BenchmarkFormatter.FormatMarkdown(new List<BenchmarkResult>
        {
            new(
                "timing-test",
                7,
                TimeSpan.FromMicroseconds(500),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(12.34),
                TimeSpan.FromMilliseconds(345),
                10,
                15,
                200)
        });

        var expected = string.Join("\n", new[]
        {
            "## Benchmark Results",
            string.Empty,
            "| Scenario | Iterations | Min | Max | Median | Mean | Nodes | Edges | Tokens |",
            "|----------|------------|-----|-----|--------|------|-------|-------|--------|",
            "| timing-test | 7 | 500.0µs | 2.00s | 12.34ms | 345.00ms | 10 | 15 | 200 |",
            string.Empty
        });

        Assert.Equal(expected, NormalizeLineEndings(markdown));
    }

    [Fact]
    public void FormatJson_ReturnsExactIndentedJson()
    {
        var json = BenchmarkFormatter.FormatJson(new List<BenchmarkResult>
        {
            new(
                "json-test",
                3,
                TimeSpan.FromMilliseconds(1.234),
                TimeSpan.FromMilliseconds(5.678),
                TimeSpan.FromMilliseconds(2.5),
                TimeSpan.FromMilliseconds(3.333),
                8,
                12,
                150)
        });

        var expected = string.Join("\n", new[]
        {
            "[",
            "  {",
            "    \"scenario\": \"json-test\",",
            "    \"iterations\": 3,",
            "    \"min_ms\": 1.234,",
            "    \"max_ms\": 5.678,",
            "    \"median_ms\": 2.5,",
            "    \"mean_ms\": 3.333,",
            "    \"result_nodes\": 8,",
            "    \"result_edges\": 12,",
            "    \"output_tokens\": 150",
            "  }",
            "]"
        });

        Assert.Equal(expected, NormalizeLineEndings(json));
    }

    [Fact]
    public void FormatDuration_ExactlyOneMillisecond_UsesMilliseconds()
    {
        Assert.Equal("1.00ms", BenchmarkFormatter.FormatDuration(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void FormatDuration_ExactlyOneSecond_UsesSeconds()
    {
        Assert.Equal("1.00s", BenchmarkFormatter.FormatDuration(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void RunScenario_UnknownCommand_ThrowsExactMessage()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, meta));
        var scenario = new BenchmarkScenario("bad-cmd", "Unknown command", "nonexistent", new JsonObject());

        var exception = Assert.Throws<NotSupportedException>(() => runner.RunScenario(scenario, iterations: 1));

        Assert.Equal("Unknown command 'nonexistent'.", exception.Message);
    }

    [Fact]
    public void RunScenario_ZeroIterations_ThrowsWithParameterNameAndMessage()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, meta));
        var scenario = new BenchmarkScenario("zero-iter", "Zero iterations", "query", new JsonObject { ["pattern"] = "QueryEngine" });

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunScenario(scenario, iterations: 0));

        Assert.Equal("iterations", exception.ParamName);
        Assert.Contains("Iterations must be at least 1.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadScenarios_ReadsJsonFile()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, $"benchmark-scenarios-{Guid.NewGuid():N}.json");
        File.WriteAllText(jsonPath, """
        {
          "scenarios": [
            {
              "name": "file-scenario",
              "description": "Loaded from disk",
              "command": "query",
              "args": { "pattern": "QueryEngine", "depth": 1 }
            }
          ]
        }
        """);

        try
        {
            var scenarios = BenchmarkRunner.LoadScenarios(jsonPath);

            var scenario = Assert.Single(scenarios);
            Assert.Equal("file-scenario", scenario.Name);
            Assert.Equal("query", scenario.Command);
        }
        finally
        {
            if (File.Exists(jsonPath))
                File.Delete(jsonPath);
        }
    }

    [Fact]
    public void ParseScenarios_NullItem_IsIgnored()
    {
        const string json = """
        {
          "scenarios": [
            null,
            {
              "name": "kept",
              "command": "search",
              "args": { "query": "Graph" }
            }
          ]
        }
        """;

        var scenarios = BenchmarkRunner.ParseScenarios(json);

        var scenario = Assert.Single(scenarios);
        Assert.Equal("kept", scenario.Name);
    }

    [Fact]
    public void ParseScenarios_MissingScenariosArray_ThrowsExactMessage()
    {
        var exception = Assert.Throws<JsonException>(() => BenchmarkRunner.ParseScenarios("{}"));

        Assert.Equal("Missing 'scenarios' array.", exception.Message);
    }

    [Fact]
    public void ParseScenarios_MissingName_ThrowsExactMessage()
    {
        const string json = """
        {
          "scenarios": [
            {
              "command": "query"
            }
          ]
        }
        """;

        var exception = Assert.Throws<JsonException>(() => BenchmarkRunner.ParseScenarios(json));

        Assert.Equal("Scenario missing 'name'.", exception.Message);
    }

    [Fact]
    public void ParseScenarios_MissingCommand_ThrowsExactMessage()
    {
        const string json = """
        {
          "scenarios": [
            {
              "name": "missing-command"
            }
          ]
        }
        """;

        var exception = Assert.Throws<JsonException>(() => BenchmarkRunner.ParseScenarios(json));

        Assert.Equal("Scenario 'missing-command' missing 'command'.", exception.Message);
    }

    [Fact]
    public void RunScenario_CommandAndListScope_AreCaseInsensitiveAndAssembliesIsDefaultScope()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, meta));

        var result = runner.RunScenario(new BenchmarkScenario("upper-list", "List assemblies", "LIST", new JsonObject()), iterations: 1);

        Assert.Equal("upper-list", result.ScenarioName);
        Assert.True(result.ResultNodeCount > 0);
        Assert.Equal(0, result.ResultEdgeCount);
    }

    [Fact]
    public void RunScenario_ListUnknownScope_ThrowsExactMessage()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, meta));
        var scenario = new BenchmarkScenario("bad-scope", "Unknown scope", "list", new JsonObject { ["scope"] = "widgets" });

        var exception = Assert.Throws<NotSupportedException>(() => runner.RunScenario(scenario, iterations: 1));

        Assert.Equal("Unknown list scope 'widgets'.", exception.Message);
    }

    [Fact]
    public void RunScenario_BatchWithoutSymbols_ReturnsZeroCounts()
    {
        var (nodes, edges, meta) = BuildTestGraph();
        var runner = new BenchmarkRunner(new QueryEngine(nodes, edges, meta));

        var result = runner.RunScenario(new BenchmarkScenario("empty-batch", "No symbols", "batch", new JsonObject()), iterations: 1);

        Assert.Equal(0, result.ResultNodeCount);
        Assert.Equal(0, result.ResultEdgeCount);
        Assert.Equal(0, result.OutputTokenEstimate);
    }
}
