using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Daemon;
using CodeGraph.Indexer.Init;
using CodeGraph.Indexer.Mcp;
using CodeGraph.Indexer.Passes;
using CodeGraph.Indexer.Snapshots;
using CodeGraph.Indexer.Workspace;
using CodeGraph.Query;
using CodeGraph.Query.Benchmarks;
using CodeGraph.Query.Filters;
using CodeGraph.Query.Metrics;
using CodeGraph.Query.OutputFormatters;
using CodeGraph.Query.Report;
using CodeGraph.Query.Wiki;
using CodeGraph.Indexer.View;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    return args[0] switch
    {
        "index" => await RunIndexAsync(args),
        "init" => await RunInitAsync(args),
        "query" => await RunQueryAsync(args),
        "compare" => await RunCompareAsync(args),
        "list" => await RunListAsync(args),
        "search" => await RunSearchAsync(args),
        "explain" => await RunExplainAsync(args),
        "impact" => await RunImpactAsync(args),
        "path" => await RunPathAsync(args),
        "diff" => await RunDiffAsync(args),
        "export" => await RunExportAsync(args),
        "report" => await RunReportAsync(args),
        "stats" => await RunStatsAsync(args),
        "brief" => await RunBriefAsync(args),
        "wiki" => await RunWikiAsync(args),
        "view" => await RunViewAsync(args),
        "test-impact" => await RunTestImpactAsync(args),
        "snapshot" => await RunSnapshotAsync(args),
        "packages" => await RunPackagesAsync(args),
        "benchmark" => await RunBenchmarkAsync(args),
        "daemon" => await RunDaemonAsync(args),
        "mcp" => await RunMcpAsync(args),
        "-h" or "--help" => ShowHelp(),
        _ => ShowUnknown(args[0])
    };
}

static int ShowHelp()
{
    PrintUsage();
    return 0;
}

static int ShowUnknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    PrintUsage();
    return 1;
}

static async Task<int> RunQueryAsync(string[] args)
{
    var argList = args.Skip(1).ToList();

    if (argList.Count == 0 || argList[0] is "-h" or "--help")
    {
        PrintQueryUsage();
        return argList.Count == 0 ? 1 : 0;
    }

    var pattern = argList[0];
    argList.RemoveAt(0);

    var depth = GetOption(argList, "--depth", 1);
    var kind = GetOption(argList, "--kind", (string?)null);
    var ns = GetOption(argList, "--namespace", (string?)null);
    var project = GetOption(argList, "--project", (string?)null);
    var format = GetOption(argList, "--format", (string?)null);
    format ??= IsOutputPiped() ? "compact" : "context";
    var mode = GetOption(argList, "--mode", "all");
    var maxNodes = GetOption(argList, "--max-nodes", 50);
    var includeExternal = HasFlag(argList, "--include-external");
    var includeSource = HasFlag(argList, "--include-source");
    var rank = !HasFlag(argList, "--no-rank");
    var noMetrics = HasFlag(argList, "--no-metrics");
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");
    var fromSolution = GetOption(argList, "--from", (string?)null);
    var budget = GetOption(argList, "--budget", (int?)null);
    var jsonFlag = HasFlag(argList, "--json");
    if (jsonFlag) format = "json";

    var outputFormat = format?.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "text" => OutputFormat.Text,
        "context" => OutputFormat.Context,
        "compact" => OutputFormat.Compact,
        _ => OutputFormat.Context
    };

    var queryMode = mode?.ToLowerInvariant() switch
    {
        "focused" => QueryMode.Focused,
        "structural" => QueryMode.Structural,
        "all" => QueryMode.All,
        _ => QueryMode.All
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
        Format = outputFormat,
        Mode = queryMode,
        Budget = budget
    };

    var result = engine.Query(options);

    if (result.MatchedNodes.Count == 0)
    {
        Console.Error.WriteLine($"No nodes found matching '{pattern}'.");
        return 2;
    }

    var queryDesc = $"{pattern} --depth {depth} --kind {kind ?? "all"}";
    var output = outputFormat switch
    {
        OutputFormat.Json => JsonFormatter.Format(result),
        OutputFormat.Text => TextFormatter.Format(result),
        OutputFormat.Context => ContextFormatter.Format(result, queryDesc, includeSource),
        OutputFormat.Compact => CompactFormatter.Format(result, includeSource),
        _ => ContextFormatter.Format(result, queryDesc, includeSource)
    };

    output = BudgetTruncator.Apply(output, budget);

    if (!noMetrics)
    {
        var metrics = CompressionCalculator.Calculate(result, output);
        output = MetricsFormatter.AppendMetrics(output, metrics);
    }

    Console.WriteLine(output);

    // Emit contextual follow-up suggestions to guide exploration
    var suggestions = QuerySuggestionGenerator.Generate(result, options);
    var hints = QuerySuggestionGenerator.FormatHints(suggestions);
    if (!string.IsNullOrEmpty(hints))
        Console.Error.WriteLine(hints);

    return 0;
}

static async Task<int> RunCompareAsync(string[] args)
{
    var argList = args.Skip(1).ToList();

    if (argList.Count < 2 || HasFlag(argList, "-h") || HasFlag(argList, "--help"))
    {
        PrintCompareUsage();
        return 1;
    }

    var symbolA = argList[0];
    var symbolB = argList[1];
    var depth = GetOption(argList, "--depth", 1);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

    var engine = await QueryEngine.LoadAsync(graphDir);
    var result = engine.Compare(symbolA, symbolB, depth);
    var output = CompareFormatter.Format(result);
    Console.WriteLine(output);
    return 0;
}

static async Task<int> RunMcpAsync(string[] args)
{
    var graphDir = ".codegraph";
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--graph-dir" && i + 1 < args.Length)
            graphDir = args[++i];
    }

    var server = new McpServer(graphDir);
    return await server.RunAsync();
}

static async Task<int> RunListAsync(string[] args)
{
    var argList = args.Skip(1).ToList();

    if (argList.Count > 0 && argList[0] is "-h" or "--help")
    {
        PrintListUsage();
        return 0;
    }

    var scope = argList.Count > 0 ? argList[0] : "assemblies";
    if (argList.Count > 0)
        argList.RemoveAt(0);

    var assemblyFilter = GetOption(argList, "--assembly", (string?)null);
    var top = GetOption(argList, "--top", 50);
    var skip = GetOption(argList, "--skip", 0);
    var filter = GetOption(argList, "--filter", (string?)null);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");
    var json = HasFlag(argList, "--json");

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        if (File.Exists(dbPath))
        {
            var (_, n, e) = await SqliteGraphReader.ReadAsync(dbPath);
            nodes = n;
            edges = e;
        }
        else
        {
            var (_, n, e) = await GraphReader.ReadAsync(graphDir);
            nodes = n;
            edges = e;
        }
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var list = new ListEngine(nodes, edges);

    switch (scope)
    {
        case "assemblies":
            var assemblies = list.ListAssemblies();
            if (assemblies.Count == 0)
            {
                Console.Error.WriteLine("No assemblies found.");
                return 2;
            }
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(assemblies, s_jsonOptions));
            }
            else
            {
                Console.WriteLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
                Console.WriteLine(new string('-', 62));
                foreach (var a in assemblies)
                    Console.WriteLine($"{a.Name,-40} {a.TypeCount,6} {a.MethodCount,8} {a.TotalNodeCount,6}");
            }
            break;

        case "types":
            var result = list.ListTypes(assemblyFilter, top, skip, filter);
            if (result.Types.Count == 0)
            {
                Console.Error.WriteLine("No types found.");
                return 2;
            }
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, s_jsonOptions));
            }
            else
            {
                Console.WriteLine($"{"Type",-50} {"In",4} {"Out",4} {"Assembly",-20}");
                Console.WriteLine(new string('-', 80));
                foreach (var t in result.Types)
                    Console.WriteLine($"{t.Name,-50} {t.InDegree,4} {t.OutDegree,4} {t.Assembly,-20}");
                var endIndex = Math.Min(skip + top, result.TotalCount);
                Console.WriteLine($"\nShowing {skip + 1}-{endIndex} of {result.TotalCount:N0} types. Use --skip {endIndex} for next page.");
            }
            break;

        case "interfaces":
            var ifaces = list.ListInterfaces(assemblyFilter);
            if (ifaces.Count == 0)
            {
                Console.Error.WriteLine("No interfaces found.");
                return 2;
            }
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(ifaces, s_jsonOptions));
            }
            else
            {
                Console.WriteLine($"{"Interface",-50} {"Impls",6} {"Assembly",-20}");
                Console.WriteLine(new string('-', 78));
                foreach (var iface in ifaces)
                    Console.WriteLine($"{iface.Name,-50} {iface.ImplementationCount,6} {iface.Assembly,-20}");
            }
            break;

        case "namespaces":
            var namespaces = list.ListNamespaces(assemblyFilter);
            if (namespaces.Count == 0)
            {
                Console.Error.WriteLine("No namespaces found.");
                return 2;
            }
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(namespaces, s_jsonOptions));
            }
            else
            {
                Console.WriteLine($"{"Namespace",-50} {"Types",6} {"Methods",8}");
                Console.WriteLine(new string('-', 66));
                foreach (var ns in namespaces)
                    Console.WriteLine($"{ns.Name,-50} {ns.TypeCount,6} {ns.MethodCount,8}");
            }
            break;

        default:
            Console.Error.WriteLine($"Unknown list scope: {scope}. Use: assemblies, types, interfaces, namespaces");
            return 1;
    }

    return 0;
}

