using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
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
        int nodeCount = 0;
        int edgeCount = 0;
        int tokenEstimate = 0;

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            var (nodes, edges, tokens) = ExecuteScenario(scenario);
            sw.Stop();
            timings.Add(sw.Elapsed);

            // Capture counts from the last iteration
            nodeCount = nodes;
            edgeCount = edges;
            tokenEstimate = tokens;
        }

        timings.Sort();
        var min = timings[0];
        var max = timings[^1];
        var median = timings[timings.Count / 2];
        var mean = TimeSpan.FromTicks(timings.Sum(t => t.Ticks) / timings.Count);

        return new BenchmarkResult(scenario.Name, iterations, min, max, median, mean,
            nodeCount, edgeCount, tokenEstimate);
    }

    /// <summary>
    /// Run all supplied scenarios and return results for each.
    /// </summary>
    public IReadOnlyList<BenchmarkResult> RunAll(IReadOnlyList<BenchmarkScenario> scenarios, int iterations = 5)
    {
        var results = new List<BenchmarkResult>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            results.Add(RunScenario(scenario, iterations));
        }
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
        var doc = JsonNode.Parse(json) ?? throw new JsonException("Invalid JSON.");
        var scenariosArray = doc["scenarios"]?.AsArray()
            ?? throw new JsonException("Missing 'scenarios' array.");

        var scenarios = new List<BenchmarkScenario>();
        foreach (var item in scenariosArray)
        {
            if (item is null) continue;

            var name = item["name"]?.GetValue<string>()
                ?? throw new JsonException("Scenario missing 'name'.");
            var description = item["description"]?.GetValue<string>() ?? string.Empty;
            var command = item["command"]?.GetValue<string>()
                ?? throw new JsonException($"Scenario '{name}' missing 'command'.");
            var args = item["args"]?.AsObject() ?? new JsonObject();

            scenarios.Add(new BenchmarkScenario(name, description, command, args));
        }

        return scenarios;
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteScenario(BenchmarkScenario scenario)
    {
        return scenario.Command.ToLowerInvariant() switch
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

        var options = new QueryOptions
        {
            Pattern = pattern,
            Depth = depth,
            Format = OutputFormat.Compact
        };

        var result = _engine.Query(options);
        var formatted = CompactFormatter.Format(result);
        return (result.Nodes.Count, result.Edges.Count, EstimateTokens(formatted));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteSearch(JsonObject args)
    {
        var query = args["query"]?.GetValue<string>() ?? string.Empty;
        var top = args["top"]?.GetValue<int>() ?? 20;

        var results = _engine.Search(query, top);
        var outputLines = results.Select(n => $"{n.Kind}: {n.Id}");
        var output = string.Join("\n", outputLines);
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
        var output = string.Join("\n", assemblies.Select(a => $"{a.Name}: {a.TypeCount} types, {a.MethodCount} methods"));
        return (assemblies.Count, 0, EstimateTokens(output));
    }

    private static (int Nodes, int Edges, int TokenEstimate) ExecuteListTypes(ListEngine listEngine, JsonObject args)
    {
        var top = args["top"]?.GetValue<int>() ?? 20;
        var result = listEngine.ListTypes(top: top);
        var output = string.Join("\n", result.Types.Select(t => $"{t.Name}: in={t.InDegree} out={t.OutDegree}"));
        return (result.Types.Count, 0, EstimateTokens(output));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteImpact(JsonObject args)
    {
        var symbol = args["symbol"]?.GetValue<string>() ?? string.Empty;
        var depth = args["depth"]?.GetValue<int>() ?? 3;

        var analyzer = new ImpactAnalyzer(_engine.Nodes, _engine.Edges);
        var result = analyzer.Analyze(symbol, depth);

        var totalNodes = result.Layers.Sum(l => l.Nodes.Count);
        var totalEdges = result.Layers.Sum(l => l.Edges.Count);
        var output = $"Impact of {symbol}: {result.TotalAffected} affected across {result.Layers.Count} layers";
        return (totalNodes, totalEdges, EstimateTokens(output));
    }

    private (int Nodes, int Edges, int TokenEstimate) ExecuteBatch(JsonObject args)
    {
        var symbolsNode = args["symbols"]?.AsArray();
        var depth = args["depth"]?.GetValue<int>() ?? 1;

        var symbols = symbolsNode is not null
            ? symbolsNode.Select(s => s?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList()
            : new List<string>();

        int totalNodes = 0;
        int totalEdges = 0;
        int totalTokens = 0;

        foreach (var symbol in symbols)
        {
            var options = new QueryOptions { Pattern = symbol, Depth = depth, Format = OutputFormat.Compact };
            var result = _engine.Query(options);
            var formatted = CompactFormatter.Format(result);
            totalNodes += result.Nodes.Count;
            totalEdges += result.Edges.Count;
            totalTokens += EstimateTokens(formatted);
        }

        return (totalNodes, totalEdges, totalTokens);
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimate: ~4 characters per token
        return (text.Length + 3) / 4;
    }
}
