using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer;

/// <summary>
/// High-level API for indexing C# solutions into a code graph.
/// Use this when embedding CodeGraph as a library instead of using the CLI.
/// </summary>
public class CodeGraphIndexer
{
    private readonly IndexerOptions _options;

    public CodeGraphIndexer(IndexerOptions? options = null)
    {
        _options = options ?? new IndexerOptions();
    }

    /// <summary>
    /// Index a solution and return the full graph in memory.
    /// </summary>
    public async Task<IndexResult> IndexAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        solutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}", solutionPath);

        var solutionRoot = Path.GetDirectoryName(solutionPath)!;

        var projects = await LoadAndFilterProjectsAsync(solutionPath, cancellationToken);
        var (nodes, edges) = RunPasses(projects, solutionRoot);
        var metadata = BuildMetadata(solutionPath, solutionRoot, nodes, edges, projects);

        return new IndexResult(nodes.AsReadOnly(), edges.AsReadOnly(), metadata);
    }

    /// <summary>
    /// Index a solution and write results to disk (JSON + SQLite).
    /// </summary>
    public async Task<IndexResult> IndexAndWriteAsync(
        string solutionPath,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        var result = await IndexAsync(solutionPath, cancellationToken);

        outputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);

        var writer = new GraphWriter();
        await writer.WriteAsync(outputDir, result.Nodes, result.Edges, result.Metadata);

        var sqliteWriter = new SqliteGraphWriter();
        var dbPath = Path.Combine(outputDir, "graph.db");
        await sqliteWriter.WriteAsync(dbPath, result.Nodes, result.Edges, result.Metadata);

        return result;
    }

    private async Task<List<ProjectCompilation>> LoadAndFilterProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var loader = new HybridWorkspaceLoader();
        var compilations = await loader.LoadAsync(
            solutionPath,
            skipRestore: _options.SkipBuild || _options.SkipRestore,
            configuration: _options.Configuration,
            preprocessorSymbols: _options.PreprocessorSymbols,
            cancellationToken: cancellationToken);

        var filtered = compilations.AsEnumerable();

        if (!string.IsNullOrEmpty(_options.ProjectFilter))
        {
            filtered = filtered.Where(p => WildcardMatch(p.ProjectName, _options.ProjectFilter!));
        }
        else if (_options.IncludeProjects is { Length: > 0 } includes
                 && !(includes.Length == 1 && includes[0] == "*"))
        {
            filtered = filtered.Where(p =>
                includes.Any(pat => WildcardMatch(p.ProjectName, pat)));
        }

        if (_options.ExcludeProjects is { Length: > 0 } excludes)
        {
            filtered = filtered.Where(p =>
                !excludes.Any(pat => WildcardMatch(p.ProjectName, pat)));
        }

        return filtered.ToList();
    }

    private (List<GraphNode> Nodes, List<GraphEdge> Edges) RunPasses(
        List<ProjectCompilation> projects,
        string solutionRoot)
    {
        var projectResults = new ConcurrentBag<(List<GraphNode> Nodes, List<GraphEdge> Edges)>();
        var passOptions = new PassPipelineOptions(
            EnableRoutesPass: _options.EnableRoutesPass,
            EnableConfigurationPass: _options.EnableConfigurationPass,
            EnableMiddlewarePass: _options.EnableMiddlewarePass,
            EnableDbContextPass: _options.EnableDbContextPass);

        Parallel.ForEach(projects, project =>
        {
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

        return (allNodes, allEdges);
    }

    private static GraphMetadata BuildMetadata(
        string solutionPath,
        string solutionRoot,
        List<GraphNode> nodes,
        List<GraphEdge> edges,
        List<ProjectCompilation> projects)
    {
        var commitHash = RunGit("rev-parse HEAD", solutionRoot);
        var branch = RunGit("rev-parse --abbrev-ref HEAD", solutionRoot);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        return new GraphMetadata
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
                ["node_count"] = nodes.Count,
                ["edge_count"] = edges.Count,
                ["type_count"] = nodes.Count(n => n.Kind == NodeKind.Type),
                ["method_count"] = nodes.Count(n => n.Kind is NodeKind.Method or NodeKind.Constructor)
            }
        };
    }

    private static string RunGit(string arguments, string workingDirectory)
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

    private static bool WildcardMatch(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Options for configuring the indexing process.
/// </summary>
public record IndexerOptions
{
    /// <summary>Wildcard pattern to filter projects by name.</summary>
    public string? ProjectFilter { get; init; }

    /// <summary>Build configuration (e.g. Debug, Release).</summary>
    public string Configuration { get; init; } = "Debug";

    /// <summary>Skip dotnet build before indexing.</summary>
    public bool SkipBuild { get; init; }

    /// <summary>Skip dotnet restore before indexing.</summary>
    public bool SkipRestore { get; init; }

    /// <summary>Additional preprocessor symbols to define during compilation.</summary>
    public string[]? PreprocessorSymbols { get; init; }

    /// <summary>Wildcard patterns for projects to include. Defaults to all.</summary>
    public string[]? IncludeProjects { get; init; }

    /// <summary>Wildcard patterns for projects to exclude.</summary>
    public string[]? ExcludeProjects { get; init; }

    /// <summary>Enable the ASP.NET route-discovery pass.</summary>
    public bool EnableRoutesPass { get; init; } = true;

    /// <summary>Enable the IConfiguration binding pass.</summary>
    public bool EnableConfigurationPass { get; init; } = true;

    /// <summary>Enable the middleware pipeline pass.</summary>
    public bool EnableMiddlewarePass { get; init; } = true;

    /// <summary>Enable the EF Core DbContext pass.</summary>
    public bool EnableDbContextPass { get; init; } = true;
}

/// <summary>
/// Result of an indexing operation containing the full code graph.
/// </summary>
/// <param name="Nodes">All graph nodes discovered during indexing.</param>
/// <param name="Edges">All graph edges (relationships) discovered during indexing.</param>
/// <param name="Metadata">Metadata about the indexing run (commit, timing, stats).</param>
public record IndexResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    GraphMetadata Metadata);