static async Task<int> RunSearchAsync(string[] args)
{
    var argList = args.Skip(1).ToList();

    if (argList.Count == 0 || argList[0] is "-h" or "--help")
    {
        PrintSearchUsage();
        return argList.Count == 0 ? 1 : 0;
    }

    var query = argList[0];
    argList.RemoveAt(0);

    var top = GetOption(argList, "--top", 20);
    var kindStr = GetOption(argList, "--kind", (string?)null);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");
    var json = HasFlag(argList, "--json");

    NodeKind? kindFilter = kindStr?.ToLowerInvariant() switch
    {
        "type" => NodeKind.Type,
        "method" => NodeKind.Method,
        "namespace" => NodeKind.Namespace,
        "property" => NodeKind.Property,
        "field" => NodeKind.Field,
        _ => null
    };

    try
    {
        var engine = await QueryEngine.LoadAsync(graphDir);
        var results = engine.Search(query, top, kindFilter);

        if (results.Count == 0)
        {
            if (json)
                Console.WriteLine("[]");
            else
                Console.Error.WriteLine($"No results for '{query}'.");
            return json ? 0 : 2;
        }

        if (json)
        {
            var items = results.Select(node => new
            {
                id = node.Id,
                name = node.Name,
                kind = node.Kind.ToString().ToLowerInvariant(),
                @namespace = node.ContainingNamespaceId,
                filePath = node.FilePath,
                startLine = node.StartLine
            });
            Console.WriteLine(JsonSerializer.Serialize(items, s_jsonOptions));
        }
        else
        {
            foreach (var node in results)
            {
                var ns = node.ContainingNamespaceId ?? "";
                var filePart = !string.IsNullOrEmpty(node.FilePath) ? $" ({node.FilePath})" : "";
                Console.WriteLine($"[{node.Kind}] {node.Name}  ns={ns}{filePart}");
            }

            Console.WriteLine($"\n{results.Count} result(s) for '{query}'.");
        }
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    return 0;
}

static async Task<int> RunExplainAsync(string[] args)
{
    var graphDir = ".codegraph";
    var json = false;
    string? symbol = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--json":
                json = true;
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph explain <symbol> [--json] [--graph-dir <dir>]");
                Console.WriteLine();
                Console.WriteLine("Show detailed information about a symbol including members, edges, and test coverage.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --json               Output as JSON");
                Console.WriteLine("  --graph-dir <path>   Graph directory (default: .codegraph)");
                return 0;
            default:
                if (!args[i].StartsWith('-') && symbol is null)
                    symbol = args[i];
                break;
        }
    }

    if (string.IsNullOrEmpty(symbol))
    {
        Console.Error.WriteLine("Error: symbol argument is required.");
        Console.Error.WriteLine("Usage: codegraph explain <symbol> [--json] [--graph-dir <dir>]");
        return 1;
    }

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        Dictionary<string, GraphNode> nodes;
        List<GraphEdge> edges;

        if (File.Exists(dbPath))
        {
            var (_, n, e) = await SqliteGraphReader.ReadAsync(dbPath);
            nodes = n;
            edges = e;
        }
        else
        {
            var (_, n, e) = await GraphReader.ReadAsync(graphDir);
            nodes = n;
            edges = e;
        }

        var explainer = new SymbolExplainer(nodes, edges);
        var result = explainer.Explain(symbol);

        if (result is null)
        {
            Console.Error.WriteLine($"No nodes found matching '{symbol}'.");
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                id = result.Node.Id,
                name = result.Node.Name,
                kind = result.Node.Kind.ToString().ToLowerInvariant(),
                filePath = result.Node.FilePath,
                startLine = result.Node.StartLine,
                endLine = result.Node.EndLine,
                signature = result.Node.Signature,
                docComment = result.Node.DocComment,
                members = result.Members.Select(m => new { id = m.Id, name = m.Name, kind = m.Kind.ToString().ToLowerInvariant() }),
                outgoingEdges = result.OutgoingEdges.Where(e => e.Type != EdgeType.Contains).Select(e => new { type = e.Type.ToString().ToLowerInvariant(), toId = e.ToId }),
                incomingEdges = result.IncomingEdges.Select(e => new { type = e.Type.ToString().ToLowerInvariant(), fromId = e.FromId }),
                tests = result.Tests.Select(t => new { id = t.Id, name = t.Name })
            }, s_jsonOptions));
        }
        else
        {
            Console.Write(ExplainFormatter.Format(result));
        }

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunImpactAsync(string[] args)
{
    var graphDir = ".codegraph";
    var json = false;
    var depth = 3;
    string? symbol = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--depth" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out depth) || depth < 1)
                {
                    Console.Error.WriteLine("Error: --depth must be a positive integer.");
                    return 1;
                }
                break;
            case "--json":
                json = true;
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph impact <symbol> [--depth N] [--json] [--graph-dir <dir>]");
                Console.WriteLine();
                Console.WriteLine("Analyze the blast radius of changes to a symbol.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --depth <n>          Traversal depth (default: 3)");
                Console.WriteLine("  --json               Output as JSON");
                Console.WriteLine("  --graph-dir <path>   Graph directory (default: .codegraph)");
                return 0;
            default:
                if (!args[i].StartsWith('-') && symbol is null)
                    symbol = args[i];
                break;
        }
    }

    if (string.IsNullOrEmpty(symbol))
    {
        Console.Error.WriteLine("Error: symbol argument is required.");
        Console.Error.WriteLine("Usage: codegraph impact <symbol> [--depth N] [--json] [--graph-dir <dir>]");
        return 1;
    }

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        Dictionary<string, GraphNode> nodes;
        List<GraphEdge> edges;

        if (File.Exists(dbPath))
        {
            var (_, n, e) = await SqliteGraphReader.ReadAsync(dbPath);
            nodes = n;
            edges = e;
        }
        else
        {
            var (_, n, e) = await GraphReader.ReadAsync(graphDir);
            nodes = n;
            edges = e;
        }

        var analyzer = new ImpactAnalyzer(nodes, edges);
        var result = analyzer.Analyze(symbol, depth);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pattern = result.Pattern,
                target = result.Target is not null ? new { id = result.Target.Id, kind = result.Target.Kind.ToString().ToLowerInvariant() } : null,
                totalAffected = result.TotalAffected,
                layers = result.Layers.Select(l => new
                {
                    depth = l.Depth,
                    nodes = l.Nodes.Select(n => new { id = n.Id, kind = n.Kind.ToString().ToLowerInvariant(), filePath = n.FilePath, startLine = n.StartLine })
                })
            }, s_jsonOptions));
        }
        else
        {
            Console.Write(ImpactFormatter.Format(result));
        }

        return result.Target is null ? 1 : result.TotalAffected == 0 ? 2 : 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunPathAsync(string[] args)
{
    var graphDir = ".codegraph";
    var json = false;
    var maxDepth = 10;
    string? from = null;
    string? to = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--max-depth" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out maxDepth) || maxDepth < 1)
                {
                    Console.Error.WriteLine("Error: --max-depth must be a positive integer.");
                    return 1;
                }
                break;
            case "--json":
                json = true;
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph path <from> <to> [--max-depth N] [--json] [--graph-dir <dir>]");
                Console.WriteLine();
                Console.WriteLine("Find the shortest path between two symbols in the graph.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --max-depth <n>      Maximum search depth (default: 10)");
                Console.WriteLine("  --json               Output as JSON");
                Console.WriteLine("  --graph-dir <path>   Graph directory (default: .codegraph)");
                return 0;
            default:
                if (!args[i].StartsWith('-'))
                {
                    if (from is null)
                        from = args[i];
                    else if (to is null)
                        to = args[i];
                }
                break;
        }
    }

    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
    {
        Console.Error.WriteLine("Error: both <from> and <to> arguments are required.");
        Console.Error.WriteLine("Usage: codegraph path <from> <to> [--max-depth N] [--json] [--graph-dir <dir>]");
        return 1;
    }

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        Dictionary<string, GraphNode> nodes;
        List<GraphEdge> edges;

        if (File.Exists(dbPath))
        {
            var (_, n, e) = await SqliteGraphReader.ReadAsync(dbPath);
            nodes = n;
            edges = e;
        }
        else
        {
            var (_, n, e) = await GraphReader.ReadAsync(graphDir);
            nodes = n;
            edges = e;
        }

        var finder = new PathFinder(nodes, edges);
        var result = finder.FindPath(from, to, maxDepth);

        if (result is null)
        {
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(new { from, to, found = false, steps = Array.Empty<object>() }, s_jsonOptions));
            else
                Console.Error.WriteLine($"No path found from '{from}' to '{to}'.");
            return 2;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                from = result.FromId,
                to = result.ToId,
                found = true,
                steps = result.Steps.Select(s => new
                {
                    fromId = s.FromId,
                    toId = s.ToId,
                    edgeType = s.Edge.Type.ToString().ToLowerInvariant(),
                    toKind = s.ToNode?.Kind.ToString().ToLowerInvariant(),
                    toFilePath = s.ToNode?.FilePath
                })
            }, s_jsonOptions));
        }
        else
        {
            Console.Write(PathFormatter.Format(result));
        }

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunDiffAsync(string[] args)
{
    var argList = args.Skip(1).ToList();

    if (argList.Count > 0 && argList[0] is "-h" or "--help")
    {
        PrintDiffUsage();
        return 0;
    }

    var hasBaseFlag = argList.Contains("--base");
    var baseGraphDir = GetOption(argList, "--base", ".codegraph-prev");
    var headGraphDir = GetOption(argList, "--head", ".codegraph");
    var gitRef = GetOption(argList, "--ref", (string?)null);
    var only = GetOption(argList, "--only", (string?)null);
    var format = GetOption(argList, "--format", "context");

    if (argList.Count > 0)
    {
        Console.Error.WriteLine($"Unknown argument: {argList[0]}");
        PrintDiffUsage();
        return 1;
    }

    var outputFormat = format?.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "text" => OutputFormat.Text,
        "context" => OutputFormat.Context,
        "compact" => OutputFormat.Compact,
        _ => OutputFormat.Context
    };

    if (!string.IsNullOrWhiteSpace(gitRef) && !hasBaseFlag)
    {
        var resolvedRef = RunGit($"rev-parse {gitRef}", Directory.GetCurrentDirectory());
        var normalizedRef = gitRef.Replace('/', '-').Replace('\\', '-');
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), $".codegraph-{normalizedRef}"),
            !string.IsNullOrEmpty(resolvedRef) ? Path.Combine(Directory.GetCurrentDirectory(), $".codegraph-{(resolvedRef.Length > 7 ? resolvedRef[..7] : resolvedRef)}") : string.Empty
        };

        var foundBase = candidates.FirstOrDefault(path => !string.IsNullOrEmpty(path) && Directory.Exists(path));
        if (string.IsNullOrEmpty(foundBase))
        {
            Console.Error.WriteLine($"Error: Could not locate a graph snapshot for ref '{gitRef}'.");
            Console.Error.WriteLine("Pass --base <graph-dir> explicitly, or store a snapshot as .codegraph-<ref>.");
            return 1;
        }

        baseGraphDir = foundBase;
    }

    try
    {
        var (baseMetadata, baseNodes, baseEdges) = await GraphReader.ReadAsync(baseGraphDir);
        var (headMetadata, headNodes, headEdges) = await GraphReader.ReadAsync(headGraphDir);

        var diff = GraphDiffEngine.Compare(baseMetadata, baseNodes, baseEdges, headMetadata, headNodes, headEdges);
        var filter = ParseDiffOnly(only);
        if (filter.Count > 0)
            diff = ApplyDiffFilter(diff, filter);

        var output = outputFormat switch
        {
            OutputFormat.Json => GraphDiffJsonFormatter.Format(diff),
            OutputFormat.Text => GraphDiffTextFormatter.Format(diff),
            OutputFormat.Context => GraphDiffContextFormatter.Format(diff),
            _ => GraphDiffContextFormatter.Format(diff)
        };

        Console.WriteLine(output);
        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunIndexAsync(string[] args)
{
    string? solutionPath = null;
    string? outputDir = null;
    string? projectFilter = null;
    string? configPath = null;
    string? configuration = null;
    bool verbose = false;
    bool skipBuild = false;
    bool skipRestore = false;
    bool changedOnly = false;
    bool sequential = false;
    string? extendDbPath = null;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--solution" when i + 1 < args.Length:
                solutionPath = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                outputDir = args[++i];
                break;
            case "--projects" when i + 1 < args.Length:
                projectFilter = args[++i];
                break;
            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;
            case "--configuration" when i + 1 < args.Length:
                configuration = args[++i];
                break;
            case "--verbose":
                verbose = true;
                break;
            case "--skip-restore":
                skipRestore = true;
                break;
            case "--skip-build":
                skipBuild = true;
                break;
            case "--changed-only":
                changedOnly = true;
                break;
            case "--sequential":
                sequential = true;
                break;
            case "--extend" when i + 1 < args.Length:
                extendDbPath = args[++i];
                break;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                PrintUsage();
                return 1;
        }
    }

    // Load config, CLI args override
    var config = ConfigLoader.Load(configPath);
    outputDir ??= config.Output;
    configuration ??= config.Index.Configuration;

    // Check for multi-solution support
    var effectiveSolutions = config.GetEffectiveSolutions();

    if (solutionPath is not null)
    {
        // CLI --solution overrides: index just that one solution
        return await RunSingleIndexAsync(solutionPath, outputDir, projectFilter, configuration,
            verbose, skipBuild, skipRestore, changedOnly, config);
    }

    if (effectiveSolutions.Length > 1)
    {
        // Multi-solution indexing
        return await RunMultiSolutionIndexAsync(effectiveSolutions, outputDir!, projectFilter,
            configuration!, verbose, skipBuild, skipRestore, changedOnly, sequential, config, extendDbPath);
    }

    if (effectiveSolutions.Length == 1)
    {
        solutionPath = effectiveSolutions[0].Path;
        return await RunSingleIndexAsync(solutionPath, outputDir, projectFilter, configuration,
            verbose, skipBuild, skipRestore, changedOnly, config);
    }

    Console.Error.WriteLine("Error: --solution is required (or set 'solution' or 'solutions' in codegraph.json).");
    return 1;
}

