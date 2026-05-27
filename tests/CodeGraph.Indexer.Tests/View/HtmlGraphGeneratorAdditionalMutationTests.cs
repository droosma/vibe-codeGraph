using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.View;

namespace CodeGraph.Indexer.Tests.View;

public sealed class HtmlGraphGeneratorAdditionalMutationTests : IDisposable
{
    private readonly string _graphDir;

    public HtmlGraphGeneratorAdditionalMutationTests()
    {
        _graphDir = Path.Combine(Path.GetTempPath(), "html-graph-more-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    [Fact]
    public async Task GenerateAsync_NodeWithoutOptionalValues_SerializesEmptyNamespaceAndNullSignature()
    {
        await WriteGraphAsync(
            new GraphMetadata
            {
                SchemaVersion = GraphSchema.CurrentVersion,
                Solution = "Graph.sln",
                SolutionName = "Graph"
            },
            new Dictionary<string, GraphNode>
            {
                ["Graph.Type"] = new GraphNode
                {
                    Id = "Graph.Type",
                    Name = "Type",
                    Kind = NodeKind.Type,
                    AssemblyName = "Graph.Assembly",
                    Accessibility = Accessibility.Public
                }
            },
            new List<GraphEdge>());

        var html = await new HtmlGraphGenerator(_graphDir).GenerateAsync();

        Assert.Contains("\"ns\":\"\"", html, StringComparison.Ordinal);
        Assert.Contains("\"filePath\":\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_HighestDegreeIncomingNode_IsRetainedWhenCapacityRemains()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Demo"] = new GraphNode { Id = "Demo", Name = "Demo", Kind = NodeKind.Namespace },
            ["Demo.Type"] = new GraphNode { Id = "Demo.Type", Name = "Type", Kind = NodeKind.Type },
            ["Demo.Hub"] = new GraphNode { Id = "Demo.Hub", Name = "Hub", Kind = NodeKind.Method },
            ["Demo.A"] = new GraphNode { Id = "Demo.A", Name = "A", Kind = NodeKind.Method },
            ["Demo.B"] = new GraphNode { Id = "Demo.B", Name = "B", Kind = NodeKind.Method }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Demo.Type", ToId = "Demo.Hub", Type = EdgeType.Contains },
            new() { FromId = "Demo.A", ToId = "Demo.Hub", Type = EdgeType.Calls },
            new() { FromId = "Demo.B", ToId = "Demo.Hub", Type = EdgeType.Calls }
        };

        var (sampledNodes, sampledEdges) = new HtmlGraphGenerator(".", maxNodes: 3).Sample(nodes, edges);

        Assert.Equal(new[] { "Demo", "Demo.Type", "Demo.Hub" }, sampledNodes.Keys.ToArray());
        var edge = Assert.Single(sampledEdges);
        Assert.Equal("Demo.Type", edge.FromId);
        Assert.Equal("Demo.Hub", edge.ToId);
    }

    private async Task WriteGraphAsync(GraphMetadata metadata, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        await new GraphWriter().WriteAsync(_graphDir, nodes.Values, edges, metadata);
    }
}
