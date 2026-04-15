using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class GraphMergerExtendedTests
{
    [Fact]
    public void Merge_NoDotsInId_ExtractsWholeIdAsProject()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            ["NoDotId"] = new() { Id = "NoDotId", Name = "NoDotId", Kind = NodeKind.Type },
        };
        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = "NoDotId", ToId = "Other", Type = EdgeType.Calls },
        };

        var partialGraphs = new List<ProjectGraph>
        {
            new()
            {
                ProjectOrNamespace = "NoDotId",
                Nodes = new Dictionary<string, GraphNode>
                {
                    ["NoDotId"] = new() { Id = "NoDotId", Name = "NoDotIdUpdated", Kind = NodeKind.Type },
                },
                Edges = new List<GraphEdge>
                {
                    new() { FromId = "NoDotId", ToId = "New", Type = EdgeType.Calls },
                }
            }
        };

        var merger = new GraphMerger();
        var (mergedNodes, mergedEdges) = merger.Merge(existingNodes, existingEdges, partialGraphs);

        Assert.Single(mergedNodes);
        Assert.Equal("NoDotIdUpdated", mergedNodes["NoDotId"].Name);
        Assert.Single(mergedEdges);
        Assert.Equal("New", mergedEdges[0].ToId);
    }

    [Fact]
    public void Merge_EmptyPartialGraphs_ReturnsExisting()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            ["A.B"] = new() { Id = "A.B", Name = "B", Kind = NodeKind.Type },
        };
        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = "A.B", ToId = "C.D", Type = EdgeType.Calls },
        };

        var merger = new GraphMerger();
        var (mergedNodes, mergedEdges) = merger.Merge(existingNodes, existingEdges, Array.Empty<ProjectGraph>());

        Assert.Single(mergedNodes);
        Assert.Single(mergedEdges);
    }

    [Fact]
    public void Merge_EdgesFromRemovedNodes_AreRemoved()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            ["ProjA.Old"] = new() { Id = "ProjA.Old", Name = "Old", Kind = NodeKind.Type },
            ["ProjB.Keep"] = new() { Id = "ProjB.Keep", Name = "Keep", Kind = NodeKind.Type },
        };
        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = "ProjA.Old", ToId = "ProjB.Keep", Type = EdgeType.Calls },
            new() { FromId = "ProjB.Keep", ToId = "ProjA.Old", Type = EdgeType.Inherits },
        };

        var partialGraphs = new List<ProjectGraph>
        {
            new()
            {
                ProjectOrNamespace = "ProjA",
                Nodes = new Dictionary<string, GraphNode>
                {
                    ["ProjA.New"] = new() { Id = "ProjA.New", Name = "New", Kind = NodeKind.Type },
                },
                Edges = new List<GraphEdge>()
            }
        };

        var merger = new GraphMerger();
        var (mergedNodes, mergedEdges) = merger.Merge(existingNodes, existingEdges, partialGraphs);

        Assert.Equal(2, mergedNodes.Count);
        Assert.Contains("ProjA.New", mergedNodes.Keys);
        Assert.Contains("ProjB.Keep", mergedNodes.Keys);
        Assert.Single(mergedEdges);
        Assert.Equal("ProjB.Keep", mergedEdges[0].FromId);
    }
}