static async Task<int> RunMultiSolutionIndexAsync(
    SolutionEntry[] solutions, string outputDir, string? projectFilter,
    string configuration, bool verbose, bool skipBuild, bool skipRestore,
    bool changedOnly, bool sequential, CodeGraphConfig config, string? extendDbPath)
{
    Console.WriteLine($"Multi-solution index: {solutions.Length} solutions");
    var errors = new ConcurrentBag<string>();

    async Task IndexSolution(SolutionEntry entry)
    {
        var slnName = Path.GetFileNameWithoutExtension(entry.Path);
        var subOutputDir = Path.Combine(Path.GetFullPath(outputDir), slnName);

        if (verbose) Console.WriteLine($"  Indexing solution: {entry.Path} → {subOutputDir}");

        var result = await RunSingleIndexAsync(
            entry.Path, subOutputDir, projectFilter, configuration,
            verbose, skipBuild, skipRestore, changedOnly, config);

        if (result != 0)
            errors.Add(entry.Path);
    }

    if (sequential)
    {
        foreach (var entry in solutions)
            await IndexSolution(entry);
    }
    else
    {
        var tasks = solutions.Select(IndexSolution).ToArray();
        await Task.WhenAll(tasks);
    }

    if (errors.Count > 0)
    {
        Console.Error.WriteLine($"Errors indexing {errors.Count} solution(s): {string.Join(", ", errors)}");
        return 1;
    }

    // Merge per-solution graphs into a unified DB
    var unifiedDbPath = extendDbPath ?? Path.Combine(Path.GetFullPath(outputDir), "graph.db");
    await MergeMultiSolutionGraphsAsync(solutions, outputDir, unifiedDbPath, verbose);

    Console.WriteLine($"Multi-solution index complete: {solutions.Length} solutions indexed to {outputDir}");
    return 0;
}

// Reads each per-solution graph, appends them into a unified database, then
// runs CrossSolutionLinker to resolve external edges across solutions.
static async Task MergeMultiSolutionGraphsAsync(
    SolutionEntry[] solutions, string outputDir, string unifiedDbPath, bool verbose)
{
    var writer = new SqliteGraphWriter();
    var allNodes = new Dictionary<string, GraphNode>();
    var allEdges = new List<GraphEdge>();

    foreach (var entry in solutions)
    {
        var slnName = Path.GetFileNameWithoutExtension(entry.Path);
        var subDir = Path.Combine(Path.GetFullPath(outputDir), slnName);
        var subDbPath = Path.Combine(subDir, "graph.db");

        if (!File.Exists(subDbPath))
        {
            if (verbose) Console.WriteLine($"  Skipping merge for {slnName}: no graph.db found");
            continue;
        }

        if (verbose) Console.WriteLine($"  Merging {slnName} into unified graph");

        var (_, nodes, edges) = await SqliteGraphReader.ReadAsync(subDbPath);

        foreach (var kvp in nodes)
            allNodes[kvp.Key] = kvp.Value;
        allEdges.AddRange(edges);

        await writer.AppendAsync(unifiedDbPath, nodes.Values, edges, slnName);
    }

    // Run cross-solution linking
    var (crossEdges, resolvedIds) = CrossSolutionLinker.Link(allNodes, allEdges);
    if (crossEdges.Count > 0)
    {
        if (verbose)
            Console.WriteLine($"  Cross-solution linker: {crossEdges.Count} edges resolved, " +
                              $"{resolvedIds.Count} external IDs matched");

        // Append the cross-solution edges under a synthetic solution name
        await writer.AppendAsync(
            unifiedDbPath,
            Array.Empty<GraphNode>(),
            crossEdges,
            "__cross_solution__");
    }
}

