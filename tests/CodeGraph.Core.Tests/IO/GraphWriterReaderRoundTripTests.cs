using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class GraphWriterReaderRoundTripTests : IDisposable
{
    private readonly string _outputDir;

    public GraphWriterReaderRoundTripTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"codegraph-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    [Fact]
    public async Task WriteAsyncAndReadAsync_RoundTripPreservesAllNodeAndEdgeFields()
    {
        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "abc123",
            Branch = "feature/mutation-tests",
            GeneratedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            IndexerVersion = "2.0.0",
            Solution = "CodeGraph.sln",
            SolutionName = "CodeGraph",
            ProjectsIndexed = new[] { "Proj" },
            Stats = new Dictionary<string, int> { ["nodeCount"] = 2, ["edgeCount"] = 1 }
        };

        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "Proj.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                FilePath = "src/Proj/Service.cs",
                StartLine = 1,
                EndLine = 20,
                Signature = "public class Service",
                DocComment = "Service docs",
                ContainingNamespaceId = "Proj",
                Accessibility = Accessibility.Public,
                AssemblyName = "Proj.Core",
                Metadata = new Dictionary<string, string> { ["role"] = "entry" }
            },
            new()
            {
                Id = "Proj.Service.Execute",
                Name = "Execute",
                Kind = NodeKind.Method,
                FilePath = "src/Proj/Service.cs",
                StartLine = 5,
                EndLine = 9,
                Signature = "void Execute()",
                ContainingTypeId = "Proj.Service",
                ContainingNamespaceId = "Proj",
                Accessibility = Accessibility.Private,
                AssemblyName = "Proj.Core"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Proj.Service.Execute",
                ToId = "External.Dependency",
                Type = EdgeType.Calls,
                IsExternal = true,
                Confidence = EdgeConfidence.Unresolved,
                Metadata = new Dictionary<string, string> { ["callSite"] = "line 7" }
            }
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, edges, metadata);

        var (readMetadata, readNodes, readEdges) = await GraphReader.ReadAsync(_outputDir);

        Assert.Equal(metadata.CommitHash, readMetadata.CommitHash);
        Assert.Equal(metadata.Branch, readMetadata.Branch);
        Assert.Equal(metadata.GeneratedAt, readMetadata.GeneratedAt);
        Assert.Equal(metadata.IndexerVersion, readMetadata.IndexerVersion);
        Assert.Equal(metadata.Solution, readMetadata.Solution);
        Assert.Equal(metadata.SolutionName, readMetadata.SolutionName);
        Assert.Equal(metadata.ProjectsIndexed, readMetadata.ProjectsIndexed);
        Assert.Equal(2, readMetadata.Stats.Count);
        Assert.Equal(2, readMetadata.Stats["nodeCount"]);
        Assert.Equal(1, readMetadata.Stats["edgeCount"]);

        var typeNode = readNodes["Proj.Service"];
        Assert.Equal("Service", typeNode.Name);
        Assert.Equal(NodeKind.Type, typeNode.Kind);
        Assert.Equal("src/Proj/Service.cs", typeNode.FilePath);
        Assert.Equal(1, typeNode.StartLine);
        Assert.Equal(20, typeNode.EndLine);
        Assert.Equal("public class Service", typeNode.Signature);
        Assert.Equal("Service docs", typeNode.DocComment);
        Assert.Null(typeNode.ContainingTypeId);
        Assert.Equal("Proj", typeNode.ContainingNamespaceId);
        Assert.Equal(Accessibility.Public, typeNode.Accessibility);
        Assert.Equal("Proj.Core", typeNode.AssemblyName);
        Assert.Equal("entry", typeNode.Metadata["role"]);

        var methodNode = readNodes["Proj.Service.Execute"];
        Assert.Equal("Execute", methodNode.Name);
        Assert.Equal(NodeKind.Method, methodNode.Kind);
        Assert.Equal("Proj.Service", methodNode.ContainingTypeId);
        Assert.Null(methodNode.DocComment);
        Assert.Empty(methodNode.Metadata);

        var edge = Assert.Single(readEdges);
        Assert.Equal("Proj.Service.Execute", edge.FromId);
        Assert.Equal("External.Dependency", edge.ToId);
        Assert.Equal(EdgeType.Calls, edge.Type);
        Assert.True(edge.IsExternal);
        Assert.Equal(EdgeConfidence.Unresolved, edge.Confidence);
        Assert.Null(edge.PackageSource);
        Assert.Null(edge.SourceLink);
        Assert.Null(edge.Resolution);
        Assert.Equal("line 7", edge.Metadata["callSite"]);
    }

    [Fact]
    public async Task ReadAsync_UppercaseMetaJson_IsNotTreatedAsProjectGraph()
    {
        var metaJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = GraphSchema.CurrentVersion,
                commitHash = "abc",
                nodes = new Dictionary<string, GraphNode>
                {
                    ["Should.Not.Appear"] = new() { Id = "Should.Not.Appear", Name = "Fake", Kind = NodeKind.Type }
                },
                edges = new[]
                {
                    new GraphEdge { FromId = "Should.Not.Appear", ToId = "Proj.Real", Type = EdgeType.Calls }
                }
            },
            GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "META.JSON"), metaJson);

        var realProject = new ProjectGraph
        {
            ProjectOrNamespace = "Proj",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["Proj.Real"] = new() { Id = "Proj.Real", Name = "Real", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>()
        };
        await File.WriteAllTextAsync(
            Path.Combine(_outputDir, "Proj.json"),
            JsonSerializer.Serialize(realProject, GraphSerializationOptions.Default));

        var (metadata, nodes, edges) = await GraphReader.ReadAsync(_outputDir);

        Assert.Equal("abc", metadata.CommitHash);
        Assert.Single(nodes);
        Assert.True(nodes.ContainsKey("Proj.Real"));
        Assert.DoesNotContain("Should.Not.Appear", nodes.Keys);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task ReadAsync_NullMetaJson_ThrowsInvalidOperationException()
    {
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "meta.json"), "null");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GraphReader.ReadAsync(_outputDir));

        Assert.Contains("Failed to deserialize meta.json", exception.Message);
    }
}
