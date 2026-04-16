using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Filters;
using CodeGraph.Query.OutputFormatters;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintUsage();
        return 0;
    }

    // Parse: codegraph query <symbol-pattern> [options]
    // The first arg may be "query" (if invoked as subcommand) or the pattern directly
    var argList = args.ToList();
    if (argList.Count > 0 && argList[0].Equals("query", StringComparison.OrdinalIgnoreCase))
        argList.RemoveAt(0);

    // Parse chaining options before consuming pattern
    var thenSteps = GetRepeatableOption(argList, "--then");
    var fromStdin = HasFlag(argList, "--from-stdin");
    var setOp = GetOption(argList, "--set-op", (string?)null);

    // Pattern is optional when --from-stdin is used without --set-op
    string? pattern = null;
    if (argList.Count > 0 && !argList[0].StartsWith("--"))
    {
        pattern = argList[0];
        argList.RemoveAt(0);
    }

    if (pattern is null && !fromStdin)
    {
        Console.Error.WriteLine("Error: symbol pattern is required.");
        PrintUsage();
        return 1;
    }

    var depth = GetOption(argList, "--depth", 1);
    var kind = GetOption(argList, "--kind", (string?)null);
    var ns = GetOption(argList, "--namespace", (string?)null);
    var project = GetOption(argList, "--project", (string?)null);
    var format = GetOption(argList, "--format", "context");
    var maxNodes = GetOption(argList, "--max-nodes", 50);
    var includeExternal = HasFlag(argList, "--include-external");
    var rank = !HasFlag(argList, "--no-rank");
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

    var outputFormat = format?.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "text" => OutputFormat.Text,
        "context" => OutputFormat.Context,
        _ => OutputFormat.Context
    };

    EdgeType? edgeTypeFilter;
    try
    {
        edgeTypeFilter = EdgeTypeFilter.Parse(kind);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // Parse --then edge types upfront
    var thenEdgeTypes = new List<EdgeType?>();
    foreach (var step in thenSteps)
    {
        try
        {
            thenEdgeTypes.Add(EdgeTypeFilter.Parse(step));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error in --then: {ex.Message}");
            return 1;
        }
    }

    // Load graph
    QueryEngine engine;
    try
    {
        engine = await QueryEngine.LoadAsync(graphDir);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // Staleness check
    CheckStaleness(graphDir);

    QueryResult result;

    if (fromStdin)
    {
        // Read previous result from stdin
        QueryResult stdinResult;
        try
        {
            var json = await Console.In.ReadToEndAsync();
            stdinResult = JsonSerializer.Deserialize<QueryResult>(json, GetStdinJsonOptions())
                ?? throw new InvalidOperationException("Failed to deserialize stdin JSON.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error reading from stdin: {ex.Message}");
            return 1;
        }

        if (setOp is not null && pattern is not null)
        {
            // Run new query and combine with stdin result
            var newResult = engine.Query(new QueryOptions
            {
                Pattern = pattern,
                Depth = depth,
                EdgeTypeFilter = edgeTypeFilter,
                NamespaceFilter = ns,
                ProjectFilter = project,
                MaxNodes = maxNodes,
                IncludeExternal = includeExternal,
                Rank = rank,
                Format = outputFormat
            });

            result = setOp.ToLowerInvariant() switch
            {
                "union" => QueryResult.Union(stdinResult, newResult),
                "intersect" => QueryResult.Intersect(stdinResult, newResult),
                "difference" => QueryResult.Difference(stdinResult, newResult),
                _ => throw new ArgumentException($"Unknown set operation '{setOp}'. Valid values: union, intersect, difference")
            };
        }
        else
        {
            // Use stdin result nodes as seeds for follow-up query
            var seedIds = stdinResult.Nodes.Keys.ToList();
            result = engine.QueryFromResult(seedIds, new QueryOptions
            {
                Depth = depth,
                EdgeTypeFilter = edgeTypeFilter,
                NamespaceFilter = ns,
                ProjectFilter = project,
                MaxNodes = maxNodes,
                IncludeExternal = includeExternal,
                Rank = rank,
                Format = outputFormat
            });
        }
    }
    else
    {
        var options = new QueryOptions
        {
            Pattern = pattern!,
            Depth = depth,
            EdgeTypeFilter = edgeTypeFilter,
            NamespaceFilter = ns,
            ProjectFilter = project,
            MaxNodes = maxNodes,
            IncludeExternal = includeExternal,
            Rank = rank,
            Format = outputFormat
        };

        result = engine.Query(options);
    }

    if (result.MatchedNodes.Count == 0 && result.Nodes.Count == 0)
    {
        Console.Error.WriteLine(pattern is not null
            ? $"No nodes found matching '{pattern}'."
            : "No nodes found from input.");
        return 1;
    }

    // Apply --then chaining steps (graph already loaded, reused across steps)
    foreach (var thenEdgeType in thenEdgeTypes)
    {
        var seedIds = result.Nodes.Keys.ToList();
        result = engine.QueryFromResult(seedIds, new QueryOptions
        {
            Depth = 1,
            EdgeTypeFilter = thenEdgeType,
            NamespaceFilter = ns,
            ProjectFilter = project,
            MaxNodes = maxNodes,
            IncludeExternal = includeExternal,
            Rank = rank,
            Format = outputFormat
        });
    }

    // Format output
    var queryDesc = $"{pattern ?? "from-stdin"} --depth {depth} --kind {kind ?? "all"}";
    if (thenSteps.Count > 0)
        queryDesc += " --then " + string.Join(" --then ", thenSteps);
    if (setOp is not null)
        queryDesc += $" --set-op {setOp}";

    var output = outputFormat switch
    {
        OutputFormat.Json => JsonFormatter.Format(result),
        OutputFormat.Text => TextFormatter.Format(result),
        OutputFormat.Context => ContextFormatter.Format(result, queryDesc),
        _ => ContextFormatter.Format(result, queryDesc)
    };

    Console.WriteLine(output);
    return 0;
}

static void CheckStaleness(string graphDir)
{
    try
    {
        var metaPath = Path.Combine(graphDir, "meta.json");
        if (!File.Exists(metaPath)) return;

        var json = File.ReadAllText(metaPath);
        // Simple extraction of commitHash from JSON
        var commitMatch = System.Text.RegularExpressions.Regex.Match(json, "\"commitHash\"\\s*:\\s*\"([^\"]+)\"");
        if (!commitMatch.Success) return;

        var graphCommit = commitMatch.Groups[1].Value;

        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = Process.Start(psi);
        if (proc is null) return;
        var currentCommit = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit(5000);

        if (!string.IsNullOrEmpty(currentCommit) && !currentCommit.StartsWith(graphCommit, StringComparison.OrdinalIgnoreCase)
            && !graphCommit.StartsWith(currentCommit, StringComparison.OrdinalIgnoreCase))
        {
            var graphShort = graphCommit.Length > 7 ? graphCommit[..7] : graphCommit;
            var currentShort = currentCommit.Length > 7 ? currentCommit[..7] : currentCommit;
            Console.Error.WriteLine($"⚠ Graph is stale (graph: {graphShort}, current: {currentShort}). Run 'codegraph index' to update.");
        }
    }
    catch
    {
        // Ignore staleness check failures
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage: codegraph query <symbol-pattern> [options]

        Options:
          --depth <n>          Traversal depth (default: 1)
          --kind <type>        Edge filter: calls-to, calls-from, inherits, implements, depends-on, resolves-to, covers, all
          --namespace <filter> Include only nodes in matching namespaces
          --project <filter>   Include only nodes in matching projects
          --format <fmt>       json | text | context (default: context)
          --max-nodes <n>      Cap output size (default: 50)
          --include-external   Include external dependency nodes (default: false)
          --no-rank            Disable result ranking
          --graph-dir <path>   Graph directory (default: .codegraph)

        Chaining:
          --then <kind>        Chain: use previous results as seeds, filter by edge kind (repeatable)
          --from-stdin         Read previous QueryResult JSON from stdin as seeds
          --set-op <op>        Combine stdin result with new query: union, intersect, difference

        Examples:
          codegraph query OrderService --then calls-to --then resolves-to
          codegraph query OrderService --format json | codegraph query --from-stdin --kind calls-to
          codegraph query A --format json | codegraph query B --from-stdin --set-op union
        """);
}

static T GetOption<T>(List<string> args, string name, T defaultValue)
{
    var idx = args.IndexOf(name);
    if (idx < 0 || idx + 1 >= args.Count)
        return defaultValue;

    var value = args[idx + 1];
    args.RemoveRange(idx, 2);

    if (typeof(T) == typeof(int))
        return (T)(object)int.Parse(value);
    if (typeof(T) == typeof(string) || typeof(T) == typeof(string))
        return (T)(object)value;

    return defaultValue;
}

static bool HasFlag(List<string> args, string name)
{
    var idx = args.IndexOf(name);
    if (idx < 0) return false;
    args.RemoveAt(idx);
    return true;
}

static List<string> GetRepeatableOption(List<string> args, string name)
{
    var values = new List<string>();
    while (true)
    {
        var idx = args.IndexOf(name);
        if (idx < 0 || idx + 1 >= args.Count) break;
        values.Add(args[idx + 1]);
        args.RemoveRange(idx, 2);
    }
    return values;
}

static JsonSerializerOptions GetStdinJsonOptions() => new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    PropertyNameCaseInsensitive = true
};
