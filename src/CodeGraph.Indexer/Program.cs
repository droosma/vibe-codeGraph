using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Init;
using CodeGraph.Indexer.Mcp;
using CodeGraph.Indexer.Passes;
using CodeGraph.Indexer.Workspace;
using CodeGraph.Query;
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
        "list" => await RunListAsync(args),
        "diff" => await RunDiffAsync(args),
        "export" => await RunExportAsync(args),
        "report" => await RunReportAsync(args),
        "stats" => await RunStatsAsync(args),
        "wiki" => await RunWikiAsync(args),
        "view" => await RunViewAsync(args),
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
    var format = GetOption(argList, "--format", "context");
    var mode = GetOption(argList, "--mode", "all");
    var maxNodes = GetOption(argList, "--max-nodes", 50);
    var includeExternal = HasFlag(argList, "--include-external");
    var rank = !HasFlag(argList, "--no-rank");
    var noMetrics = HasFlag(argList, "--no-metrics");
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");
    var fromSolution = GetOption(argList, "--from", (string?)null);
    var budget = GetOption(argList, "--budget", (int?)null);

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
        return 1;
    }

    var queryDesc = $"{pattern} --depth {depth} --kind {kind ?? "all"}";
    var output = outputFormat switch
    {
        OutputFormat.Json => JsonFormatter.Format(result),
        OutputFormat.Text => TextFormatter.Format(result),
        OutputFormat.Context => ContextFormatter.Format(result, queryDesc),
        OutputFormat.Compact => CompactFormatter.Format(result),
        _ => ContextFormatter.Format(result, queryDesc)
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
    var top = GetOption(argList, "--top", 20);
    var graphDir = GetOption(argList, "--graph-dir", ".codegraph");

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
            Console.WriteLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
            Console.WriteLine(new string('-', 62));
            foreach (var a in assemblies)
                Console.WriteLine($"{a.Name,-40} {a.TypeCount,6} {a.MethodCount,8} {a.TotalNodeCount,6}");
            break;

        case "types":
            var types = list.ListTypes(assemblyFilter, top);
            Console.WriteLine($"{"Type",-50} {"In",4} {"Out",4} {"Assembly",-20}");
            Console.WriteLine(new string('-', 80));
            foreach (var t in types)
                Console.WriteLine($"{t.Name,-50} {t.InDegree,4} {t.OutDegree,4} {t.Assembly,-20}");
            break;

        case "interfaces":
            var ifaces = list.ListInterfaces(assemblyFilter);
            Console.WriteLine($"{"Interface",-50} {"Impls",6} {"Assembly",-20}");
            Console.WriteLine(new string('-', 78));
            foreach (var iface in ifaces)
                Console.WriteLine($"{iface.Name,-50} {iface.ImplementationCount,6} {iface.Assembly,-20}");
            break;

        case "namespaces":
            var namespaces = list.ListNamespaces(assemblyFilter);
            Console.WriteLine($"{"Namespace",-50} {"Types",6} {"Methods",8}");
            Console.WriteLine(new string('-', 66));
            foreach (var ns in namespaces)
                Console.WriteLine($"{ns.Name,-50} {ns.TypeCount,6} {ns.MethodCount,8}");
            break;

        default:
            Console.Error.WriteLine($"Unknown list scope: {scope}. Use: assemblies, types, interfaces, namespaces");
            return 1;
    }

    return 0;
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
    var syntaxPass = new SyntaxPass();
    var semanticPass = new SemanticPass();
    var diPass = new DiPass();
    var testCoveragePass = new TestCoveragePass();

    Parallel.ForEach(projects, project =>
    {
        if (verbose) Console.WriteLine($"  Indexing {project.ProjectName}...");

        try
        {
            var nodes = new List<GraphNode>();
            var edges = new List<GraphEdge>();

            var (syntaxNodes, syntaxEdges) = syntaxPass.Execute(project.Compilation, solutionRoot);
            nodes.AddRange(syntaxNodes);
            edges.AddRange(syntaxEdges);

            var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

            var (externalNodes, semanticEdges) = semanticPass.Execute(project.Compilation, solutionRoot, knownIds);
            nodes.AddRange(externalNodes);
            edges.AddRange(semanticEdges);
            foreach (var en in externalNodes) knownIds.Add(en.Id);

            var (diEdges, diExternalNodes) = diPass.Execute(project.Compilation, solutionRoot, knownIds);
            nodes.AddRange(diExternalNodes);
            edges.AddRange(diEdges);
            foreach (var en in diExternalNodes) knownIds.Add(en.Id);

            var (testEdges, testExternalNodes) = testCoveragePass.Execute(project.Compilation, solutionRoot, knownIds);
            nodes.AddRange(testExternalNodes);
            edges.AddRange(testEdges);

            projectResults.Add((nodes, edges));
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

static async Task<int> RunViewAsync(string[] args)
{
    var graphDir = ".codegraph";
    var output = (string?)null;
    var maxNodes = 5000;
    var open = true;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph-dir" when i + 1 < args.Length:
                graphDir = args[++i];
                break;
            case "--output" or "-o" when i + 1 < args.Length:
                output = args[++i];
                break;
            case "--max-nodes" when i + 1 < args.Length:
                maxNodes = int.Parse(args[++i]);
                break;
            case "--no-open":
                open = false;
                break;
            case "-h" or "--help":
                PrintViewUsage();
                return 0;
        }
    }

    output ??= Path.Combine(graphDir, "graph.html");

    var generator = new HtmlGraphGenerator(graphDir, maxNodes);

    try
    {
        var html = await generator.GenerateAsync();
        var fullPath = Path.GetFullPath(output);
        await File.WriteAllTextAsync(fullPath, html);
        Console.WriteLine($"Graph visualization written to {fullPath}");

        if (open)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
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
        Console.WriteLine("Multiple solutions found:");
        var entries = new List<SolutionEntry>();
        foreach (var sln in slnFiles)
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
    if (solutionFlag is null)
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
    Console.WriteLine("  codegraph list [scope] [options]");
    Console.WriteLine("  codegraph diff [options]");
    Console.WriteLine("  codegraph export [--graph-dir <dir>] [--output <dir>]");
    Console.WriteLine("  codegraph report [--graph-dir <dir>] [--output <path>]");
    Console.WriteLine("  codegraph stats [--graph-dir <dir>]");
    Console.WriteLine("  codegraph wiki [--graph-dir <dir>] [--output <dir>]");
    Console.WriteLine("  codegraph view [--graph-dir <dir>] [--output <path>] [--max-nodes <n>] [--no-open]");
    Console.WriteLine("  codegraph mcp [--graph-dir <dir>]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  init                     Initialize config, MCP, and agent skill files");
    Console.WriteLine("  index                    Build the code graph from a solution");
    Console.WriteLine("  query                    Query the code graph for symbols and relationships");
    Console.WriteLine("  list                     Browse the code graph hierarchy (assemblies, types, etc.)");
    Console.WriteLine("  diff                     Compare graph snapshots and report structural changes");
    Console.WriteLine("  export                   Export graph from SQLite database to JSON files");
    Console.WriteLine("  report                   Generate a markdown report analyzing the graph");
    Console.WriteLine("  stats                    Show graph-level statistics (node/edge counts by kind)");
    Console.WriteLine("  wiki                     Generate navigable markdown wiki pages from the graph");
    Console.WriteLine("  view                     Open interactive 3D graph visualization in browser");
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
          --top <n>              Max items to return (default: 20, types only)
          --graph-dir <path>     Graph directory (default: .codegraph)
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

static void PrintViewUsage()
{
    Console.WriteLine("""
        Usage: codegraph view [options]

        Generates an interactive 3D graph visualization as a self-contained HTML file
        and opens it in the default browser.

        Options:
          --graph-dir <path>   Directory containing graph data (default: .codegraph)
          --output <path>      Save HTML to a specific file (default: temp file)
          --max-nodes <n>      Maximum nodes to render, smart-sampled when exceeded (default: 5000)
          --no-open            Generate HTML but do not open in browser
          --help, -h           Show this help

        Examples:
          codegraph view                              # Open graph in browser
          codegraph view --output graph.html          # Save to file
          codegraph view --max-nodes 2000             # Limit for performance
          codegraph view --graph-dir .codegraph/Api   # View specific sub-graph
          codegraph view --output report.html --no-open  # CI: generate without opening
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
          --output, -o <path>  Output HTML file (default: .codegraph/graph.html)
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
          --no-rank            Disable result ranking
          --graph-dir <path>   Graph directory (default: .codegraph)
          --from <solution>    Query only the specified solution sub-graph (multi-solution)
          --budget <tokens>    Maximum token budget for output (truncates with hint)
          --no-metrics         Suppress the compression metrics footer
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
