using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Benchmarks;

/// <summary>
/// A scenario to benchmark, loaded from scenarios.json or constructed in code.
/// </summary>
public record BenchmarkScenario(string Name, string Description, string Command, JsonObject Args);

/// <summary>
/// Timing and size results from running a single benchmark scenario.
/// </summary>
public record BenchmarkResult(
    string ScenarioName,
    int Iterations,
    TimeSpan Min,
    TimeSpan Max,
    TimeSpan Median,
    TimeSpan Mean,
    int ResultNodeCount,
    int ResultEdgeCount,
    int OutputTokenEstimate);

/// <summary>
/// Runs benchmark scenarios against a <see cref="QueryEngine"/> and collects timing statistics.
/// </summary>
public class BenchmarkRunner
{
    private readonly QueryEngine _engine;

    public BenchmarkRunner(QueryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Run a single scenario for the given number of iterations and return aggregated results.
    /// </summary>
    public BenchmarkResult RunScenario(BenchmarkScenario scenario, int iterations = 5)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be at least 1.");

        var timings = new List<TimeSpan>(iterations);
        var finalCounts = (Nodes: 0, Edges: 0, TokenEstimate: 0);

        for (var i = 0; i < iterations; i++)
        {
            var iterationResult = RunIteration(scenario);
            timings.Add(iterationResult.Elapsed);
            finalCounts = iterationResult.Counts;
        }

        return CreateBenchmarkResult(scenario.Name, iterations, timings, finalCounts);
    }

    /// <summary>
    /// Run all supplied scenarios and return results for each.
    /// </summary>
    public IReadOnlyList<BenchmarkResult> RunAll(IReadOnlyList<BenchmarkScenario> scenarios, int iterations = 5)
    {
        var results = new List<BenchmarkResult>(scenarios.Count);
        foreach (var scenario in scenarios)
            results.Add(RunScenario(scenario, iterations));

        return results;
    }