static async Task<int> RunSingleIndexAsync(
    string solutionPath, string? outputDir, string? projectFilter,
    string? configuration, bool verbose, bool skipBuild, bool skipRestore,
    bool changedOnly, CodeGraphConfig config)
{
    if (string.IsNullOrEmpty(solutionPath))
    {
        Console.Error.WriteLine("Error: --solution is required (or set 'solution' or 'solutions' in codegraph.json).");
        return 1;
    }

    solutionPath = Path.GetFullPath(solutionPath);
    outputDir = Path.GetFullPath(outputDir ?? ".codegraph");
    var solutionRoot = Path.GetDirectoryName(solutionPath)!;

    if (verbose) Console.WriteLine($"Solution: {solutionPath}");
    if (verbose) Console.WriteLine($"Output:   {outputDir}");

    // --changed-only: determine which projects have changes
    HashSet<string>? changedProjects = null;
    Dictionary<string, GraphNode>? existingNodes = null;
    List<GraphEdge>? existingEdges = null;
    GraphMetadata? existingMetadata = null;

    if (changedOnly)
    {
        var metaPath = Path.Combine(outputDir, "meta.json");
        if (!File.Exists(metaPath))
        {
            if (verbose) Console.WriteLine("No existing graph found; falling back to full index.");
            changedOnly = false;
        }
        else
        {
            (existingMetadata, existingNodes, existingEdges) = await GraphReader.ReadAsync(outputDir);

            var lastCommit = existingMetadata.CommitHash;
            if (string.IsNullOrEmpty(lastCommit))
            {
                if (verbose) Console.WriteLine("No commit hash in existing graph; falling back to full index.");
                changedOnly = false;
            }
            else
            {
                var diffOutput = RunGit($"diff --name-only {lastCommit} HEAD -- \"*.cs\" \"*.csproj\"", solutionRoot);
                if (string.IsNullOrEmpty(diffOutput))
                {
                    Console.WriteLine("No changes detected since last index.");
                    return 0;
                }

                var changedFiles = diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (verbose) Console.WriteLine($"Changed files: {changedFiles.Length}");

                changedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in changedFiles)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(solutionRoot, file.Replace('/', Path.DirectorySeparatorChar)));
                    var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;

                    // Walk up from the changed file to find which project directory contains it
                    var current = new DirectoryInfo(dir);
                    while (current is not null && current.FullName.Length >= solutionRoot.Length)
                    {
                        var csprojFiles = current.GetFiles("*.csproj");
                        if (csprojFiles.Length > 0)
                        {
                            changedProjects.Add(Path.GetFileNameWithoutExtension(csprojFiles[0].Name));
                            break;
                        }
                        current = current.Parent;
                    }
                }

                if (changedProjects.Count == 0)
                {
                    Console.WriteLine("No changes detected since last index.");
                    return 0;
                }

                if (verbose)
                {
                    Console.WriteLine($"Changed projects: {string.Join(", ", changedProjects)}");
                }
            }
        }
    }

    // Load compilations
    var loader = new HybridWorkspaceLoader();
    var compilations = await loader.LoadAsync(
        solutionPath,
        skipRestore: skipBuild || skipRestore,
        configuration: configuration ?? "Debug",
        preprocessorSymbols: config.Index.PreprocessorSymbols.Length > 0 ? config.Index.PreprocessorSymbols : null);

    // Filter projects
    var filtered = compilations.AsEnumerable();
    if (!string.IsNullOrEmpty(projectFilter))
    {
        filtered = filtered.Where(p => WildcardMatch(p.ProjectName, projectFilter));
    }
    else if (config.Index.IncludeProjects.Length > 0 &&
             !(config.Index.IncludeProjects.Length == 1 && config.Index.IncludeProjects[0] == "*"))
    {
        filtered = filtered.Where(p =>
            config.Index.IncludeProjects.Any(pat => WildcardMatch(p.ProjectName, pat)));
    }

    if (config.Index.ExcludeProjects.Length > 0)
    {
        filtered = filtered.Where(p =>
            !config.Index.ExcludeProjects.Any(pat => WildcardMatch(p.ProjectName, pat)));
    }

    // Further filter to changed projects when --changed-only is active
    if (changedOnly && changedProjects is not null)
    {
        filtered = filtered.Where(p => changedProjects.Contains(p.ProjectName));
    }

    var projects = filtered.ToList();
    if (verbose) Console.WriteLine($"Projects: {projects.Count}");

    // Run passes (parallelized per project)
    var projectResults = new ConcurrentBag<(List<GraphNode> Nodes, List<GraphEdge> Edges)>();
    var passOptions = new PassPipelineOptions(
        EnableRoutesPass: true,
        EnableConfigurationPass: true,
        EnableMiddlewarePass: true,
        EnableDbContextPass: true);

    Parallel.ForEach(projects, project =>
    {
        if (verbose) Console.WriteLine($"  Indexing {project.ProjectName}...");

        try
        {
            projectResults.Add(PassPipelineRunner.Execute(project, solutionRoot, passOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Error indexing {project.ProjectName}: {ex.Message}");
        }
    });

    var allNodes = new List<GraphNode>();
    var allEdges = new List<GraphEdge>();
    foreach (var (nodes, edges) in projectResults)
    {
        allNodes.AddRange(nodes);
        allEdges.AddRange(edges);
    }

    // Build metadata
    var commitHash = RunGit("rev-parse HEAD", solutionRoot);
    var branch = RunGit("rev-parse --abbrev-ref HEAD", solutionRoot);
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    // Merge with existing graph when --changed-only
    if (changedOnly && existingNodes is not null && existingEdges is not null)
    {
        var splitStrategy = config.SplitBy.Equals("namespace", StringComparison.OrdinalIgnoreCase)
            ? SplitFileStrategy.ByNamespace
            : SplitFileStrategy.ByAssembly;

        var partialGraphs = GroupIntoProjectGraphs(allNodes, allEdges, splitStrategy);

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, partialGraphs);

        var mergedNodeList = mergedNodes.Values.ToList();
        var allProjectsIndexed = existingMetadata!.ProjectsIndexed
            .Union(projects.Select(p => p.ProjectName))
            .Distinct()
            .ToArray();

        var metadata = new GraphMetadata
        {
            CommitHash = commitHash,
            Branch = branch,
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = version,
            Solution = Path.GetFileName(solutionPath),
            SolutionName = Path.GetFileNameWithoutExtension(solutionPath),
            ProjectsIndexed = allProjectsIndexed,
            Stats = new Dictionary<string, int>
            {
                ["node_count"] = mergedNodeList.Count,
                ["edge_count"] = mergedEdges.Count,
                ["type_count"] = mergedNodeList.Count(n => n.Kind == NodeKind.Type),
                ["method_count"] = mergedNodeList.Count(n => n.Kind is NodeKind.Method or NodeKind.Constructor)
            }
        };

        var writer = new GraphWriter(splitStrategy);
        await writer.WriteAsync(outputDir, mergedNodeList, mergedEdges, metadata);

        var sqliteWriter = new SqliteGraphWriter();
        var dbPath = Path.Combine(outputDir, "graph.db");
        await sqliteWriter.WriteAsync(dbPath, mergedNodeList, mergedEdges, metadata);

        Console.WriteLine($"CodeGraph incremental index complete.");
        Console.WriteLine($"  Changed projects: {projects.Count}");
        Console.WriteLine($"  Total nodes:      {mergedNodeList.Count}");
        Console.WriteLine($"  Total edges:      {mergedEdges.Count}");
        Console.WriteLine($"  Output:           {outputDir}");

        return 0;
    }

    var fullMetadata = new GraphMetadata
    {
        CommitHash = commitHash,
        Branch = branch,
        GeneratedAt = DateTimeOffset.UtcNow,
        IndexerVersion = version,
        Solution = Path.GetFileName(solutionPath),
        SolutionName = Path.GetFileNameWithoutExtension(solutionPath),
        ProjectsIndexed = projects.Select(p => p.ProjectName).ToArray(),
        Stats = new Dictionary<string, int>
        {
            ["node_count"] = allNodes.Count,
            ["edge_count"] = allEdges.Count,
            ["type_count"] = allNodes.Count(n => n.Kind == NodeKind.Type),
            ["method_count"] = allNodes.Count(n => n.Kind is NodeKind.Method or NodeKind.Constructor)
        }
    };

    // Write output
    {
        var splitStrategy = config.SplitBy.Equals("namespace", StringComparison.OrdinalIgnoreCase)
            ? SplitFileStrategy.ByNamespace
            : SplitFileStrategy.ByAssembly;
        var writer = new GraphWriter(splitStrategy);
        await writer.WriteAsync(outputDir, allNodes, allEdges, fullMetadata);

        var sqliteWriter = new SqliteGraphWriter();
        var dbPath = Path.Combine(outputDir, "graph.db");
        await sqliteWriter.WriteAsync(dbPath, allNodes, allEdges, fullMetadata);
    }

    // Summary
    Console.WriteLine($"CodeGraph index complete.");
    Console.WriteLine($"  Projects: {projects.Count}");
    Console.WriteLine($"  Nodes:    {allNodes.Count}");
    Console.WriteLine($"  Edges:    {allEdges.Count}");
    Console.WriteLine($"  Types:    {fullMetadata.Stats["type_count"]}");
    Console.WriteLine($"  Methods:  {fullMetadata.Stats["method_count"]}");
    Console.WriteLine($"  Output:   {outputDir}");

    return 0;
}

static async Task<int> RunReportAsync(string[] args)
{
    var graphDir = ".codegraph";
    var outputPath = (string?)null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--output" or "-o" when i + 1 < args.Length:
                outputPath = args[++i];
                break;
            case "-h" or "--help":
                PrintReportUsage();
                return 0;
        }
    }

    var dbPath = Path.Combine(graphDir, "graph.db");
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Graph database not found at {dbPath}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
    var edgeList = edges.ToList();

    var report = ReportGenerator.Generate(nodes, edgeList, metadata);

    outputPath ??= Path.Combine(graphDir, "REPORT.md");
    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(outputPath, report);

    Console.WriteLine($"Report written to {outputPath}");
    return 0;
}

static async Task<int> RunBriefAsync(string[] args)
{
    var graphDir = ".codegraph";

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "-h" or "--help":
                PrintBriefUsage();
                return 0;
        }
    }

    var dbPath = Path.Combine(graphDir, "graph.db");
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Graph database not found at {dbPath}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
    var edgeList = edges.ToList();

    var brief = BriefGenerator.Generate(metadata, nodes, edgeList);

    var outputPath = Path.Combine(graphDir, "BRIEF.md");
    Directory.CreateDirectory(graphDir);
    await File.WriteAllTextAsync(outputPath, brief);

    Console.WriteLine(brief);
    Console.WriteLine();
    Console.WriteLine($"Brief written to {outputPath}");
    return 0;
}

static void PrintBriefUsage()
{
    Console.WriteLine("""
        Usage: codegraph brief [options]

        Generates a compact BRIEF.md codebase orientation file optimized for
        LLM agents to read as their first action.

        Options:
          --graph-dir <dir>    Directory containing graph.db (default: .codegraph)
        """);
}

static async Task<int> RunStatsAsync(string[] args)
{
    var graphDir = ".codegraph";

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "-h" or "--help":
                PrintStatsUsage();
                return 0;
        }
    }

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        if (File.Exists(dbPath))
        {
            var (_, n, e) = await SqliteGraphReader.ReadAsync(dbPath);
            nodes = n;
            edges = e;
        }
        else
        {
            var (_, n, e) = await GraphReader.ReadAsync(graphDir);
            nodes = n;
            edges = e;
        }
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    Console.WriteLine("CodeGraph Statistics");
    Console.WriteLine($"  Total nodes: {nodes.Count}");
    Console.WriteLine($"  Total edges: {edges.Count}");
    Console.WriteLine();
    Console.WriteLine("Nodes by kind:");
    foreach (var group in nodes.Values.GroupBy(n => n.Kind).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    Console.WriteLine();
    Console.WriteLine("Edges by type:");
    foreach (var group in edges.GroupBy(e => e.Type).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Key}: {group.Count()}");

    return 0;
}

static async Task<int> RunWikiAsync(string[] args)
{
    var graphDir = ".codegraph";
    var outputDir = (string?)null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--output" or "-o" when i + 1 < args.Length:
                outputDir = args[++i];
                break;
            case "-h" or "--help":
                PrintWikiUsage();
                return 0;
        }
    }

    outputDir ??= Path.Combine(graphDir, "wiki");

    Dictionary<string, GraphNode> nodes;
    List<GraphEdge> edges;
    GraphMetadata metadata;

    try
    {
        var dbPath = Path.Combine(graphDir, "graph.db");
        if (File.Exists(dbPath))
        {
            (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
        }
        else
        {
            (metadata, nodes, edges) = await GraphReader.ReadAsync(graphDir);
        }
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    WikiGenerator.Generate(outputDir, nodes, edges, metadata);

    Console.WriteLine($"Wiki generated at {outputDir}/");
    return 0;
}

static async Task<int> RunExportAsync(string[] args)
{
    var graphDir = ".codegraph";
    var outputDir = "export";

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                outputDir = args[++i];
                break;
            case "-h" or "--help":
                PrintExportUsage();
                return 0;
        }
    }

    var dbPath = Path.Combine(graphDir, "graph.db");
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Graph database not found at {dbPath}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
    var edgeList = edges.ToList();

    var writer = new GraphWriter();
    await writer.WriteAsync(outputDir, nodes.Values, edgeList, metadata);

    Console.WriteLine($"Exported {nodes.Count} nodes and {edgeList.Count} edges to {outputDir}/");
    return 0;
}

