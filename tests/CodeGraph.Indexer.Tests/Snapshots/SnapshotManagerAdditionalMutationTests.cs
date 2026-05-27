using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Snapshots;

namespace CodeGraph.Indexer.Tests.Snapshots;

public sealed class SnapshotManagerAdditionalMutationTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _graphDir;
    private readonly SnapshotManager _manager;

    public SnapshotManagerAdditionalMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "snapshot-more-" + Guid.NewGuid().ToString("N"));
        _graphDir = Path.Combine(_rootDir, ".codegraph");
        Directory.CreateDirectory(_graphDir);
        _manager = new SnapshotManager(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_MissingGraphDb_DoesNotCreateDatabaseFileInSnapshot()
    {
        await WriteGraphAsync();

        await _manager.SaveAsync("no-db");

        Assert.False(File.Exists(Path.Combine(_manager.GetSnapshotPath("no-db"), "graph.db")));
    }

    [Fact]
    public void GetSnapshotPath_WhitespaceName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _manager.GetSnapshotPath(" "));
    }

    [Fact]
    public void List_InvalidSnapshotDirectoriesOnly_ReturnsEmpty()
    {
        var snapshotsRoot = Path.Combine(_graphDir, "snapshots");
        Directory.CreateDirectory(Path.Combine(snapshotsRoot, "broken-a"));
        Directory.CreateDirectory(Path.Combine(snapshotsRoot, "broken-b"));

        Assert.Empty(_manager.List());
    }

    private async Task WriteGraphAsync()
    {
        var metadata = new GraphMetadata
        {
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            Solution = "Sample.sln",
            SolutionName = "Sample"
        };
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Demo.Type",
                Name = "Type",
                Kind = NodeKind.Type,
                AssemblyName = "Demo",
                FilePath = "Type.cs"
            }
        };

        await new GraphWriter().WriteAsync(_graphDir, nodes, Array.Empty<GraphEdge>(), metadata);
    }
}