    /// <summary>
    /// Load benchmark scenarios from a JSON file.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> LoadScenarios(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return ParseScenarios(json);
    }

    /// <summary>
    /// Parse benchmark scenarios from a JSON string.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> ParseScenarios(string json)
    {
        var document = JsonNode.Parse(json) ?? throw new JsonException("Invalid JSON.");
        var scenariosArray = document["scenarios"]?.AsArray()
            ?? throw new JsonException("Missing 'scenarios' array.");

        var scenarios = new List<BenchmarkScenario>();
        foreach (var item in scenariosArray)
        {
            if (item is null)
                continue;

            scenarios.Add(ParseScenario(item));
        }

        return scenarios;
    }

    private static BenchmarkScenario ParseScenario(JsonNode item)
    {
        var name = item["name"]?.GetValue<string>()
            ?? throw new JsonException("Scenario missing 'name'.");
        var description = item["description"]?.GetValue<string>() ?? string.Empty;
        var command = item["command"]?.GetValue<string>()
            ?? throw new JsonException($"Scenario '{name}' missing 'command'.");
        var args = item["args"]?.AsObject() ?? new JsonObject();

        return new BenchmarkScenario(name, description, command, args);
    }

    private (TimeSpan Elapsed, (int Nodes, int Edges, int TokenEstimate) Counts) RunIteration(BenchmarkScenario scenario)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = ExecuteScenario(scenario);
        stopwatch.Stop();

        return (stopwatch.Elapsed, counts);
    }

    private static BenchmarkResult CreateBenchmarkResult(
        string scenarioName,
        int iterations,
        List<TimeSpan> timings,
        (int Nodes, int Edges, int TokenEstimate) finalCounts)
    {
        timings.Sort();

        var min = timings[0];
        var max = timings[^1];
        var median = timings[timings.Count / 2];
        var mean = TimeSpan.FromTicks(timings.Sum(timing => timing.Ticks) / timings.Count);

        return new BenchmarkResult(
            scenarioName,
            iterations,
            min,
            max,
            median,
            mean,
            finalCounts.Nodes,
            finalCounts.Edges,
            finalCounts.TokenEstimate);
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteScenario(BenchmarkScenario scenario)
    {
        var command = scenario.Command.ToLowerInvariant();
        return command switch
        {
            "query" => ExecuteQuery(scenario.Args),
            "search" => ExecuteSearch(scenario.Args),
            "list" => ExecuteList(scenario.Args),
            "impact" => ExecuteImpact(scenario.Args),
            "batch" => ExecuteBatch(scenario.Args),
            _ => throw new NotSupportedException($"Unknown command '{scenario.Command}'.")
        };
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteQuery(JsonObject args)
    {
        var pattern = args["pattern"]?.GetValue<string>() ?? string.Empty;
        var depth = args["depth"]?.GetValue<int>() ?? 1;
        var (result, formattedOutput) = RunCompactQuery(pattern, depth);

        return (result.Nodes.Count, result.Edges.Count, EstimateTokens(formattedOutput));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteSearch(JsonObject args)
    {
        var query = args["query"]?.GetValue<string>() ?? string.Empty;
        var top = args["top"]?.GetValue<int>() ?? 20;

        var results = _engine.Search(query, top);
        var output = string.Join("\n", results.Select(node => $"{node.Kind}: {node.Id}"));
        return (results.Count, 0, EstimateTokens(output));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteList(JsonObject args)
    {
        var scope = args["scope"]?.GetValue<string>() ?? "assemblies";
        var listEngine = new ListEngine(_engine.Nodes, _engine.Edges);

        return scope.ToLowerInvariant() switch
        {
            "assemblies" => ExecuteListAssemblies(listEngine),
            "types" => ExecuteListTypes(listEngine, args),
            _ => throw new NotSupportedException($"Unknown list scope '{scope}'.")
        };
    }

    private static (int Nodes, int Edges, int TokenEstimate) ExecuteListAssemblies(ListEngine listEngine)
    {
        var assemblies = listEngine.ListAssemblies();
        var output = string.Join("\n", assemblies.Select(assembly => $"{assembly.Name}: {assembly.TypeCount} types, {assembly.MethodCount} methods"));
        return (assemblies.Count, 0, EstimateTokens(output));
    }

    private static (int Nodes, int Edges, int TokenEstimate) ExecuteListTypes(ListEngine listEngine, JsonObject args)
    {
        var top = args["top"]?.GetValue<int>() ?? 20;
        var result = listEngine.ListTypes(top: top);
        var output = string.Join("\n", result.Types.Select(type => $"{type.Name}: in={type.InDegree} out={type.OutDegree}"));
        return (result.Types.Count, 0, EstimateTokens(output));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteImpact(JsonObject args)
    {
        var symbol = args["symbol"]?.GetValue<string>() ?? string.Empty;
        var depth = args["depth"]?.GetValue<int>() ?? 3;

        var analyzer = new ImpactAnalyzer(_engine.Nodes, _engine.Edges);
        var result = analyzer.Analyze(symbol, depth);

        var totalNodes = result.Layers.Sum(layer => layer.Nodes.Count);
        var totalEdges = result.Layers.Sum(layer => layer.Edges.Count);
        var output = $"Impact of {symbol}: {result.TotalAffected} affected across {result.Layers.Count} layers";
        return (totalNodes, totalEdges, EstimateTokens(output));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteBatch(JsonObject args)
    {
        var depth = args["depth"]?.GetValue<int>() ?? 1;
        var symbols = ReadSymbols(args);

        var totalNodes = 0;
        var totalEdges = 0;
        var totalTokens = 0;

        foreach (var symbol in symbols)
        {
            var (result, formattedOutput) = RunCompactQuery(symbol, depth);
            totalNodes += result.Nodes.Count;
            totalEdges += result.Edges.Count;
            totalTokens += EstimateTokens(formattedOutput);
        }

        return (totalNodes, totalEdges, totalTokens);
    }

    private (QueryResult Result, string FormattedOutput) RunCompactQuery(string pattern, int depth)
    {
        var options = new QueryOptions
        {
            Pattern = pattern,
            Depth = depth,
            Format = OutputFormat.Compact
        };

        var result = _engine.Query(options);
        var formattedOutput = CompactFormatter.Format(result);
        return (result, formattedOutput);
    }

    private static List<string> ReadSymbols(JsonObject args)
    {
        var symbolsNode = args["symbols"]?.AsArray();
        if (symbolsNode is null)
            return [];

        return symbolsNode
            .Select(symbol => symbol?.GetValue<string>() ?? string.Empty)
            .Where(symbol => symbol.Length > 0)
            .ToList();
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimate: ~4 characters per token
        return (text.Length + 3) / 4;
    }
}
