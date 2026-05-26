using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Snapshots;

namespace CodeGraph.Indexer.Tests.Snapshots;

public class SnapshotManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _graphDir;
    private readonly SnapshotManager _sut;

    public SnapshotManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "codegraph-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _graphDir = Path.Combine(_tempDir, ".codegraph");
        Directory.CreateDirectory(_graphDir);

        _sut = new SnapshotManager(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetSnapshotPath_UsesGraphSnapshotsSubdirectory()
    {
        var snapshotPath = _sut.GetSnapshotPath("main");

        Assert.Equal(Path.Combine(_graphDir, "snapshots", "main"), snapshotPath);
    }

    [Fact]
    public async Task Save_CopiesMetaAndJsonFiles()
    {
        await WriteTestGraph(_graphDir, "abc123");

        await _sut.SaveAsync("v1");

        var snapshotDir = _sut.GetSnapshotPath("v1");
        Assert.True(File.Exists(Path.Combine(snapshotDir, "meta.json")));
        Assert.True(File.Exists(Path.Combine(snapshotDir, "TestProject.json")));
    }

    [Fact]
    public async Task Save_CopiesGraphDb_WhenPresent()
    {
        await WriteTestGraph(_graphDir, "abc123");
        var dbPath = Path.Combine(_graphDir, "graph.db");
        await File.WriteAllTextAsync(dbPath, "fake-db-content");

        await _sut.SaveAsync("with-db");

        var snapshotDir = _sut.GetSnapshotPath("with-db");
        Assert.True(File.Exists(Path.Combine(snapshotDir, "graph.db")));
    }

    [Fact]
    public async Task Save_OverwritesExistingSnapshot()
    {
        await WriteTestGraph(_graphDir, "first-commit");
        await _sut.SaveAsync("snap");

        await WriteTestGraph(_graphDir, "second-commit");
        await _sut.SaveAsync("snap");

        var snapshotDir = _sut.GetSnapshotPath("snap");
        var metaContent = await File.ReadAllTextAsync(Path.Combine(snapshotDir, "meta.json"));
        Assert.Contains("second-commit", metaContent);
        Assert.DoesNotContain("first-commit", metaContent);
    }

    [Fact]
    public async Task Save_ThrowsWhenGraphDirMissing()
    {
        var isolated = new SnapshotManager(Path.Combine(_tempDir, "nonexistent"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => isolated.SaveAsync("snap"));
    }

    [Fact]
    public async Task Save_ThrowsWhenMetaJsonMissing()
    {
        var emptyDir = Path.Combine(_tempDir, "empty-graph");
        Directory.CreateDirectory(emptyDir);
        var isolated = new SnapshotManager(emptyDir);

        await Assert.ThrowsAsync<FileNotFoundException>(() => isolated.SaveAsync("snap"));
    }

    [Fact]
    public async Task List_ReturnsSnapshots_SortedByName()
    {
        await WriteTestGraph(_graphDir, "c1");
        await _sut.SaveAsync("beta");
        await _sut.SaveAsync("alpha");

        var snapshots = _sut.List();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("alpha", snapshots[0].Name);
        Assert.Equal("beta", snapshots[1].Name);
    }

    [Fact]
    public void List_ReturnsEmpty_WhenNoSnapshots()
    {
        var snapshots = _sut.List();

        Assert.Empty(snapshots);
    }

    [Fact]
    public void List_ReturnsEmpty_WhenSnapshotRootMissing()
    {
        var isolated = new SnapshotManager(Path.Combine(_tempDir, "no-such-root"));

        var snapshots = isolated.List();

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task List_SkipsDirectoriesWithoutMeta()
    {
        await WriteTestGraph(_graphDir, "c1");
        await _sut.SaveAsync("valid");

        var bogusDir = Path.Combine(_graphDir, "snapshots", "bogus");
        Directory.CreateDirectory(bogusDir);

        var snapshots = _sut.List();

        Assert.Single(snapshots);
        Assert.Equal("valid", snapshots[0].Name);
    }

    [Fact]
    public async Task List_PopulatesSnapshotInfoFields()
    {
        await WriteTestGraph(_graphDir, "c1");
        await _sut.SaveAsync("info-test");

        var snapshots = _sut.List();
        var snapshot = Assert.Single(snapshots);

        Assert.Equal("info-test", snapshot.Name);
        Assert.NotEqual(default, snapshot.CreatedAt);
        Assert.Contains(Path.Combine(".codegraph", "snapshots"), snapshot.Path);
        Assert.Contains("info-test", snapshot.Path);
    }

    [Fact]
    public async Task Delete_RemovesSnapshot_ReturnsTrue()
    {
        await WriteTestGraph(_graphDir, "c1");
        await _sut.SaveAsync("doomed");

        var result = _sut.Delete("doomed");

        Assert.True(result);
        Assert.Empty(_sut.List());
    }

    [Fact]
    public void Delete_NonExistentSnapshot_ReturnsFalse()
    {
        var result = _sut.Delete("ghost");

        Assert.False(result);
    }

    [Fact]
    public async Task Delete_DoesNotAffectOtherSnapshots()
    {
        await WriteTestGraph(_graphDir, "c1");
        await _sut.SaveAsync("keep");
        await _sut.SaveAsync("remove");

        _sut.Delete("remove");

        var remaining = _sut.List();
        Assert.Single(remaining);
        Assert.Equal("keep", remaining[0].Name);
    }

    [Fact]
    public async Task RoundTrip_SnapshotCanBeReadByGraphReader()
    {
        await WriteTestGraph(_graphDir, "roundtrip-hash");
        await _sut.SaveAsync("readable");

        var snapshotDir = _sut.GetSnapshotPath("readable");
        var (metadata, nodes, edges) = await GraphReader.ReadAsync(snapshotDir);

        Assert.Equal("roundtrip-hash", metadata.CommitHash);
        Assert.True(nodes.Count > 0);
    }

    private static async Task WriteTestGraph(string dir, string commitHash)
    {
        var metadata = new GraphMetadata
        {
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var nodes = new Dictionary<string, GraphNode>
        {
            ["TestNs.TestClass"] = new()
            {
                Id = "TestNs.TestClass",
                Name = "TestClass",
                Kind = NodeKind.Type,
                FilePath = "TestClass.cs",
                AssemblyName = "TestProject"
            }
        };

        var edges = new List<GraphEdge>();

        var writer = new GraphWriter();
        await writer.WriteAsync(dir, nodes.Values, edges, metadata);
    }
}