static async Task<int> RunTestImpactAsync(string[] args)
{
    var graphDir = ".codegraph";
    string? symbol = null;
    var depth = 3;
    var json = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--depth" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out depth) || depth < 1)
                {
                    Console.Error.WriteLine("Error: --depth must be a positive integer.");
                    return 1;
                }
                break;
            case "--json":
                json = true;
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph test-impact <symbol> [--depth N] [--json] [--graph-dir <dir>]");
                Console.WriteLine();
                Console.WriteLine("Analyze test coverage for a symbol, showing direct and indirect tests.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --depth <n>          Traversal depth for indirect coverage (default: 3)");
                Console.WriteLine("  --json               Output as JSON");
                Console.WriteLine("  --graph-dir <path>   Graph directory (default: .codegraph)");
                return 0;
            default:
                if (!args[i].StartsWith("-") && symbol is null)
                    symbol = args[i];
                break;
        }
    }

    if (string.IsNullOrEmpty(symbol))
    {
        Console.Error.WriteLine("Error: symbol argument is required.");
        Console.Error.WriteLine("Usage: codegraph test-impact <symbol> [--depth N] [--json] [--graph-dir <dir>]");
        return 1;
    }

    try
    {
        var (_, nodes, edges) = await GraphReader.ReadAsync(graphDir);
        var analyzer = new TestImpactAnalyzer(nodes, edges);
        var result = analyzer.Analyze(symbol, depth);

        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pattern = result.Pattern,
                target = result.Target is not null ? new { id = result.Target.Id, kind = result.Target.Kind.ToString().ToLowerInvariant() } : null,
                directTests = result.DirectTests.Select(t => new { testId = t.TestNode.Id, path = t.PathFromTarget.Select(n => n.Id).ToList() }),
                indirectTests = result.IndirectTests.Select(t => new { testId = t.TestNode.Id, path = t.PathFromTarget.Select(n => n.Id).ToList() }),
                uncoveredCallers = result.UncoveredCallers.Select(c => new { callerId = c.Caller.Id, depth = c.Depth }),
                suggestedTestCommand = result.SuggestedTestCommand
            }, s_jsonOptions));
        else
            Console.Write(TestImpactFormatter.Format(result));

        if (result.Target is null || (result.DirectTests.Count == 0 && result.IndirectTests.Count == 0))
            return 2;

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunSnapshotAsync(string[] args)
{
    var graphDir = ".codegraph";

    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: codegraph snapshot <save|list|delete> [name] [--graph-dir <dir>]");
        return 1;
    }

    var subCommand = args[1];
    string? name = null;

    for (var i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            default:
                if (!args[i].StartsWith("-") && name is null)
                    name = args[i];
                break;
        }
    }

    var manager = new SnapshotManager(graphDir);

    switch (subCommand)
    {
        case "save":
            if (string.IsNullOrEmpty(name))
            {
                Console.Error.WriteLine("Error: snapshot name is required.");
                Console.Error.WriteLine("Usage: codegraph snapshot save <name> [--graph-dir <dir>]");
                return 1;
            }
            await manager.SaveAsync(graphDir, name);
            Console.WriteLine($"Snapshot '{name}' saved.");
            return 0;

        case "list":
            var snapshots = manager.List();
            if (snapshots.Count == 0)
            {
                Console.WriteLine("No snapshots found.");
            }
            else
            {
                foreach (var s in snapshots)
                    Console.WriteLine($"  {s.Name,-20} {s.CreatedAt:yyyy-MM-dd HH:mm:ss}  {s.Path}");
            }
            return 0;

        case "delete":
            if (string.IsNullOrEmpty(name))
            {
                Console.Error.WriteLine("Error: snapshot name is required.");
                Console.Error.WriteLine("Usage: codegraph snapshot delete <name> [--graph-dir <dir>]");
                return 1;
            }
            if (manager.Delete(name))
                Console.WriteLine($"Snapshot '{name}' deleted.");
            else
                Console.Error.WriteLine($"Snapshot '{name}' not found.");
            return 0;

        default:
            Console.Error.WriteLine($"Unknown snapshot command: {subCommand}");
            Console.Error.WriteLine("Usage: codegraph snapshot <save|list|delete> [name]");
            return 1;
    }
}

static async Task<int> RunPackagesAsync(string[] args)
{
    var graphDir = ".codegraph";
    string? projectFilter = null;
    string? packageFilter = null;
    var formatJson = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--project" when i + 1 < args.Length:
                projectFilter = args[++i];
                break;
            case "--package" when i + 1 < args.Length:
                packageFilter = args[++i];
                break;
            case "--format" when i + 1 < args.Length:
                formatJson = args[++i].Equals("json", StringComparison.OrdinalIgnoreCase);
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph packages [--project <name>] [--package <name>] [--format json] [--graph-dir <dir>]");
                return 0;
        }
    }

    try
    {
        var (_, nodes, edges) = await GraphReader.ReadAsync(graphDir);
        var analyzer = new PackageAnalyzer(nodes, edges);

        if (!string.IsNullOrEmpty(packageFilter))
        {
            var usages = analyzer.AnalyzeByPackage(packageFilter);
            Console.Write(formatJson ? PackageFormatter.FormatUsageAsJson(usages) : PackageFormatter.FormatUsage(usages));
        }
        else
        {
            var usages = analyzer.AnalyzeByProject(projectFilter);
            Console.Write(formatJson ? PackageFormatter.FormatUsageAsJson(usages) : PackageFormatter.FormatUsage(usages));

            var conflicts = analyzer.FindConflicts();
            if (conflicts.Count > 0)
            {
                Console.WriteLine();
                Console.Write(formatJson ? PackageFormatter.FormatConflictsAsJson(conflicts) : PackageFormatter.FormatConflicts(conflicts));
            }
        }

        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunBenchmarkAsync(string[] args)
{
    var graphDir = ".codegraph";
    string? scenariosPath = null;
    var iterations = 5;
    var formatJson = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--scenarios" when i + 1 < args.Length:
                scenariosPath = args[++i];
                break;
            case "--iterations" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out iterations) || iterations < 1)
                {
                    Console.Error.WriteLine("Error: --iterations must be a positive integer.");
                    return 1;
                }
                break;
            case "--format" when i + 1 < args.Length:
                formatJson = args[++i].Equals("json", StringComparison.OrdinalIgnoreCase);
                break;
            case "-h" or "--help":
                Console.WriteLine("Usage: codegraph benchmark [--scenarios <path>] [--iterations N] [--format json] [--graph-dir <dir>]");
                return 0;
        }
    }

    try
    {
        var engine = await QueryEngine.LoadAsync(graphDir);
        var runner = new BenchmarkRunner(engine);

        IReadOnlyList<BenchmarkScenario> scenarios;
        if (!string.IsNullOrEmpty(scenariosPath))
        {
            var json = await File.ReadAllTextAsync(scenariosPath);
            scenarios = BenchmarkRunner.LoadScenarios(json);
        }
        else
        {
            var defaultPath = Path.Combine(AppContext.BaseDirectory, "benchmarks", "scenarios.json");
            if (File.Exists(defaultPath))
            {
                var json = await File.ReadAllTextAsync(defaultPath);
                scenarios = BenchmarkRunner.LoadScenarios(json);
            }
            else
            {
                scenarios = BenchmarkRunner.LoadScenarios("""
                {
                  "scenarios": [
                    { "name": "query-single", "description": "Single type query", "command": "query", "args": { "pattern": "*", "depth": 1 } },
                    { "name": "search-broad", "description": "Broad search", "command": "search", "args": { "query": "Service", "top": 50 } },
                    { "name": "list-assemblies", "description": "List assemblies", "command": "list", "args": { "scope": "assemblies" } }
                  ]
                }
                """);
            }
        }

        var results = runner.RunAll(scenarios, iterations);
        Console.Write(formatJson ? BenchmarkFormatter.FormatJson(results) : BenchmarkFormatter.FormatMarkdown(results));
        return 0;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }
}

static async Task<int> RunDaemonAsync(string[] args)
{
    var graphDir = ".codegraph";

    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: codegraph daemon <start|stop|status> [--graph-dir <dir>]");
        return 1;
    }

    var subCommand = args[1];

    for (var i = 2; i < args.Length; i++)
    {
        if (args[i] == "--graph-dir" && i + 1 < args.Length)
            graphDir = args[++i];
    }

    graphDir = Path.GetFullPath(graphDir);

    switch (subCommand)
    {
        case "start":
            if (PidFile.IsProcessRunning(graphDir))
            {
                Console.WriteLine("Daemon is already running.");
                return 0;
            }
            Console.WriteLine($"Starting daemon for {graphDir}...");
            Console.WriteLine($"Pipe: {DaemonServer.GetPipeName(graphDir)}");
            var server = new DaemonServer(graphDir);
            PidFile.Write(graphDir, Environment.ProcessId);
            try
            {
                await server.StartAsync();
            }
            finally
            {
                PidFile.Delete(graphDir);
            }
            return 0;

        case "stop":
            var pid = PidFile.Read(graphDir);
            if (pid is null)
            {
                Console.Error.WriteLine("No daemon is running.");
                return 1;
            }
            try
            {
                var proc = Process.GetProcessById(pid.Value);
                proc.Kill();
                Console.WriteLine($"Daemon (PID {pid.Value}) stopped.");
            }
            catch
            {
                Console.WriteLine("Daemon process not found (may have already exited).");
            }
            PidFile.Delete(graphDir);
            return 0;

        case "status":
            if (PidFile.IsProcessRunning(graphDir))
            {
                var daemonPid = PidFile.Read(graphDir);
                Console.WriteLine($"Daemon is running (PID {daemonPid}).");
                Console.WriteLine($"Pipe: {DaemonServer.GetPipeName(graphDir)}");
            }
            else
            {
                Console.WriteLine("No daemon is running.");
            }
            return 0;

        default:
            Console.Error.WriteLine($"Unknown daemon command: {subCommand}");
            Console.Error.WriteLine("Usage: codegraph daemon <start|stop|status>");
            return 1;
    }
}

