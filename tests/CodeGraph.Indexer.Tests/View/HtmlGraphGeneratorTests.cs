using System.Text.Json;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.View;

namespace CodeGraph.Indexer.Tests.View;

public class HtmlGraphGeneratorTests : IDisposable
{
    private readonly string _testDir;

    public HtmlGraphGeneratorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-view-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private async Task WriteTestGraph(int nodeCount = 5, int edgeCount = 2)
    {
        var meta = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "abc123",
            Branch = "main",
            Solution = "Test.sln",
            SolutionName = "Test"
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "meta.json"),
            JsonSerializer.Serialize(meta, GraphSerializationOptions.Default));

        var nodes = new Dictionary<string, GraphNode>();
        for (int i = 0; i < nodeCount; i++)
        {
            var id = $"TestProject.Class{i}";
            nodes[id] = new GraphNode
            {
                Id = id,
                Name = $"Class{i}",
                Kind = i % 2 == 0 ? NodeKind.Type : NodeKind.Method,
                AssemblyName = "TestProject",
                Accessibility = Accessibility.Public
            };
        }

        var edges = new List<GraphEdge>();
        for (int i = 0; i < edgeCount && i + 1 < nodeCount; i++)
        {
            edges.Add(new GraphEdge
            {
                FromId = $"TestProject.Class{i}",
                ToId = $"TestProject.Class{i + 1}",
                Type = EdgeType.Calls
            });
        }

        var pg = new ProjectGraph
        {
            ProjectOrNamespace = "TestProject",
            Nodes = nodes,
            Edges = edges
        };
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "TestProject.json"),
            JsonSerializer.Serialize(pg, GraphSerializationOptions.Default));
    }

    [Fact]
    public async Task GenerateAsync_ProducesValidHtml()
    {
        await WriteTestGraph();

        var generator = new HtmlGraphGenerator(_testDir);
        var html = await generator.GenerateAsync();

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("CodeGraph", html);
        Assert.Contains("3d-force-graph", html);
        Assert.Contains("const graphData =", html);
        Assert.Contains("Class0", html);
    }

    [Fact]
    public async Task GenerateAsync_ContainsSolutionName()
    {
        await WriteTestGraph();

        var generator = new HtmlGraphGenerator(_testDir);
        var html = await generator.GenerateAsync();

        Assert.Contains("Test", html);
    }

    [Fact]
    public async Task GenerateAsync_ContainsNodeAndEdgeData()
    {
        await WriteTestGraph(nodeCount: 3, edgeCount: 2);

        var generator = new HtmlGraphGenerator(_testDir);
        var html = await generator.GenerateAsync();

        Assert.Contains("\"source\":", html);
        Assert.Contains("\"target\":", html);
        Assert.Contains("\"kind\":", html);
    }

    [Fact]
    public void Sample_RetainsAllNodesWhenUnderCap()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method },
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var generator = new HtmlGraphGenerator(".", maxNodes: 100);
        var (sampledNodes, sampledEdges) = generator.Sample(nodes, edges);

        Assert.Equal(2, sampledNodes.Count);
        Assert.Single(sampledEdges);
    }

    [Fact]
    public void Sample_CapsNodesWhenOverLimit()
    {
        var nodes = new Dictionary<string, GraphNode>();
        for (int i = 0; i < 100; i++)
        {
            nodes[$"M{i}"] = new() { Id = $"M{i}", Name = $"Method{i}", Kind = NodeKind.Method };
        }
        // Add a few Type nodes
        nodes["T1"] = new() { Id = "T1", Name = "Type1", Kind = NodeKind.Type };
        nodes["T2"] = new() { Id = "T2", Name = "Type2", Kind = NodeKind.Type };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "T1", ToId = "M0", Type = EdgeType.Contains },
            new() { FromId = "T2", ToId = "M1", Type = EdgeType.Contains },
        };

        var generator = new HtmlGraphGenerator(".", maxNodes: 10);
        var (sampledNodes, sampledEdges) = generator.Sample(nodes, edges);

        Assert.Equal(10, sampledNodes.Count);
        // Type nodes should be prioritized
        Assert.Contains("T1", sampledNodes.Keys);
        Assert.Contains("T2", sampledNodes.Keys);
    }

    [Fact]
    public void Sample_RemovesEdgesForExcludedNodes()
    {
        var nodes = new Dictionary<string, GraphNode>();
        for (int i = 0; i < 20; i++)
        {
            nodes[$"N{i}"] = new() { Id = $"N{i}", Name = $"N{i}", Kind = NodeKind.Method };
        }

        var edges = new List<GraphEdge>
        {
            new() { FromId = "N0", ToId = "N1", Type = EdgeType.Calls },
            new() { FromId = "N18", ToId = "N19", Type = EdgeType.Calls },
        };

        var generator = new HtmlGraphGenerator(".", maxNodes: 5);
        var (sampledNodes, sampledEdges) = generator.Sample(nodes, edges);

        // All remaining edges should reference only retained nodes
        foreach (var edge in sampledEdges)
        {
            Assert.Contains(edge.FromId, sampledNodes.Keys);
            Assert.Contains(edge.ToId, sampledNodes.Keys);
        }
    }
}
