using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class GraphReaderErrorTests : IDisposable
{
    private readonly string _testDir;

    public GraphReaderErrorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task ReadAsync_MissingMetaJson_ThrowsFileNotFound()
    {
        var reader = new GraphReader();
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => reader.ReadAsync(_testDir));
        Assert.Contains("meta.json", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_InvalidSchemaVersion_Throws()
    {
        var meta = new GraphMetadata { SchemaVersion = 999 };
        var metaJson = JsonSerializer.Serialize(meta, GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "meta.json"), metaJson);

        var reader = new GraphReader();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(_testDir));
    }

    [Fact]
    public async Task ReadAsync_NullDeserializedProjectGraph_IsSkipped()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        var metaJson = JsonSerializer.Serialize(meta, GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "meta.json"), metaJson);

        await File.WriteAllTextAsync(Path.Combine(_testDir, "project1.json"), "null");

        var reader = new GraphReader();
        var (_, nodes, edges) = await reader.ReadAsync(_testDir);

        Assert.Empty(nodes);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task ReadAsync_ValidData_ReturnsCorrectCounts()
    {
        var meta = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "test123",
            Branch = "main"
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var pg = new ProjectGraph
        {
            ProjectOrNamespace = "TestProj",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["TestProj.A"] = new() { Id = "TestProj.A", Name = "A", Kind = NodeKind.Type },
                ["TestProj.B"] = new() { Id = "TestProj.B", Name = "B", Kind = NodeKind.Type },
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "TestProj.A", ToId = "TestProj.B", Type = EdgeType.Calls }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "TestProj.json"),
            JsonSerializer.Serialize(pg, GraphSerializationOptions.Default));

        var reader = new GraphReader();
        var (readMeta, nodes, edges) = await reader.ReadAsync(_testDir);

        Assert.Equal("test123", readMeta.CommitHash);
        Assert.Equal("main", readMeta.Branch);
        Assert.Equal(2, nodes.Count);
        Assert.Single(edges);
    }

    [Fact]
    public async Task ReadAsync_MetaJsonExcludedFromProjectFiles()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var reader = new GraphReader();
        var (_, nodes, _) = await reader.ReadAsync(_testDir);
        Assert.Empty(nodes);
    }

    [Fact]
    public async Task ReadAsync_MultipleProjectFiles_MergesAll()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var pg1 = new ProjectGraph
        {
            ProjectOrNamespace = "Proj1",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["Proj1.A"] = new() { Id = "Proj1.A", Name = "A", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "Proj1.A", ToId = "Proj2.B", Type = EdgeType.Calls }
            }
        };

        var pg2 = new ProjectGraph
        {
            ProjectOrNamespace = "Proj2",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["Proj2.B"] = new() { Id = "Proj2.B", Name = "B", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>()
        };

        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "Proj1.json"),
            JsonSerializer.Serialize(pg1, GraphSerializationOptions.Default));
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "Proj2.json"),
            JsonSerializer.Serialize(pg2, GraphSerializationOptions.Default));

        var reader = new GraphReader();
        var (_, nodes, edges) = await reader.ReadAsync(_testDir);

        Assert.Equal(2, nodes.Count);
        Assert.Single(edges);
    }
}
