using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class GraphWriterStrategyTests : IDisposable
{
    private readonly string _outputDir;

    public GraphWriterStrategyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"codegraph-writer-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    private static GraphMetadata MakeMetadata() => new()
    {
        CommitHash = "abc",
        Branch = "main",
        GeneratedAt = DateTimeOffset.UtcNow,
        IndexerVersion = "1.0.0",
        Solution = "Test.sln",
        ProjectsIndexed = new[] { "TestProj" }
    };

    [Fact]
    public async Task WriteAsync_ByNamespace_GroupsByNamespace()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder", Kind = NodeKind.Method },
            new() { Id = "MyApp.Services.OrderService.CancelOrder", Name = "CancelOrder", Kind = NodeKind.Method },
            new() { Id = "MyApp.Repos.OrderRepo.Save", Name = "Save", Kind = NodeKind.Method },
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Repos.OrderRepo.Save", Type = EdgeType.Calls }
        };

        var writer = new GraphWriter(SplitFileStrategy.ByNamespace);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "MyApp.Services.OrderService.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "MyApp.Repos.OrderRepo.json")));
    }

    [Fact]
    public async Task WriteAsync_ByAssembly_GroupsByAssemblyName()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "MyApp.ClassA", Name = "ClassA", Kind = NodeKind.Type, AssemblyName = "MyApp.Core" },
            new() { Id = "MyApp.ClassB", Name = "ClassB", Kind = NodeKind.Type, AssemblyName = "MyApp.Core" },
            new() { Id = "Other.ClassC", Name = "ClassC", Kind = NodeKind.Type, AssemblyName = "Other.Lib" },
        };
        var edges = new List<GraphEdge>();

        var writer = new GraphWriter(SplitFileStrategy.ByAssembly);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "MyApp.Core.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "Other.Lib.json")));
    }

    [Fact]
    public async Task WriteAsync_ByAssembly_ExternalNodes_GoToExternalKey()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "MyApp.ClassA", Name = "ClassA", Kind = NodeKind.Type, AssemblyName = "MyApp" },
            new()
            {
                Id = "Newtonsoft.Json.JsonConvert",
                Name = "JsonConvert",
                Kind = NodeKind.Type,
                FilePath = "",
                AssemblyName = "",
                Metadata = new Dictionary<string, string> { ["assembly"] = "Newtonsoft.Json" }
            },
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.ClassA", ToId = "Newtonsoft.Json.JsonConvert", Type = EdgeType.Calls }
        };

        var writer = new GraphWriter(SplitFileStrategy.ByAssembly);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "_external.json")));

        var externalJson = await File.ReadAllTextAsync(Path.Combine(_outputDir, "_external.json"));
        var externalGraph = JsonSerializer.Deserialize<ProjectGraph>(externalJson, GraphSerializationOptions.Default);
        Assert.NotNull(externalGraph);
        Assert.Contains("Newtonsoft.Json.JsonConvert", externalGraph.Nodes.Keys);
        Assert.Equal("_external", externalGraph.ProjectOrNamespace);
    }

    [Fact]
    public async Task WriteAsync_ByAssembly_NoAssemblyName_FallsBackToExtractProject()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "FallbackProject.SomeClass", Name = "SomeClass", Kind = NodeKind.Type, AssemblyName = "" },
        };
        var edges = new List<GraphEdge>();

        var writer = new GraphWriter(SplitFileStrategy.ByAssembly);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "FallbackProject.json")));
    }

    [Fact]
    public async Task WriteAsync_ByProject_GroupsByFirstSegment()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "Alpha.ClassA", Name = "ClassA", Kind = NodeKind.Type },
            new() { Id = "Beta.ClassB", Name = "ClassB", Kind = NodeKind.Type },
        };
        var edges = new List<GraphEdge>();

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "Alpha.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "Beta.json")));
    }

    [Fact]
    public async Task WriteAsync_EdgeAssignedToSourceNodeProject()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "ProjA.Class1", Name = "Class1", Kind = NodeKind.Type },
            new() { Id = "ProjB.Class2", Name = "Class2", Kind = NodeKind.Type },
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "ProjA.Class1", ToId = "ProjB.Class2", Type = EdgeType.Calls },
            new() { FromId = "ProjB.Class2", ToId = "ProjA.Class1", Type = EdgeType.Inherits },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        var projAJson = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ProjA.json"));
        var projA = JsonSerializer.Deserialize<ProjectGraph>(projAJson, GraphSerializationOptions.Default)!;
        Assert.Single(projA.Edges);
        Assert.Equal("ProjA.Class1", projA.Edges[0].FromId);

        var projBJson = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ProjB.json"));
        var projB = JsonSerializer.Deserialize<ProjectGraph>(projBJson, GraphSerializationOptions.Default)!;
        Assert.Single(projB.Edges);
        Assert.Equal("ProjB.Class2", projB.Edges[0].FromId);
    }

    [Fact]
    public async Task WriteAsync_EdgeWithUnknownSource_GoesToFallback()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "ProjA.Class1", Name = "Class1", Kind = NodeKind.Type },
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Unknown.Source", ToId = "ProjA.Class1", Type = EdgeType.Calls },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        var projAJson = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ProjA.json"));
        var projA = JsonSerializer.Deserialize<ProjectGraph>(projAJson, GraphSerializationOptions.Default)!;
        Assert.Contains(projA.Edges, e => e.FromId == "Unknown.Source");
    }

    [Fact]
    public async Task WriteAsync_MetaJsonIsWritten()
    {
        var metadata = MakeMetadata();
        var writer = new GraphWriter();
        await writer.WriteAsync(_outputDir, Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), metadata);

        var metaPath = Path.Combine(_outputDir, "meta.json");
        Assert.True(File.Exists(metaPath));

        var metaJson = await File.ReadAllTextAsync(metaPath);
        var deserialized = JsonSerializer.Deserialize<GraphMetadata>(metaJson, GraphSerializationOptions.Default);
        Assert.NotNull(deserialized);
        Assert.Equal("abc", deserialized.CommitHash);
    }

    [Fact]
    public async Task WriteAsync_SanitizesInvalidFileNameChars()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "Bad<Name>.Class1", Name = "Class1", Kind = NodeKind.Type },
        };
        var edges = new List<GraphEdge>();

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, edges, MakeMetadata());

        var files = Directory.GetFiles(_outputDir, "*.json")
            .Select(Path.GetFileName)
            .Where(f => f != "meta.json")
            .ToList();

        Assert.Single(files);
        Assert.DoesNotContain("<", files[0]);
        Assert.DoesNotContain(">", files[0]);
    }

    [Fact]
    public async Task WriteAsync_ByNamespace_ExtractsNamespaceCorrectly()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A.B.C.Method", Name = "Method", Kind = NodeKind.Method },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByNamespace);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "A.B.C.json")));
    }

    [Fact]
    public async Task WriteAsync_ByProject_ExtractsFirstSegment()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "MyApp.Services.OrderService", Name = "OrderService", Kind = NodeKind.Type },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "MyApp.json")));
    }

    [Fact]
    public async Task WriteAsync_ProjectGraphHasCorrectProjectOrNamespace()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "TestProj.MyClass", Name = "MyClass", Kind = NodeKind.Type },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "TestProj.json"));
        var pg = JsonSerializer.Deserialize<ProjectGraph>(json, GraphSerializationOptions.Default)!;
        Assert.Equal("TestProj", pg.ProjectOrNamespace);
    }

    [Fact]
    public async Task WriteAsync_EmptyGroupKey_BecomesDefault()
    {
        // Node with empty assembly and metadata containing "assembly" key -> _external
        // But empty FilePath + no assembly metadata + empty AssemblyName -> ExtractProject
        // A node with Id "" would have empty ExtractProject -> "_default" key
        var nodes = new List<GraphNode>
        {
            new() { Id = "", Name = "Empty", Kind = NodeKind.Type, AssemblyName = "" },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        // Empty Id -> ExtractProject returns "" -> key becomes "_default"
        Assert.True(File.Exists(Path.Combine(_outputDir, "_default.json")));
    }

    [Fact]
    public async Task WriteAsync_SingleSegmentId_ByNamespace_UsesWholeId()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "SingleSegment", Name = "SingleSegment", Kind = NodeKind.Type },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByNamespace);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        // LastIndexOf('.') returns -1, so whole id returned
        Assert.True(File.Exists(Path.Combine(_outputDir, "SingleSegment.json")));
    }

    [Fact]
    public async Task WriteAsync_SingleSegmentId_ByProject_UsesWholeId()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "SingleSegment", Name = "SingleSegment", Kind = NodeKind.Type },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByProject);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        Assert.True(File.Exists(Path.Combine(_outputDir, "SingleSegment.json")));
    }

    [Fact]
    public void Constructor_DefaultStrategy_IsByAssembly()
    {
        // Default constructor uses ByAssembly - just verify it doesn't throw
        var writer = new GraphWriter();
        Assert.NotNull(writer);
    }

    [Fact]
    public async Task WriteAsync_ByAssembly_ExternalNodeWithFilePath_NotExternal()
    {
        // Node has assembly metadata but also has a FilePath -> NOT external
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "Lib.SomeClass",
                Name = "SomeClass",
                Kind = NodeKind.Type,
                FilePath = "src/Lib/SomeClass.cs",
                AssemblyName = "Lib",
                Metadata = new Dictionary<string, string> { ["assembly"] = "Lib" }
            },
        };

        var writer = new GraphWriter(SplitFileStrategy.ByAssembly);
        await writer.WriteAsync(_outputDir, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        // Should be in "Lib" group, not "_external"
        Assert.True(File.Exists(Path.Combine(_outputDir, "Lib.json")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "_external.json")));
    }
}
