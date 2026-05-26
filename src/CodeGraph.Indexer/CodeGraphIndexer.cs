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
        var fullSolutionPath = GetExistingSolutionPath(solutionPath);
        var solutionRoot = Path.GetDirectoryName(fullSolutionPath)!;
        var projects = await LoadAndFilterProjectsAsync(fullSolutionPath, cancellationToken).ConfigureAwait(false);
        var (nodes, edges) = RunPasses(projects, solutionRoot);
        var metadata = BuildMetadata(fullSolutionPath, solutionRoot, nodes, edges, projects);

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
        var result = await IndexAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        await WriteOutputsAsync(outputDir, result).ConfigureAwait(false);
        return result;
    }

    private static string GetExistingSolutionPath(string solutionPath)
    {
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullSolutionPath))
            throw new FileNotFoundException($"Solution file not found: {fullSolutionPath}", fullSolutionPath);

        return fullSolutionPath;
    }

    private static async Task WriteOutputsAsync(string outputDir, IndexResult result)
    {
        var fullOutputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(fullOutputDir);

        var graphWriter = new GraphWriter();
        await graphWriter.WriteAsync(fullOutputDir, result.Nodes, result.Edges, result.Metadata).ConfigureAwait(false);

        var sqliteWriter = new SqliteGraphWriter();
        var databasePath = Path.Combine(fullOutputDir, "graph.db");
        await sqliteWriter.WriteAsync(databasePath, result.Nodes, result.Edges, result.Metadata).ConfigureAwait(false);
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
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ApplyProjectFilters(compilations).ToList();
    }

    private IEnumerable<ProjectCompilation> ApplyProjectFilters(IEnumerable<ProjectCompilation> projects)
    {
        var filteredProjects = projects;

        if (!string.IsNullOrEmpty(_options.ProjectFilter))
        {
            filteredProjects = filteredProjects.Where(project =>
                WildcardMatch(project.ProjectName, _options.ProjectFilter!));
        }
        else if (ShouldApplyIncludeFilter())
        {
            filteredProjects = filteredProjects.Where(project =>
                _options.IncludeProjects!.Any(pattern => WildcardMatch(project.ProjectName, pattern)));
        }

        if (_options.ExcludeProjects is { Length: > 0 } excludedProjects)
        {
            filteredProjects = filteredProjects.Where(project =>
                !excludedProjects.Any(pattern => WildcardMatch(project.ProjectName, pattern)));
        }

        return filteredProjects;
    }

    private bool ShouldApplyIncludeFilter()
    {
        return _options.IncludeProjects is { Length: > 0 } includedProjects
               && !(includedProjects.Length == 1 && includedProjects[0] == "*");
    }

    private (List<GraphNode> Nodes, List<GraphEdge> Edges) RunPasses(
        List<ProjectCompilation> projects,
        string solutionRoot)
    {
        var projectResults = new ConcurrentBag<(List<GraphNode> Nodes, List<GraphEdge> Edges)>();
        var passOptions = CreatePassOptions();

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

        return MergeProjectResults(projectResults);
    }

    private PassPipelineOptions CreatePassOptions()
    {
        return new PassPipelineOptions(
            EnableRoutesPass: _options.EnableRoutesPass,
            EnableConfigurationPass: _options.EnableConfigurationPass,
            EnableMiddlewarePass: _options.EnableMiddlewarePass,
            EnableDbContextPass: _options.EnableDbContextPass);
    }

    private static (List<GraphNode> Nodes, List<GraphEdge> Edges) MergeProjectResults(
        IEnumerable<(List<GraphNode> Nodes, List<GraphEdge> Edges)> projectResults)
    {
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
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        return new GraphMetadata
        {
            CommitHash = RunGit("rev-parse HEAD", solutionRoot),
            Branch = RunGit("rev-parse --abbrev-ref HEAD", solutionRoot),
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = version,
            Solution = Path.GetFileName(solutionPath),
            SolutionName = Path.GetFileNameWithoutExtension(solutionPath),
            ProjectsIndexed = projects.Select(project => project.ProjectName).ToArray(),
            Stats = BuildStats(nodes, edges)
        };
    }

    private static Dictionary<string, int> BuildStats(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        return new Dictionary<string, int>
        {
            ["node_count"] = nodes.Count,
            ["edge_count"] = edges.Count,
            ["type_count"] = nodes.Count(node => node.Kind == NodeKind.Type),
            ["method_count"] = nodes.Count(node => node.Kind is NodeKind.Method or NodeKind.Constructor)
        };
    }

    private static string RunGit(string arguments, string workingDirectory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return string.Empty;

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
        var regexPattern = "^"
            + Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal)
            + "$";

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
