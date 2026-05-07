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
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => GraphReader.ReadAsync(_testDir));
        Assert.Contains("meta.json", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_InvalidSchemaVersion_Throws()
    {
        var meta = new GraphMetadata { SchemaVersion = 999 };
        var metaJson = JsonSerializer.Serialize(meta, GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "meta.json"), metaJson);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GraphReader.ReadAsync(_testDir));
    }

    [Fact]
    public async Task ReadAsync_NullDeserializedProjectGraph_IsSkipped()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        var metaJson = JsonSerializer.Serialize(meta, GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "meta.json"), metaJson);

        await File.WriteAllTextAsync(Path.Combine(_testDir, "project1.json"), "null");

        var (_, nodes, edges) = await GraphReader.ReadAsync(_testDir);

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

        var (readMeta, nodes, edges) = await GraphReader.ReadAsync(_testDir);

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

        var (_, nodes, _) = await GraphReader.ReadAsync(_testDir);
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

        var (_, nodes, edges) = await GraphReader.ReadAsync(_testDir);

        Assert.Equal(2, nodes.Count);
        Assert.Single(edges);
    }

    [Fact]
    public async Task ReadAsync_MalformedProjectJson_ThrowsJsonException()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        await File.WriteAllTextAsync(Path.Combine(_testDir, "broken.json"), "{ invalid json!!! }");

        await Assert.ThrowsAsync<JsonException>(() => GraphReader.ReadAsync(_testDir));
    }

    [Fact]
    public async Task ReadAsync_EmptyProjectJson_SkipsGracefully()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        // Empty JSON object — valid but has empty nodes/edges
        await File.WriteAllTextAsync(Path.Combine(_testDir, "empty.json"), "{}");

        var (_, nodes, edges) = await GraphReader.ReadAsync(_testDir);

        Assert.Empty(nodes);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task ReadAsync_MetadataFieldsPreserved()
    {
        var meta = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "deadbeef",
            Branch = "feature/test",
            IndexerVersion = "2.5.0",
            Solution = "MyApp.sln",
            SolutionName = "MyApp",
            ProjectsIndexed = new[] { "P1", "P2", "P3" }
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var (readMeta, _, _) = await GraphReader.ReadAsync(_testDir);

        Assert.Equal("deadbeef", readMeta.CommitHash);
        Assert.Equal("feature/test", readMeta.Branch);
        Assert.Equal("2.5.0", readMeta.IndexerVersion);
        Assert.Equal("MyApp.sln", readMeta.Solution);
        Assert.Equal("MyApp", readMeta.SolutionName);
        Assert.Equal(new[] { "P1", "P2", "P3" }, readMeta.ProjectsIndexed);
    }

    [Fact]
    public async Task ReadAsync_EdgePropertiesPreserved()
    {
        var meta = new GraphMetadata { SchemaVersion = GraphSchema.CurrentVersion };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var pg = new ProjectGraph
        {
            ProjectOrNamespace = "Test",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
                ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>
            {
                new()
                {
                    FromId = "A", ToId = "B", Type = EdgeType.Implements,
                    IsExternal = true, PackageSource = "NuGet",
                    SourceLink = "https://src", Resolution = "resolved",
                    Confidence = EdgeConfidence.Unresolved,
                    Metadata = new Dictionary<string, string> { ["key1"] = "val1" }
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "Test.json"),
            JsonSerializer.Serialize(pg, GraphSerializationOptions.Default));

        var (_, _, edges) = await GraphReader.ReadAsync(_testDir);

        Assert.Single(edges);
        var e = edges[0];
        Assert.Equal("A", e.FromId);
        Assert.Equal("B", e.ToId);
        Assert.Equal(EdgeType.Implements, e.Type);
        Assert.True(e.IsExternal);
        Assert.Equal("NuGet", e.PackageSource);
        Assert.Equal("https://src", e.SourceLink);
        Assert.Equal("resolved", e.Resolution);
        Assert.Equal(EdgeConfidence.Unresolved, e.Confidence);
        Assert.Equal("val1", e.Metadata["key1"]);
    }
}
