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

    // Route subcommands
    if (args[0].Equals("path", StringComparison.OrdinalIgnoreCase))
        return await RunPathAsync(args.Skip(1).ToList());
    if (args[0].Equals("impact", StringComparison.OrdinalIgnoreCase))
        return await RunImpactAsync(args.Skip(1).ToList());
    if (args[0].Equals("explain", StringComparison.OrdinalIgnoreCase))
        return await RunExplainAsync(args.Skip(1).ToList());

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
    var filePath = GetOption(argList, "--file", (string?)null);
    var confidenceStr = GetOption(argList, "--confidence", (string?)null);

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

    // Handle --file query
    if (filePath is not null)
    {
        var fileNodes = engine.FindByFilePath(filePath);
        if (fileNodes.Count == 0)
        {
            Console.Error.WriteLine($"No nodes found in file '{filePath}'.");
            return 1;
        }

        var fileResult = new QueryResult { MatchedNodes = fileNodes };
        var fileOutput = outputFormat switch
        {
            OutputFormat.Json => JsonFormatter.Format(fileResult),
            OutputFormat.Text => TextFormatter.Format(fileResult),
            OutputFormat.Context => ContextFormatter.Format(fileResult, $"file:{filePath}"),
            _ => ContextFormatter.Format(fileResult, $"file:{filePath}")
        };
        Console.WriteLine(fileOutput);
        return 0;
    }

    EdgeConfidence? minConfidence = confidenceStr?.ToLowerInvariant() switch
    {
        "verified" => EdgeConfidence.Verified,
        "inferred" => EdgeConfidence.Inferred,
        "unresolved" => EdgeConfidence.Unresolved,
        null => null,
        _ => null
    };

    if (confidenceStr is not null && minConfidence is null)
    {
        Console.Error.WriteLine($"Error: Invalid confidence value '{confidenceStr}'. Use verified, inferred, or unresolved.");
        return 1;
    }

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
        Format = outputFormat,
        ConfidenceThreshold = minConfidence
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

static async Task<int> RunPathAsync(List<string> argList)
{
    if (argList.Count < 2)
    {
        Console.Error.WriteLine("Usage: codegraph path <from> <to> [--max-depth N] [--graph-dir DIR]");
        return 1;
    }

    var from = argList[0];
    var to = argList[1];
    argList.RemoveRange(0, 2);
    var maxDepth = GetOption(argList, "--max-depth", 10);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;
    try
    {
        (nodes, edges) = await LoadGraphDataAsync(graphDir);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var finder = new PathFinder(nodes, edges);
    var result = finder.FindPath(from, to, maxDepth);
    if (result is null)
    {
        Console.Error.WriteLine($"No path found from '{from}' to '{to}'.");
        return 1;
    }

    Console.WriteLine(PathFormatter.Format(result));
    return 0;
}

static async Task<int> RunImpactAsync(List<string> argList)
{
    if (argList.Count < 1)
    {
        Console.Error.WriteLine("Usage: codegraph impact <symbol> [--depth N] [--graph-dir DIR]");
        return 1;
    }

    var symbol = argList[0];
    argList.RemoveAt(0);
    var depth = GetOption(argList, "--depth", 3);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;
    try
    {
        (nodes, edges) = await LoadGraphDataAsync(graphDir);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var analyzer = new ImpactAnalyzer(nodes, edges);
    var result = analyzer.Analyze(symbol, depth);
    Console.WriteLine(ImpactFormatter.Format(result));
    return 0;
}

static async Task<int> RunExplainAsync(List<string> argList)
{
    if (argList.Count < 1)
    {
        Console.Error.WriteLine("Usage: codegraph explain <symbol> [--graph-dir DIR]");
        return 1;
    }

    var symbol = argList[0];
    argList.RemoveAt(0);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;
    try
    {
        (nodes, edges) = await LoadGraphDataAsync(graphDir);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var explainer = new SymbolExplainer(nodes, edges);
    var result = explainer.Explain(symbol);
    if (result is null)
    {
        Console.Error.WriteLine($"No node found matching '{symbol}'.");
        return 1;
    }

    Console.WriteLine(ExplainFormatter.Format(result));
    return 0;
}

static async Task<(Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges)> LoadGraphDataAsync(string graphDir)
{
    var dbPath = Path.Combine(graphDir, "graph.db");
    if (File.Exists(dbPath))
    {
        var (_, nodes, edges) = await CodeGraph.Core.IO.Sqlite.SqliteGraphReader.ReadAsync(dbPath);
        return (nodes, edges);
    }

    var result = await CodeGraph.Core.IO.GraphReader.ReadAsync(graphDir);
    return (result.Nodes, result.Edges);
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage: codegraph <command> [options]

        Commands:
          query <symbol-pattern>     Query the code graph for symbol relationships
          path <from> <to>           Find shortest path between two symbols
          impact <symbol>            Analyze what depends on a symbol
          explain <symbol>           Comprehensive single-symbol view

        Query options:
          --depth <n>          Traversal depth (default: 1)
          --kind <type>        Edge filter: calls-to, calls-from, inherits, implements, depends-on, resolves-to, covers, covered-by, references, overrides, contains, all
          --namespace <filter> Include only nodes in matching namespaces
          --project <filter>   Include only nodes in matching projects
          --format <fmt>       json | text | context (default: context)
          --max-nodes <n>      Cap output size (default: 50)
          --include-external   Include external dependency nodes (default: false)
          --no-rank            Disable result ranking
          --confidence <level> Minimum confidence: verified, inferred, unresolved (default: all)
          --graph-dir <path>   Graph directory (default: .codegraph)
          --from <solution>    Query only the specified solution sub-graph (multi-solution)
          --file <path>        Query by file path instead of symbol pattern

        Path options:
          --max-depth <n>      Maximum search depth (default: 10)
          --graph-dir <path>   Graph directory (default: .codegraph)

        Impact options:
          --depth <n>          Reverse traversal depth (default: 3)
          --graph-dir <path>   Graph directory (default: .codegraph)

        Explain options:
          --graph-dir <path>   Graph directory (default: .codegraph)
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
