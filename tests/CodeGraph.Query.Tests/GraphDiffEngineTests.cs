using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class GraphDiffEngineTests
{
    [Fact]
    public void Compare_DetectsNodeAndEdgeChanges()
    {
        var baseMetadata = new GraphMetadata { CommitHash = "abc1234", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
        var headMetadata = new GraphMetadata { CommitHash = "def5678", Branch = "feature", GeneratedAt = DateTimeOffset.UtcNow };

        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "A()" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method, Signature = "B()" }
        };
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "A(int x)" },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Type, Signature = "class C" }
        };

        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "A", Type = EdgeType.References }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "A", Type = EdgeType.References }
        };

        var diff = GraphDiffEngine.Compare(baseMetadata, baseNodes, baseEdges, headMetadata, headNodes, headEdges);

        Assert.Single(diff.AddedNodes);
        Assert.Equal("C", diff.AddedNodes[0].Id);

        Assert.Single(diff.RemovedNodes);
        Assert.Equal("B", diff.RemovedNodes[0].Id);

        Assert.Single(diff.SignatureChangedNodes);
        Assert.Equal("A()", diff.SignatureChangedNodes[0].Previous.Signature);
        Assert.Equal("A(int x)", diff.SignatureChangedNodes[0].Current.Signature);

        Assert.Single(diff.AddedEdges);
        Assert.Equal("C", diff.AddedEdges[0].ToId);

        Assert.Single(diff.RemovedEdges);
        Assert.Equal("B", diff.RemovedEdges[0].ToId);
    }
}
