using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Init;
using CodeGraph.Indexer.Mcp;
using CodeGraph.Indexer.Passes;
using CodeGraph.Indexer.Workspace;
using CodeGraph.Query;
using CodeGraph.Query.Filters;
using CodeGraph.Query.OutputFormatters;

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

static async Task<int> RunIndexAsync(string[] args)
{
    string? solutionPath = null;
    string? outputDir = null;
    string? projectFilter = null;
    string? configPath = null;
    string? configuration = null;
    bool verbose = false;
    bool skipBuild = false;
    bool changedOnly = false;

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
            case "--skip-build":
                skipBuild = true;
                break;
            case "--changed-only":
                changedOnly = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                PrintUsage();
                return 1;
        }
    }

    // Load config, CLI args override
    var config = ConfigLoader.Load(configPath);
    solutionPath ??= config.Solution;
    outputDir ??= config.Output;
    configuration ??= config.Index.Configuration;

    if (string.IsNullOrEmpty(solutionPath))
    {
        Console.Error.WriteLine("Error: --solution is required (or set 'solution' in codegraph.json).");
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
            var reader = new GraphReader();
            (existingMetadata, existingNodes, existingEdges) = await reader.ReadAsync(outputDir);

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
        skipBuild: skipBuild,
        configuration: configuration,
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

    // Run passes
    var allNodes = new List<GraphNode>();
    var allEdges = new List<GraphEdge>();
    var syntaxPass = new SyntaxPass();
    var semanticPass = new SemanticPass();
    var diPass = new DiPass();
    var testCoveragePass = new TestCoveragePass();

    foreach (var project in projects)
    {
        if (verbose) Console.WriteLine($"  Indexing {project.ProjectName}...");

        try
        {
            var (nodes, edges) = syntaxPass.Execute(project.Compilation, solutionRoot);
            allNodes.AddRange(nodes);
            allEdges.AddRange(edges);

            var knownIds = new HashSet<string>(allNodes.Select(n => n.Id));

            var (externalNodes, semanticEdges) = semanticPass.Execute(project.Compilation, solutionRoot, knownIds);
            allNodes.AddRange(externalNodes);
            allEdges.AddRange(semanticEdges);
            foreach (var en in externalNodes) knownIds.Add(en.Id);

            var (diEdges, diExternalNodes) = diPass.Execute(project.Compilation, solutionRoot, knownIds);
            allNodes.AddRange(diExternalNodes);
            allEdges.AddRange(diEdges);
            foreach (var en in diExternalNodes) knownIds.Add(en.Id);

            var (testEdges, testExternalNodes) = testCoveragePass.Execute(project.Compilation, solutionRoot, knownIds);
            allNodes.AddRange(testExternalNodes);
            allEdges.AddRange(testEdges);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Error indexing {project.ProjectName}: {ex.Message}");
        }
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

        var merger = new GraphMerger();
        var (mergedNodes, mergedEdges) = merger.Merge(existingNodes, existingEdges, partialGraphs);

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

    // --- Step 0: codegraph.json + MCP configs (existing behavior) ---
    var slnFiles = Directory.GetFiles(currentDir, "*.sln");
    string? selectedSln = solutionFlag;

    if (selectedSln is null && slnFiles.Length > 0)
    {
        if (slnFiles.Length == 1)
        {
            selectedSln = Path.GetFileName(slnFiles[0]);
            Console.WriteLine($"Found solution: {selectedSln}");
        }
        else
        {
            Console.WriteLine("Multiple solutions found:");
            foreach (var sln in slnFiles)
                Console.WriteLine($"  {Path.GetFileName(sln)}");
            selectedSln = Path.GetFileName(slnFiles[0]);
            Console.WriteLine($"Using first: {selectedSln}");
        }
    }

    if (selectedSln is not null)
    {
        var config = new CodeGraphConfig
        {
            Solution = selectedSln,
            Output = outputDir ?? ".codegraph"
        };

        var configPath = Path.Combine(currentDir, ConfigLoader.DefaultFileName);
        await ConfigLoader.SaveAsync(config, configPath);
        Console.WriteLine($"Created {ConfigLoader.DefaultFileName}");
    }

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
    Console.WriteLine("  codegraph mcp [--graph-dir <dir>]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  init                     Initialize config, MCP, and agent skill files");
    Console.WriteLine("  index                    Build the code graph from a solution");
    Console.WriteLine("  query                    Query the code graph for symbols and relationships");
    Console.WriteLine("  mcp                      Start MCP (Model Context Protocol) stdio server");
    Console.WriteLine();
    Console.WriteLine("Run 'codegraph <command> --help' for command-specific options.");
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

static void PrintQueryUsage()
{
    Console.WriteLine("""
        Usage: codegraph query <symbol-pattern> [options]

        Options:
          --depth <n>          Traversal depth (default: 1)
          --kind <type>        Edge filter: calls-to, calls-from, inherits, implements,
                               depends-on, resolves-to, covers, all
          --namespace <filter> Include only nodes in matching namespaces
          --project <filter>   Include only nodes in matching projects
          --format <fmt>       json | text | context (default: context)
          --max-nodes <n>      Cap output size (default: 50)
          --include-external   Include external dependency nodes
          --no-rank            Disable result ranking
          --graph-dir <path>   Graph directory (default: .codegraph)
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
