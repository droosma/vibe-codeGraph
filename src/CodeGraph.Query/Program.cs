using System.Diagnostics;
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

    if (argList.Count == 0)
    {
        Console.Error.WriteLine("Error: symbol pattern is required.");
        PrintUsage();
        return 1;
    }

    var pattern = argList[0];
    argList.RemoveAt(0);

    var depth = GetOption(argList, "--depth", 1);
    var kind = GetOption(argList, "--kind", (string?)null);
    var ns = GetOption(argList, "--namespace", (string?)null);
    var project = GetOption(argList, "--project", (string?)null);
    var format = GetOption(argList, "--format", "context");
    var maxNodes = GetOption(argList, "--max-nodes", 50);
    var includeExternal = HasFlag(argList, "--include-external");
    var rank = !HasFlag(argList, "--no-rank");
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");
    var fromSolution = GetOption(argList, "--from", (string?)null);

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

    // Load graph
    QueryEngine engine;
    try
    {
        engine = await QueryEngine.LoadAsync(graphDir, fromSolution);
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

    var options = new QueryOptions
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
    };

    var result = engine.Query(options);

    if (result.MatchedNodes.Count == 0)
    {
        Console.Error.WriteLine($"No nodes found matching '{pattern}'.");
        return 1;
    }

    // Format output
    var queryDesc = $"{pattern} --depth {depth} --kind {kind ?? "all"}";
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
          --kind <type>        Edge filter: calls-to, calls-from, inherits, implements, depends-on, resolves-to, covers, covered-by, references, overrides, contains, all
          --namespace <filter> Include only nodes in matching namespaces
          --project <filter>   Include only nodes in matching projects
          --format <fmt>       json | text | context (default: context)
          --max-nodes <n>      Cap output size (default: 50)
          --include-external   Include external dependency nodes (default: false)
          --no-rank            Disable result ranking
          --graph-dir <path>   Graph directory (default: .codegraph)
          --from <solution>    Query only the specified solution sub-graph (multi-solution)
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
