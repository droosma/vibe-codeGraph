using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Snapshots;

namespace CodeGraph.Indexer.Tests.Snapshots;

public sealed class SnapshotManagerMutationTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _graphDir;
    private readonly SnapshotManager _manager;

    public SnapshotManagerMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "snapshot-extra-" + Guid.NewGuid().ToString("N"));
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
    public void Constructor_WhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SnapshotManager("  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveAsync_WhitespaceSnapshotName_ThrowsArgumentException(string snapshotName)
    {
        await WriteGraphAsync(_graphDir, "commit-a");
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.SaveAsync(snapshotName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Delete_WhitespaceSnapshotName_ThrowsArgumentException(string snapshotName)
    {
        Assert.Throws<ArgumentException>(() => _manager.Delete(snapshotName));
    }

    [Fact]
    public async Task SaveAsync_CopiesOnlyTopLevelJsonFiles_WithExactContents()
    {
        await WriteGraphAsync(_graphDir, "commit-a");
        File.WriteAllText(Path.Combine(_graphDir, "settings.json"), "{\"setting\":1}");
        Directory.CreateDirectory(Path.Combine(_graphDir, "nested"));
        File.WriteAllText(Path.Combine(_graphDir, "nested", "ignored.json"), "{\"ignored\":true}");
        File.WriteAllText(Path.Combine(_graphDir, "graph.db"), "db-content");

        await _manager.SaveAsync("v1");

        var snapshotDir = _manager.GetSnapshotPath("v1");
        Assert.Equal("{\"setting\":1}", File.ReadAllText(Path.Combine(snapshotDir, "settings.json")));
        Assert.Equal("db-content", File.ReadAllText(Path.Combine(snapshotDir, "graph.db")));
        Assert.False(File.Exists(Path.Combine(snapshotDir, "nested", "ignored.json")));
    }

    [Fact]
    public async Task SaveAsync_OverwriteRemovesFilesThatNoLongerExistInGraphDirectory()
    {
        await WriteGraphAsync(_graphDir, "commit-a");
        File.WriteAllText(Path.Combine(_graphDir, "alpha.json"), "{\"value\":\"alpha\"}");
        await _manager.SaveAsync("stable");

        File.Delete(Path.Combine(_graphDir, "alpha.json"));
        File.WriteAllText(Path.Combine(_graphDir, "beta.json"), "{\"value\":\"beta\"}");
        await _manager.SaveAsync("stable");

        var snapshotDir = _manager.GetSnapshotPath("stable");
        Assert.False(File.Exists(Path.Combine(snapshotDir, "alpha.json")));
        Assert.Equal("{\"value\":\"beta\"}", File.ReadAllText(Path.Combine(snapshotDir, "beta.json")));
    }

    [Fact]
    public async Task List_UsesMetaJsonWriteTimeAsCreatedAt()
    {
        await WriteGraphAsync(_graphDir, "commit-a");
        await _manager.SaveAsync("timed");

        var snapshotDir = _manager.GetSnapshotPath("timed");
        var expectedUtc = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(snapshotDir, "meta.json"), expectedUtc);

        var snapshot = Assert.Single(_manager.List());
        Assert.Equal(expectedUtc, snapshot.CreatedAt.UtcDateTime);
        Assert.Equal(snapshotDir, snapshot.Path);
    }

    private static async Task WriteGraphAsync(string directory, string commitHash)
    {
        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = commitHash,
            Solution = "Snapshot.sln",
            SolutionName = "Snapshot"
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

        await new GraphWriter().WriteAsync(directory, nodes, Array.Empty<GraphEdge>(), metadata);
    }
}