static async Task<int> RunViewAsync(string[] args)
{
    var graphDir = ".codegraph";
    string? outputPath = null;
    var maxNodes = 5000;
    var noOpen = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                outputPath = args[++i];
                break;
            case "--max-nodes" when i + 1 < args.Length:
                if (!int.TryParse(args[++i], out maxNodes) || maxNodes < 1)
                {
                    Console.Error.WriteLine("Error: --max-nodes must be a positive integer.");
                    return 1;
                }
                break;
            case "--no-open":
                noOpen = true;
                break;
            case "-h" or "--help":
                PrintViewUsage();
                return 0;
        }
    }

    string html;
    try
    {
        var generator = new HtmlGraphGenerator(graphDir, maxNodes);
        html = await generator.GenerateAsync();
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine("Run 'codegraph index' to generate the graph first.");
        return 1;
    }

    string filePath;
    if (outputPath is not null)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        filePath = outputPath;
    }
    else
    {
        filePath = Path.Combine(Path.GetTempPath(), $"codegraph-{Guid.NewGuid():N}.html");
    }

    await File.WriteAllTextAsync(filePath, html);
    Console.WriteLine($"Graph visualization written to {filePath}");

    if (!noOpen)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            Console.WriteLine("Could not open browser automatically. Open the file manually.");
        }
    }

    return 0;
}

static async Task<int> RunInitAsync(string[] args)
{
    string? outputDir = null;
    string? agentFlag = null;
    string? solutionFlag = null;
    bool force = false;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--output" when i + 1 < args.Length:
                outputDir = args[++i];
                break;
            case "--agent" when i + 1 < args.Length:
                agentFlag = args[++i];
                break;
            case "--solution" when i + 1 < args.Length:
                solutionFlag = args[++i];
                break;
            case "--force":
                force = true;
                break;
            case "-h" or "--help":
                PrintInitUsage();
                return 0;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                PrintInitUsage();
                return 1;
        }
    }

    var currentDir = Directory.GetCurrentDirectory();

    // --- Step 0: codegraph.json + MCP configs ---
    // Auto-discover .sln/.slnx files recursively (excluding common build/dependency directories)
    var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages", "artifacts" };

    var slnFiles = Directory.GetFiles(currentDir, "*.sln", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(currentDir, "*.slnx", SearchOption.AllDirectories))
        .Where(f =>
        {
            var relativePath = Path.GetRelativePath(currentDir, f);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !parts.Any(p => excludedDirs.Contains(p));
        })
        .ToArray();

    if (solutionFlag is not null)
    {
        // Single solution specified via flag — use singular format for backward compat
        var config = new CodeGraphConfig
        {
            Solution = solutionFlag,
            Output = outputDir ?? ".codegraph"
        };

        var configPath = Path.Combine(currentDir, ConfigLoader.DefaultFileName);
        await ConfigLoader.SaveAsync(config, configPath);
        Console.WriteLine($"Created {ConfigLoader.DefaultFileName}");
    }
    else if (slnFiles.Length == 1)
    {
        var foundSln = Path.GetRelativePath(currentDir, slnFiles[0]);
        Console.WriteLine($"Found solution: {foundSln}");

        var config = new CodeGraphConfig
        {
            Solution = foundSln,
            Output = outputDir ?? ".codegraph"
        };

        var configPath = Path.Combine(currentDir, ConfigLoader.DefaultFileName);
        await ConfigLoader.SaveAsync(config, configPath);
        Console.WriteLine($"Created {ConfigLoader.DefaultFileName}");
    }
    else if (slnFiles.Length > 1)
    {
        // Prefer root-level solutions over nested ones
        var rootSlnFiles = slnFiles
            .Where(f => Path.GetDirectoryName(f) == currentDir)
            .ToArray();

        string[] selectedSlnFiles;
        string selectionMessage;

        if (rootSlnFiles.Length == 1)
        {
            // Exactly one root-level solution — auto-select it
            selectedSlnFiles = rootSlnFiles;
            var rootRelPath = Path.GetRelativePath(currentDir, rootSlnFiles[0]);
            selectionMessage = $"Found {slnFiles.Length} solutions, selected 1 root-level solution: {rootRelPath}";
        }
        else if (rootSlnFiles.Length > 1)
        {
            // Multiple root-level solutions — use only those
            selectedSlnFiles = rootSlnFiles;
            selectionMessage = $"Found {slnFiles.Length} solutions, selected {rootSlnFiles.Length} root-level solutions";
        }
        else
        {
            // No root-level solutions — fall back to all discovered
            selectedSlnFiles = slnFiles;
            selectionMessage = $"Found {slnFiles.Length} solutions (none at root level)";
        }

        Console.WriteLine(selectionMessage);

        // Deduplicate by name: if two solutions resolve to the same name, keep .sln over .slnx (or first found)
        var deduped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sln in selectedSlnFiles)
        {
            var name = Path.GetFileNameWithoutExtension(sln);
            if (!deduped.TryGetValue(name, out var existing))
            {
                deduped[name] = sln;
            }
            else
            {
                // Prefer .sln over .slnx
                var existingExt = Path.GetExtension(existing);
                var currentExt = Path.GetExtension(sln);
                if (existingExt.Equals(".slnx", StringComparison.OrdinalIgnoreCase) &&
                    currentExt.Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    deduped[name] = sln;
                }
                // Otherwise keep the first one found
            }
        }

        var finalSlnFiles = deduped.Values.ToArray();

        if (finalSlnFiles.Length == 1)
        {
            // After dedup, only one remains — use singular format
            var foundSln = Path.GetRelativePath(currentDir, finalSlnFiles[0]);
            Console.WriteLine($"  Selected: {foundSln}");

            var config = new CodeGraphConfig
            {
                Solution = foundSln,
                Output = outputDir ?? ".codegraph"
            };

            var configPath = Path.Combine(currentDir, ConfigLoader.DefaultFileName);
            await ConfigLoader.SaveAsync(config, configPath);
            Console.WriteLine($"Created {ConfigLoader.DefaultFileName}");
        }
        else
        {
            var entries = new List<SolutionEntry>();
            foreach (var sln in finalSlnFiles)
            {
                var relativePath = Path.GetRelativePath(currentDir, sln);
                Console.WriteLine($"  {relativePath}");
                entries.Add(new SolutionEntry { Path = relativePath });
            }

            var config = new CodeGraphConfig
            {
                Solutions = entries.ToArray(),
                Output = outputDir ?? ".codegraph"
            };

            var configPath = Path.Combine(currentDir, ConfigLoader.DefaultFileName);
            await ConfigLoader.SaveAsync(config, configPath);
            Console.WriteLine($"Created {ConfigLoader.DefaultFileName} with {entries.Count} solutions");
        }
    }

    // Determine a primary solution name for agent config files (APM).
    // In multi-solution setups, the first discovered solution is used for the package name.
    // This only affects the APM config name — MCP federation queries all solutions regardless.
    string? selectedSln = solutionFlag
        ?? (slnFiles.Length > 0 ? Path.GetFileName(slnFiles[0]) : null);

    // Generate MCP server configs for agent auto-discovery
    var mcpContent = """
        {
          "servers": {
            "codegraph": {
              "type": "stdio",
              "command": "dotnet",
              "args": ["codegraph", "mcp"],
              "cwd": "${workspaceFolder}"
            }
          }
        }
        """;

    // .vscode/mcp.json — VS Code Copilot, Cursor
    var vscodeMcpDir = Path.Combine(currentDir, ".vscode");
    var vscodeMcpPath = Path.Combine(vscodeMcpDir, "mcp.json");
    if (!File.Exists(vscodeMcpPath))
    {
        Directory.CreateDirectory(vscodeMcpDir);
        await File.WriteAllTextAsync(vscodeMcpPath, mcpContent);
        Console.WriteLine("Created .vscode/mcp.json");
    }
    else
    {
        Console.WriteLine(".vscode/mcp.json already exists — skipped");
    }

    // .mcp.json — Claude Code
    var mcpPath = Path.Combine(currentDir, ".mcp.json");
    if (!File.Exists(mcpPath))
    {
        var claudeMcpContent = """
            {
              "mcpServers": {
                "codegraph": {
                  "command": "dotnet",
                  "args": ["codegraph", "mcp"]
                }
              }
            }
            """;
        await File.WriteAllTextAsync(mcpPath, claudeMcpContent);
        Console.WriteLine("Created .mcp.json");
    }
    else
    {
        Console.WriteLine(".mcp.json already exists — skipped");
    }

    // apm.yml — Microsoft APM (Agent Package Manager)
    if (selectedSln is not null)
    {
        var apmPath = Path.Combine(currentDir, "apm.yml");
        if (!File.Exists(apmPath))
        {
            var packageName = Regex.Replace(
                Path.GetFileNameWithoutExtension(selectedSln).ToLowerInvariant(),
                @"[^a-z0-9._-]", "-");
            var apmContent = $"""
                name: {packageName}
                version: 1.0.0
                description: Agent configuration for {Path.GetFileNameWithoutExtension(selectedSln)}

                dependencies:
                  apm: []
                  mcp:
                    - name: codegraph
                      registry: false
                      transport: stdio
                      command: dotnet
                      args: ["codegraph", "mcp"]
                """;
            await File.WriteAllTextAsync(apmPath, apmContent);
            Console.WriteLine("Created apm.yml");
        }
        else
        {
            Console.WriteLine("apm.yml already exists — skipped");
        }
    }

    // --- Step 1: Detect / select agents ---
    Console.WriteLine();
    Console.WriteLine("CodeGraph Agent Setup");
    Console.WriteLine("=====================");
    Console.WriteLine();

    List<AgentKind> agentsToInstall;

    if (agentFlag is not null)
    {
        // Non-interactive: use specified agent(s)
        agentsToInstall = ParseAgentFlag(agentFlag);
        if (agentsToInstall.Count == 0)
        {
            Console.Error.WriteLine($"Unknown agent: {agentFlag}");
            Console.Error.WriteLine("Valid agents: claude, copilot, opencode, cursor, all");
            return 1;
        }
    }
    else
    {
        // Auto-detect agent configurations
        var detections = AgentDetector.Detect(currentDir);

        if (detections.Count > 0)
        {
            Console.WriteLine("Detected agent configurations:");
            foreach (var d in detections)
                Console.WriteLine($"  ✓ {d.Agent} ({d.MatchedPath} found)");

            // Report agents NOT detected
            var detected = detections.Select(d => d.Agent).ToHashSet();
            foreach (var agent in Enum.GetValues(typeof(AgentKind)).Cast<AgentKind>())
            {
                if (!detected.Contains(agent))
                    Console.WriteLine($"  ✗ {agent} (not detected)");
            }

            agentsToInstall = detections.Select(d => d.Agent).ToList();
        }
        else
        {
            Console.WriteLine("No agent configurations detected.");
            Console.WriteLine("Installing generic CodeGraph instructions.");
            agentsToInstall = new List<AgentKind>();
        }
    }

    // --- Step 2: Write skill files ---
    Console.WriteLine();
    Console.WriteLine("Installing CodeGraph skills...");
    Console.WriteLine();

    var results = await AgentSkillWriter.WriteAsync(currentDir, agentsToInstall, force);

    foreach (var r in results)
    {
        var icon = r.Action switch
        {
            WriteAction.Created => "✓ Created",
            WriteAction.Appended => "✓ Appended CodeGraph section to",
            WriteAction.AlreadyPresent => "· Already present in",
            WriteAction.Skipped => "· Skipped (exists)",
            _ => "?"
        };
        Console.WriteLine($"  {icon} {r.RelativePath}");
    }

    // --- Step 3: .gitignore ---
    var gitignoreResult = await AgentSkillWriter.EnsureGitignoreEntryAsync(currentDir);
    {
        var icon = gitignoreResult.Action switch
        {
            WriteAction.Created => "✓ Created",
            WriteAction.Appended => "✓ Added .codegraph/ to",
            WriteAction.AlreadyPresent => "· .codegraph/ already in",
            _ => "?"
        };
        Console.WriteLine($"  {icon} {gitignoreResult.RelativePath}");
    }

    // --- Step 4: Optional — run index ---
    if (solutionFlag is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"Indexing {solutionFlag}...");
        var indexArgs = new[] { "index", "--solution", solutionFlag, "--output", outputDir ?? ".codegraph" };
        var indexResult = await RunIndexAsync(indexArgs);
        if (indexResult != 0)
            return indexResult;

        // Auto-generate REPORT.md so agents have an overview on first use
        Console.WriteLine();
        Console.WriteLine("Generating REPORT.md for agent context...");
        var reportArgs = new[] { "report", "--graph-dir", outputDir ?? ".codegraph" };
        var reportResult = await RunReportAsync(reportArgs);
        if (reportResult != 0)
            Console.Error.WriteLine("Warning: report generation failed (non-blocking).");
    }

    // --- Next steps ---
    Console.WriteLine();
    Console.WriteLine("Next steps:");
    if (solutionFlag is null && selectedSln is not null)
    {
        Console.WriteLine("  1. Index your codebase:");
        Console.WriteLine($"     codegraph index --solution {selectedSln} --output .codegraph/");
        Console.WriteLine();
        Console.WriteLine("  2. Verify it works:");
        Console.WriteLine("     codegraph query '<any-type-name>' --depth 1");
    }
    else if (solutionFlag is null)
    {
        Console.WriteLine("  1. Index your codebase:");
        Console.WriteLine("     codegraph index --solution <your-solution.sln> --output .codegraph/");
        Console.WriteLine();
        Console.WriteLine("  2. Verify it works:");
        Console.WriteLine("     codegraph query '<any-type-name>' --depth 1");
    }
    else
    {
        Console.WriteLine("  1. Verify it works:");
        Console.WriteLine("     codegraph query '<any-type-name>' --depth 1");
    }
    Console.WriteLine();
    Console.WriteLine("  Commit the skill files:");
    Console.WriteLine("     git add .claude/ .github/ .codegraph/INSTRUCTIONS.md");
    Console.WriteLine("     git commit -m 'Add CodeGraph agent skills'");

    return 0;
}

