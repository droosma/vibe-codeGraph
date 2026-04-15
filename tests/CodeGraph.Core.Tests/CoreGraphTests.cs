using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests;

public class GraphSchemaTests
{
    [Fact]
    public void Validate_CorrectVersion_DoesNotThrow()
    {
        GraphSchema.Validate(GraphSchema.CurrentVersion);
    }

    [Fact]
    public void Validate_WrongVersion_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GraphSchema.Validate(999));
        Assert.Contains("schema version mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class JsonRoundTripTests
{
    private static readonly JsonSerializerOptions Options = GraphSerializationOptions.Default;

    [Fact]
    public void GraphNode_RoundTrips()
    {
        var node = new GraphNode
        {
            Id = "MyApp.Services.OrderService.PlaceOrder",
            Name = "PlaceOrder",
            Kind = NodeKind.Method,
            FilePath = "src/Services/OrderService.cs",
            StartLine = 10,
            EndLine = 25,
            Signature = "public async Task<bool> PlaceOrder(Order order)",
            DocComment = "Places an order",
            ContainingTypeId = "MyApp.Services.OrderService",
            ContainingNamespaceId = "MyApp.Services",
            Accessibility = Accessibility.Public,
            Metadata = new Dictionary<string, string> { ["isAsync"] = "true" }
        };

        var json = JsonSerializer.Serialize(node, Options);
        var deserialized = JsonSerializer.Deserialize<GraphNode>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(node.Id, deserialized.Id);
        Assert.Equal(node.Name, deserialized.Name);
        Assert.Equal(node.Kind, deserialized.Kind);
        Assert.Equal(node.FilePath, deserialized.FilePath);
        Assert.Equal(node.StartLine, deserialized.StartLine);
        Assert.Equal(node.EndLine, deserialized.EndLine);
        Assert.Equal(node.Signature, deserialized.Signature);
        Assert.Equal(node.DocComment, deserialized.DocComment);
        Assert.Equal(node.ContainingTypeId, deserialized.ContainingTypeId);
        Assert.Equal(node.Accessibility, deserialized.Accessibility);
        Assert.Equal(node.Metadata["isAsync"], deserialized.Metadata["isAsync"]);
    }

    [Fact]
    public void GraphEdge_RoundTrips()
    {
        var edge = new GraphEdge
        {
            FromId = "MyApp.Services.OrderService.PlaceOrder",
            ToId = "MyApp.Repositories.OrderRepo.Save",
            Type = EdgeType.Calls,
            IsExternal = false,
            PackageSource = null,
            Resolution = "static",
            Metadata = new Dictionary<string, string> { ["weight"] = "1" }
        };

        var json = JsonSerializer.Serialize(edge, Options);
        var deserialized = JsonSerializer.Deserialize<GraphEdge>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(edge.FromId, deserialized.FromId);
        Assert.Equal(edge.ToId, deserialized.ToId);
        Assert.Equal(edge.Type, deserialized.Type);
        Assert.Equal(edge.IsExternal, deserialized.IsExternal);
        Assert.Equal(edge.Resolution, deserialized.Resolution);
        Assert.Equal(edge.Metadata["weight"], deserialized.Metadata["weight"]);
    }

    [Fact]
    public void GraphMetadata_RoundTrips()
    {
        var meta = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            IndexerVersion = "1.0.0",
            Solution = "MyApp.sln",
            ProjectsIndexed = new[] { "MyApp", "MyApp.Tests" },
            Stats = new Dictionary<string, int> { ["nodes"] = 100, ["edges"] = 250 }
        };

        var json = JsonSerializer.Serialize(meta, Options);
        var deserialized = JsonSerializer.Deserialize<GraphMetadata>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(meta.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(meta.CommitHash, deserialized.CommitHash);
        Assert.Equal(meta.Branch, deserialized.Branch);
        Assert.Equal(meta.GeneratedAt, deserialized.GeneratedAt);
        Assert.Equal(meta.ProjectsIndexed, deserialized.ProjectsIndexed);
        Assert.Equal(meta.Stats["nodes"], deserialized.Stats["nodes"]);
    }

    [Fact]
    public void ProjectGraph_RoundTrips()
    {
        var graph = new ProjectGraph
        {
            ProjectOrNamespace = "MyApp",
            Nodes = new Dictionary<string, GraphNode>
            {
                ["MyApp.Foo"] = new GraphNode { Id = "MyApp.Foo", Name = "Foo", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>
            {
                new GraphEdge { FromId = "MyApp.Foo", ToId = "MyApp.Bar", Type = EdgeType.DependsOn }
            }
        };

        var json = JsonSerializer.Serialize(graph, Options);
        var deserialized = JsonSerializer.Deserialize<ProjectGraph>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("MyApp", deserialized.ProjectOrNamespace);
        Assert.Single(deserialized.Nodes);
        Assert.Single(deserialized.Edges);
        Assert.Equal("MyApp.Foo", deserialized.Nodes["MyApp.Foo"].Id);
    }
}

public class GraphWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesSeparateFilesPerProject()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"codegraph-test-{Guid.NewGuid():N}");
        try
        {
            var nodes = new List<GraphNode>
            {
                new() { Id = "ProjectA.ClassA", Name = "ClassA", Kind = NodeKind.Type },
                new() { Id = "ProjectA.ClassA.Method1", Name = "Method1", Kind = NodeKind.Method },
                new() { Id = "ProjectB.ClassB", Name = "ClassB", Kind = NodeKind.Type },
            };

            var edges = new List<GraphEdge>
            {
                new() { FromId = "ProjectA.ClassA.Method1", ToId = "ProjectB.ClassB", Type = EdgeType.DependsOn },
                new() { FromId = "ProjectB.ClassB", ToId = "ProjectA.ClassA", Type = EdgeType.Inherits },
            };

            var metadata = new GraphMetadata
            {
                CommitHash = "abc",
                Branch = "main",
                GeneratedAt = DateTimeOffset.UtcNow,
                IndexerVersion = "1.0.0",
                Solution = "Test.sln",
                ProjectsIndexed = new[] { "ProjectA", "ProjectB" }
            };

            var writer = new GraphWriter();
            await writer.WriteAsync(outputDir, nodes, edges, metadata);

            Assert.True(File.Exists(Path.Combine(outputDir, "meta.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "ProjectA.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "ProjectB.json")));

            // Verify content
            var projectAJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "ProjectA.json"));
            var projectA = JsonSerializer.Deserialize<ProjectGraph>(projectAJson, GraphSerializationOptions.Default);
            Assert.NotNull(projectA);
            Assert.Equal(2, projectA.Nodes.Count); // ClassA + Method1
            Assert.Single(projectA.Edges); // Method1 → ClassB
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }
}

