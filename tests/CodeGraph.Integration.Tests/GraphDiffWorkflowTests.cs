using System.Diagnostics;
using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;

namespace CodeGraph.Integration.Tests;

public class GraphDiffWorkflowTests : IDisposable
{
    private readonly string _workDir;

    public GraphDiffWorkflowTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"codegraph-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public async Task SnapshotCommandsAndDiff_EndToEndWorkflow_Succeeds()
    {
        var graphDir = Path.Combine(_workDir, ".codegraph");
        await WriteGraphAsync(graphDir, "aaaaaaa", "Sample.Service", Array.Empty<string>());

        var saveResult = await RunCliAsync("snapshot", "save", "main");
        Assert.Equal(0, saveResult.ExitCode);
        Assert.Contains("Snapshot 'main' saved.", saveResult.StdOut);
        Assert.True(File.Exists(Path.Combine(graphDir, "snapshots", "main", "graph.db")));

        var listResult = await RunCliAsync("snapshot", "list");
        Assert.Equal(0, listResult.ExitCode);
        Assert.Contains("main", listResult.StdOut);

        await WriteGraphAsync(graphDir, "bbbbbbb", "Sample.NewService", new[] { "Sample.NewService.DoWork" });

        var diffResult = await RunCliAsync("diff", "--ref", "main", "--format", "text");
        Assert.Equal(0, diffResult.ExitCode);
        Assert.Contains("Graph Diff aaaaaaa..bbbbbbb", diffResult.StdOut);
        Assert.Contains("Added nodes: 2", diffResult.StdOut);
        Assert.Contains("Removed nodes: 1", diffResult.StdOut);

        var deleteResult = await RunCliAsync("snapshot", "delete", "main");
        Assert.Equal(0, deleteResult.ExitCode);
        Assert.Contains("Snapshot 'main' deleted.", deleteResult.StdOut);
        Assert.False(Directory.Exists(Path.Combine(graphDir, "snapshots", "main")));
    }

    private async Task WriteGraphAsync(string graphDir, string commitHash, string typeId, IReadOnlyList<string> methodIds)
    {
        Directory.CreateDirectory(graphDir);

        foreach (var jsonFile in Directory.GetFiles(graphDir, "*.json", SearchOption.TopDirectoryOnly))
            File.Delete(jsonFile);

        var dbPath = Path.Combine(graphDir, "graph.db");
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        var metadata = new GraphMetadata
        {
            SchemaVersion = 1,
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = "Sample.sln",
            SolutionName = "Sample",
            ProjectsIndexed = new[] { "Sample" }
        };

        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = typeId,
                Name = typeId.Split('.').Last(),
                Kind = NodeKind.Type,
                FilePath = "Sample.cs",
                AssemblyName = "Sample"
            }
        };

        var edges = new List<GraphEdge>();
        foreach (var methodId in methodIds)
        {
            nodes.Add(new GraphNode
            {
                Id = methodId,
                Name = methodId.Split('.').Last(),
                Kind = NodeKind.Method,
                FilePath = "Sample.cs",
                AssemblyName = "Sample",
                ContainingTypeId = typeId
            });
            edges.Add(new GraphEdge
            {
                FromId = typeId,
                ToId = methodId,
                Type = EdgeType.Contains
            });
        }

        var writer = new GraphWriter();
        await writer.WriteAsync(graphDir, nodes, edges, metadata);

        var sqliteWriter = new SqliteGraphWriter();
        await sqliteWriter.WriteAsync(Path.Combine(graphDir, "graph.db"), nodes, edges, metadata);
    }

    private async Task<CommandResult> RunCliAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(GetIndexerDllPath());
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start codegraph process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string GetIndexerDllPath()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = baseDirectory.Name;
        var current = baseDirectory;

        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CodeGraph.sln")))
            current = current.Parent;

        var repoRoot = current?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var dllPath = Path.Combine(repoRoot, "src", "CodeGraph.Indexer", "bin", configuration, tfm, "CodeGraph.Indexer.dll");
            if (File.Exists(dllPath))
                return dllPath;
        }

        throw new FileNotFoundException($"Could not find CodeGraph.Indexer.dll for {tfm}.");
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