static List<AgentKind> ParseAgentFlag(string value)
{
    return value.ToLowerInvariant() switch
    {
        "all" => new List<AgentKind>
        {
            AgentKind.Claude, AgentKind.Copilot, AgentKind.OpenCode, AgentKind.Cursor
        },
        "claude" => new List<AgentKind> { AgentKind.Claude },
        "copilot" => new List<AgentKind> { AgentKind.Copilot },
        "opencode" => new List<AgentKind> { AgentKind.OpenCode },
        "cursor" => new List<AgentKind> { AgentKind.Cursor },
        _ => new List<AgentKind>()
    };
}

static List<ProjectGraph> GroupIntoProjectGraphs(
    List<GraphNode> nodes, List<GraphEdge> edges, SplitFileStrategy strategy)
{
    var grouped = strategy switch
    {
        SplitFileStrategy.ByNamespace => nodes.GroupBy(n =>
        {
            var lastDot = n.Id.LastIndexOf('.');
            return lastDot > 0 ? n.Id[..lastDot] : n.Id;
        }),
        SplitFileStrategy.ByAssembly => nodes.GroupBy(n =>
        {
            if (string.IsNullOrEmpty(n.FilePath) && n.Metadata.ContainsKey("assembly"))
                return "_external";
            if (!string.IsNullOrEmpty(n.AssemblyName))
                return n.AssemblyName;
            var dotIndex = n.Id.IndexOf('.');
            return dotIndex > 0 ? n.Id[..dotIndex] : n.Id;
        }),
        _ => nodes.GroupBy(n =>
        {
            var dotIndex = n.Id.IndexOf('.');
            return dotIndex > 0 ? n.Id[..dotIndex] : n.Id;
        })
    };

    var nodeIdToKey = new Dictionary<string, string>();
    var result = new Dictionary<string, ProjectGraph>();

    foreach (var group in grouped)
    {
        var key = string.IsNullOrEmpty(group.Key) ? "_default" : group.Key;
        var nodeDict = new Dictionary<string, GraphNode>();
        foreach (var node in group)
        {
            nodeDict[node.Id] = node;
            nodeIdToKey[node.Id] = key;
        }

        result[key] = new ProjectGraph
        {
            ProjectOrNamespace = key,
            Nodes = nodeDict,
            Edges = new List<GraphEdge>()
        };
    }

    foreach (var edge in edges)
    {
        if (nodeIdToKey.TryGetValue(edge.FromId, out var key) && result.ContainsKey(key))
            result[key].Edges.Add(edge);
    }

    return result.Values.ToList();
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  codegraph init [--agent <name>] [--solution <path.sln>] [--force]");
    Console.WriteLine("  codegraph index --solution <path.sln> [options]");
    Console.WriteLine("  codegraph query <symbol> [options]");
    Console.WriteLine("  codegraph compare <symbolA> <symbolB> [options]");
    Console.WriteLine("  codegraph list [scope] [options]");
    Console.WriteLine("  codegraph search <query> [--top N] [--kind type]");
    Console.WriteLine("  codegraph explain <symbol> [--json] [--graph-dir <dir>]");
    Console.WriteLine("  codegraph impact <symbol> [--depth N] [--json] [--graph-dir <dir>]");
    Console.WriteLine("  codegraph path <from> <to> [--max-depth N] [--json] [--graph-dir <dir>]");
    Console.WriteLine("  codegraph diff [options]");
    Console.WriteLine("  codegraph export [--graph-dir <dir>] [--output <dir>]");
    Console.WriteLine("  codegraph report [--graph-dir <dir>] [--output <path>]");
    Console.WriteLine("  codegraph stats [--graph-dir <dir>]");
    Console.WriteLine("  codegraph brief [--graph-dir <dir>]");
    Console.WriteLine("  codegraph wiki [--graph-dir <dir>] [--output <dir>]");
    Console.WriteLine("  codegraph view [--graph-dir <dir>] [--output <path>] [--max-nodes <n>] [--no-open]");
    Console.WriteLine("  codegraph test-impact <symbol> [--depth N] [--graph-dir <dir>]");
    Console.WriteLine("  codegraph snapshot <save|list|delete> [name] [--graph-dir <dir>]");
    Console.WriteLine("  codegraph packages [--project <name>] [--package <name>] [--format json]");
    Console.WriteLine("  codegraph benchmark [--scenarios <path>] [--iterations N] [--format json]");
    Console.WriteLine("  codegraph daemon <start|stop|status> [--graph-dir <dir>]");
    Console.WriteLine("  codegraph mcp [--graph-dir <dir>]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  init                     Initialize config, MCP, and agent skill files");
    Console.WriteLine("  index                    Build the code graph from a solution");
    Console.WriteLine("  query                    Query the code graph for symbols and relationships");
    Console.WriteLine("  compare                  Compare two symbols structurally");
    Console.WriteLine("  list                     Browse the code graph hierarchy (assemblies, types, etc.)");
    Console.WriteLine("  search                   Search for symbols by name, namespace, or file path");
    Console.WriteLine("  explain                  Show detailed information about a symbol");
    Console.WriteLine("  impact                   Analyze the blast radius of changes to a symbol");
    Console.WriteLine("  path                     Find shortest path between two symbols");
    Console.WriteLine("  diff                     Compare graph snapshots and report structural changes");
    Console.WriteLine("  export                   Export graph from SQLite database to JSON files");
    Console.WriteLine("  report                   Generate a markdown report analyzing the graph");
    Console.WriteLine("  stats                    Show graph-level statistics (node/edge counts by kind)");
    Console.WriteLine("  brief                    Generate compact BRIEF.md codebase orientation for LLM agents");
    Console.WriteLine("  wiki                     Generate navigable markdown wiki pages from the graph");
    Console.WriteLine("  view                     Open interactive 3D graph visualization in browser");
    Console.WriteLine("  test-impact              Analyze test coverage for a symbol (direct + indirect)");
    Console.WriteLine("  snapshot                 Save, list, or delete graph snapshots for diff comparison");
    Console.WriteLine("  packages                 Analyze NuGet package usage and version conflicts");
    Console.WriteLine("  benchmark                Run performance benchmarks against the graph");
    Console.WriteLine("  daemon                   Start/stop persistent background daemon for fast queries");
    Console.WriteLine("  mcp                      Start MCP (Model Context Protocol) stdio server");
    Console.WriteLine();
    Console.WriteLine("Run 'codegraph <command> --help' for command-specific options.");
}