public class GraphReaderTests
{
    [Fact]
    public async Task ReadAsync_LoadsUnifiedGraph()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"codegraph-test-{Guid.NewGuid():N}");
        try
        {
            var nodes = new List<GraphNode>
            {
                new() { Id = "ProjectA.ClassA", Name = "ClassA", Kind = NodeKind.Type },
                new() { Id = "ProjectB.ClassB", Name = "ClassB", Kind = NodeKind.Type },
            };

            var edges = new List<GraphEdge>
            {
                new() { FromId = "ProjectA.ClassA", ToId = "ProjectB.ClassB", Type = EdgeType.Calls },
            };

            var metadata = new GraphMetadata
            {
                CommitHash = "def",
                Branch = "dev",
                GeneratedAt = DateTimeOffset.UtcNow,
                IndexerVersion = "1.0.0",
                Solution = "Test.sln",
                ProjectsIndexed = new[] { "ProjectA", "ProjectB" }
            };

            var writer = new GraphWriter();
            await writer.WriteAsync(outputDir, nodes, edges, metadata);

            var reader = new GraphReader();
            var (readMeta, readNodes, readEdges) = await reader.ReadAsync(outputDir);

            Assert.Equal(GraphSchema.CurrentVersion, readMeta.SchemaVersion);
            Assert.Equal("def", readMeta.CommitHash);
            Assert.Equal(2, readNodes.Count);
            Assert.Single(readEdges);
            Assert.True(readNodes.ContainsKey("ProjectA.ClassA"));
            Assert.True(readNodes.ContainsKey("ProjectB.ClassB"));
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task ReadAsync_ThrowsOnMissingMeta()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"codegraph-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var reader = new GraphReader();
            await Assert.ThrowsAsync<FileNotFoundException>(() => reader.ReadAsync(emptyDir));
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }
}

public class GraphMergerTests
{
    [Fact]
    public void Merge_ReplacesUpdatedProjects_KeepsOthers()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            ["ProjectA.ClassA"] = new() { Id = "ProjectA.ClassA", Name = "ClassA", Kind = NodeKind.Type },
            ["ProjectA.ClassA.OldMethod"] = new() { Id = "ProjectA.ClassA.OldMethod", Name = "OldMethod", Kind = NodeKind.Method },
            ["ProjectB.ClassB"] = new() { Id = "ProjectB.ClassB", Name = "ClassB", Kind = NodeKind.Type },
        };

        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = "ProjectA.ClassA.OldMethod", ToId = "ProjectB.ClassB", Type = EdgeType.Calls },
            new() { FromId = "ProjectB.ClassB", ToId = "ProjectA.ClassA", Type = EdgeType.Inherits },
        };

        var partialGraphs = new List<ProjectGraph>
        {
            new()
            {
                ProjectOrNamespace = "ProjectA",
                Nodes = new Dictionary<string, GraphNode>
                {
                    ["ProjectA.ClassA"] = new() { Id = "ProjectA.ClassA", Name = "ClassA", Kind = NodeKind.Type },
                    ["ProjectA.ClassA.NewMethod"] = new() { Id = "ProjectA.ClassA.NewMethod", Name = "NewMethod", Kind = NodeKind.Method },
                },
                Edges = new List<GraphEdge>
                {
                    new() { FromId = "ProjectA.ClassA.NewMethod", ToId = "ProjectB.ClassB", Type = EdgeType.Calls },
                }
            }
        };

        var merger = new GraphMerger();
        var (mergedNodes, mergedEdges) = merger.Merge(existingNodes, existingEdges, partialGraphs);

        // ProjectA nodes replaced: ClassA + NewMethod (OldMethod gone)
        Assert.Equal(3, mergedNodes.Count);
        Assert.True(mergedNodes.ContainsKey("ProjectA.ClassA"));
        Assert.True(mergedNodes.ContainsKey("ProjectA.ClassA.NewMethod"));
        Assert.False(mergedNodes.ContainsKey("ProjectA.ClassA.OldMethod"));

        // ProjectB node preserved
        Assert.True(mergedNodes.ContainsKey("ProjectB.ClassB"));

        // Edges: old ProjectA edge removed, new one added, ProjectB edge kept
        Assert.Equal(2, mergedEdges.Count);
        Assert.Contains(mergedEdges, e => e.FromId == "ProjectA.ClassA.NewMethod");
        Assert.Contains(mergedEdges, e => e.FromId == "ProjectB.ClassB");
    }
}
