using System.Text.Json;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.View;

namespace CodeGraph.Indexer.Tests.View;

public sealed class HtmlGraphGeneratorMutationTests : IDisposable
{
    private readonly string _testDir;

    public HtmlGraphGeneratorMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "html-graph-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task GenerateAsync_EscapesSolutionName_AndIncludesExactJsonFields()
    {
        await WriteGraphAsync(
            new GraphMetadata
            {
                SchemaVersion = GraphSchema.CurrentVersion,
                Solution = "Graph.sln",
                SolutionName = "Name <tag> & \"quoted\""
            },
            new Dictionary<string, GraphNode>
            {
                ["Demo.Service"] = new GraphNode
                {
                    Id = "Demo.Service",
                    Name = "Service",
                    Kind = NodeKind.Type,
                    AssemblyName = "Demo.Assembly",
                    Accessibility = Accessibility.Public,
                    FilePath = @"D:\repo\Service.cs",
                    StartLine = 12,
                    EndLine = 20,
                    ContainingNamespaceId = "Demo"
                },
                ["Demo.Service.Run"] = new GraphNode
                {
                    Id = "Demo.Service.Run",
                    Name = "Run",
                    Kind = NodeKind.Method,
                    AssemblyName = "Demo.Assembly",
                    Accessibility = Accessibility.Internal,
                    Signature = "void Run()",
                    FilePath = @"D:\repo\Service.cs",
                    StartLine = 14,
                    EndLine = 16,
                    ContainingNamespaceId = "Demo"
                }
            },
            new List<GraphEdge>
            {
                new GraphEdge { FromId = "Demo.Service", ToId = "Demo.Service.Run", Type = EdgeType.Contains, IsExternal = true }
            });

        var html = await new HtmlGraphGenerator(_testDir).GenerateAsync();

        Assert.Contains("<title>CodeGraph — Name &lt;tag&gt; &amp; &quot;quoted&quot;</title>", html);
        Assert.Contains("2 nodes / 1 edges — Name &lt;tag&gt; &amp; &quot;quoted&quot;", html);
        Assert.Contains("\"assembly\":\"Demo.Assembly\"", html);
        Assert.Contains("\"accessibility\":\"Public\"", html);
        Assert.Contains("\"startLine\":12", html);
        Assert.Contains("\"ns\":\"Demo\"", html);
        Assert.Contains("\"isExternal\":true", html);
        Assert.Contains("\"signature\":\"void Run()\"", html);
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToSolutionFileName_WhenSolutionNameMissing()
    {
        await WriteGraphAsync(
            new GraphMetadata
            {
                SchemaVersion = GraphSchema.CurrentVersion,
                Solution = "Fallback.sln",
                SolutionName = string.Empty
            },
            new Dictionary<string, GraphNode>
            {
                ["Demo.Node"] = new GraphNode { Id = "Demo.Node", Name = "Node", Kind = NodeKind.Type, AssemblyName = "Demo", Accessibility = Accessibility.Public }
            },
            new List<GraphEdge>());

        var html = await new HtmlGraphGenerator(_testDir).GenerateAsync();

        Assert.Contains("<title>CodeGraph — Fallback.sln</title>", html);
        Assert.Contains("1 nodes / 0 edges — Fallback.sln", html);
    }

    [Fact]
    public void Sample_WhenTypeAndNamespaceNodesAlreadyMeetCap_TakesFirstInsertedNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Ns"] = new GraphNode { Id = "Ns", Name = "Ns", Kind = NodeKind.Namespace },
            ["TypeA"] = new GraphNode { Id = "TypeA", Name = "TypeA", Kind = NodeKind.Type },
            ["TypeB"] = new GraphNode { Id = "TypeB", Name = "TypeB", Kind = NodeKind.Type },
            ["Method"] = new GraphNode { Id = "Method", Name = "Method", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "TypeA", ToId = "Method", Type = EdgeType.Contains },
            new GraphEdge { FromId = "TypeB", ToId = "Method", Type = EdgeType.Contains }
        };

        var (sampledNodes, sampledEdges) = new HtmlGraphGenerator(".", maxNodes: 2).Sample(nodes, edges);

        Assert.Equal(new[] { "Ns", "TypeA" }, sampledNodes.Keys.ToArray());
        Assert.Empty(sampledEdges);
    }

    [Fact]
    public void Sample_PrefersHighestDegreeMethod_WhenSpaceRemainsAfterRetainingTypes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Demo"] = new GraphNode { Id = "Demo", Name = "Demo", Kind = NodeKind.Namespace },
            ["Demo.Type"] = new GraphNode { Id = "Demo.Type", Name = "Type", Kind = NodeKind.Type },
            ["Demo.Type.A"] = new GraphNode { Id = "Demo.Type.A", Name = "A", Kind = NodeKind.Method },
            ["Demo.Type.B"] = new GraphNode { Id = "Demo.Type.B", Name = "B", Kind = NodeKind.Method },
            ["Demo.Type.C"] = new GraphNode { Id = "Demo.Type.C", Name = "C", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "Demo.Type", ToId = "Demo.Type.B", Type = EdgeType.Contains },
            new GraphEdge { FromId = "Demo.Type.A", ToId = "Demo.Type.B", Type = EdgeType.Calls },
            new GraphEdge { FromId = "Demo.Type.B", ToId = "Demo.Type.C", Type = EdgeType.Calls }
        };

        var (sampledNodes, sampledEdges) = new HtmlGraphGenerator(".", maxNodes: 3).Sample(nodes, edges);

        Assert.Equal(new[] { "Demo", "Demo.Type", "Demo.Type.B" }, sampledNodes.Keys.ToArray());
        var edge = Assert.Single(sampledEdges);
        Assert.Equal("Demo.Type", edge.FromId);
        Assert.Equal("Demo.Type.B", edge.ToId);
    }

    private async Task WriteGraphAsync(GraphMetadata metadata, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var writer = new GraphWriter();
        await writer.WriteAsync(_testDir, nodes.Values, edges, metadata);
    }
}