static void PrintListUsage()
{
    Console.WriteLine("""
        Usage: codegraph list [scope] [options]

        Scopes:
          assemblies             List all assemblies with type/method counts (default)
          types                  List types, ranked by connectivity
          interfaces             List interfaces with implementation counts
          namespaces             List namespaces with type counts

        Options:
          --assembly <name>      Filter by assembly name (types, interfaces, namespaces)
          --top <n>              Max items to return (default: 50, types only)
          --skip <n>             Skip N results for pagination (types only)
          --filter <pattern>     Filter by name substring, case-insensitive (types only)
          --graph-dir <path>     Graph directory (default: .codegraph)
          --json                 Output as JSON
        """);
}

static void PrintSearchUsage()
{
    Console.WriteLine("""
        Usage: codegraph search <query> [options]

        Searches across type names, namespace names, file paths, and method names
        using case-insensitive substring matching.

        Options:
          --top <n>              Max results to return (default: 20)
          --kind <kind>          Filter by node kind: type, method, namespace, property, field
          --graph-dir <path>     Graph directory (default: .codegraph)
          --json                 Output as JSON
        """);
}

static void PrintInitUsage()
{
    Console.WriteLine("""
        Usage: codegraph init [options]

        Initializes codegraph.json, MCP configs, and scaffolds agent skill files.
        Auto-detects which AI agents are configured in the repository.

        Options:
          --agent <name>       Install for a specific agent: claude, copilot, opencode, cursor, all
          --solution <path>    Combine init + index in one step
          --output <dir>       Output directory for graph data (default: .codegraph)
          --force              Overwrite existing skill files
          --help, -h           Show this help

        Examples:
          codegraph init                          # Auto-detect agents and scaffold
          codegraph init --agent claude           # Install Claude Code skill files
          codegraph init --agent all              # Install for all agents
          codegraph init --solution MyApp.sln     # Init + index in one step
        """);
}

static void PrintExportUsage()
{
    Console.WriteLine("""
        Usage: codegraph export [options]

        Exports the graph from SQLite database (graph.db) to JSON files.

        Options:
          --graph-dir <dir>    Directory containing graph.db (default: .codegraph)
          --output <dir>       Output directory for JSON files (default: export)
          --help, -h           Show this help

        Examples:
          codegraph export                                    # Export from .codegraph/graph.db to export/
          codegraph export --output my-export                 # Export to my-export/
          codegraph export --graph-dir .codegraph --output .  # Export JSON alongside graph.db
        """);
}

static void PrintReportUsage()
{
    Console.WriteLine("""
        Usage: codegraph report [options]

        Generates a markdown report analyzing the code graph: hub types,
        assembly boundaries, test coverage, and suggested queries.

        Options:
          --graph-dir <dir>    Directory containing graph.db (default: .codegraph)
          --output, -o <path>  Output file path (default: .codegraph/REPORT.md)
          --help, -h           Show this help

        Examples:
          codegraph report                                    # Generate .codegraph/REPORT.md
          codegraph report -o report.md                       # Write to custom path
          codegraph report --graph-dir .codegraph-prev        # Report on a different graph
        """);
}

static void PrintStatsUsage()
{
    Console.WriteLine("""
        Usage: codegraph stats [options]

        Shows graph-level statistics: total nodes/edges, counts by kind and type.

        Options:
          --graph-dir <dir>    Directory containing graph data (default: .codegraph)
          --help, -h           Show this help

        Examples:
          codegraph stats                                     # Stats for .codegraph
          codegraph stats --graph-dir .codegraph-prev         # Stats for a different graph
        """);
}

static void PrintWikiUsage()
{
    Console.WriteLine("""
        Usage: codegraph wiki [options]

        Generates navigable markdown wiki pages from the code graph.
        Creates INDEX.md, per-assembly pages, INTERFACES.md, and DI-WIRING.md.

        Options:
          --graph-dir <dir>    Directory containing graph data (default: .codegraph)
          --output, -o <dir>   Output directory for wiki pages (default: .codegraph/wiki)
          --help, -h           Show this help

        Examples:
          codegraph wiki                                      # Generate .codegraph/wiki/
          codegraph wiki -o docs/wiki                         # Write to custom directory
          codegraph wiki --graph-dir .codegraph-prev          # Wiki from a different graph
        """);
}

static void PrintViewUsage()
{
    Console.WriteLine("""
        Usage: codegraph view [options]

        Generates an interactive 3D graph visualization as a self-contained HTML file
        and opens it in your default browser.

        Options:
          --graph-dir <dir>    Directory containing graph data (default: .codegraph)
          --output, -o <path>  Output HTML file (default: system temp directory)
          --max-nodes <n>      Maximum nodes to render (default: 5000)
          --no-open            Don't open browser automatically
          --help, -h           Show this help

        Examples:
          codegraph view                                      # Generate and open
          codegraph view --no-open                            # Generate without opening
          codegraph view --max-nodes 2000                     # Smaller graph for performance
          codegraph view -o report/graph.html                 # Custom output path
        """);
}

static void PrintQueryUsage()
{
    Console.WriteLine("""
        Usage: codegraph query <symbol-pattern> [options]

        Options:
          --depth <n>          Traversal depth (default: 1)
          --kind <type>        Edge filter: calls-to, calls-from, inherits, implements,
                               depends-on, resolves-to, covers, all
          --mode <mode>        Traversal mode: focused, structural, all (default: all)
          --namespace <filter> Include only nodes in matching namespaces
          --project <filter>   Include only nodes in matching projects
          --format <fmt>       json | text | context (default: context)
          --max-nodes <n>      Cap output size (default: 50)
          --include-external   Include external dependency nodes
          --include-source     Embed source code snippets in output
          --no-rank            Disable result ranking
          --graph-dir <path>   Graph directory (default: .codegraph)
          --from <solution>    Query only the specified solution sub-graph (multi-solution)
          --budget <tokens>    Maximum token budget for output (truncates with hint)
          --no-metrics         Suppress the compression metrics footer
          --json               Alias for --format json
        """);
}

static void PrintCompareUsage()
{
    Console.WriteLine("""
        Usage: codegraph compare <symbolA> <symbolB> [options]

        Compare two symbols structurally. Shows shared interfaces, base types,
        dependencies, and unique relationships for each symbol.

        Options:
          --depth <n>          Traversal depth (default: 1)
          --graph-dir <path>   Graph directory (default: .codegraph)
        """);
}

static void PrintDiffUsage()
{
    Console.WriteLine("""
        Usage: codegraph diff [options]

        Options:
          --base <path>         Base graph directory (default: .codegraph-prev)
          --head <path>         Head graph directory (default: .codegraph)
          --ref <git-ref>       Use snapshot named .codegraph-<ref> as base
          --only <types>        Comma-separated: added, removed, signature-changed,
                                added-nodes, removed-nodes, added-edges, removed-edges
          --format <fmt>        json | text | context (default: context)
        """);
}

static void CheckStaleness(string graphDir)
{
    try
    {
        var metaPath = Path.Combine(graphDir, "meta.json");
        if (!File.Exists(metaPath)) return;

        var json = File.ReadAllText(metaPath);
        var commitMatch = Regex.Match(json, "\"commitHash\"\\s*:\\s*\"([^\"]+)\"");
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

        if (!string.IsNullOrEmpty(currentCommit)
            && !currentCommit.StartsWith(graphCommit, StringComparison.OrdinalIgnoreCase)
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

static bool IsOutputPiped() => Console.IsOutputRedirected;

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

static HashSet<GraphDiffChangeType> ParseDiffOnly(string? value)
{
    var result = new HashSet<GraphDiffChangeType>();
    if (string.IsNullOrWhiteSpace(value))
        return result;

    var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var token in tokens)
    {
        switch (token.ToLowerInvariant())
        {
            case "added":
                result.Add(GraphDiffChangeType.AddedNodes);
                result.Add(GraphDiffChangeType.AddedEdges);
                break;
            case "removed":
                result.Add(GraphDiffChangeType.RemovedNodes);
                result.Add(GraphDiffChangeType.RemovedEdges);
                break;
            case "signature-changed":
                result.Add(GraphDiffChangeType.SignatureChangedNodes);
                break;
            case "added-nodes":
                result.Add(GraphDiffChangeType.AddedNodes);
                break;
            case "removed-nodes":
                result.Add(GraphDiffChangeType.RemovedNodes);
                break;
            case "added-edges":
                result.Add(GraphDiffChangeType.AddedEdges);
                break;
            case "removed-edges":
                result.Add(GraphDiffChangeType.RemovedEdges);
                break;
            default:
                throw new ArgumentException($"Unknown diff change type '{token}' in --only.");
        }
    }

    return result;
}

static GraphDiffResult ApplyDiffFilter(GraphDiffResult diff, HashSet<GraphDiffChangeType> filter)
{
    return new GraphDiffResult
    {
        BaseMetadata = diff.BaseMetadata,
        HeadMetadata = diff.HeadMetadata,
        AddedNodes = filter.Contains(GraphDiffChangeType.AddedNodes) ? diff.AddedNodes : new List<GraphNode>(),
        RemovedNodes = filter.Contains(GraphDiffChangeType.RemovedNodes) ? diff.RemovedNodes : new List<GraphNode>(),
        SignatureChangedNodes = filter.Contains(GraphDiffChangeType.SignatureChangedNodes)
            ? diff.SignatureChangedNodes
            : new List<GraphSignatureChange>(),
        AddedEdges = filter.Contains(GraphDiffChangeType.AddedEdges) ? diff.AddedEdges : new List<GraphEdge>(),
        RemovedEdges = filter.Contains(GraphDiffChangeType.RemovedEdges) ? diff.RemovedEdges : new List<GraphEdge>()
    };
}

static string RunGit(string arguments, string workingDirectory)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null) return string.Empty;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : string.Empty;
    }
    catch
    {
        return string.Empty;
    }
}

static bool WildcardMatch(string input, string pattern)
{
    var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
    return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
}

partial class Program
{
    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
