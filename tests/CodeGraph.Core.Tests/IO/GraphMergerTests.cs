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

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, partialGraphs);

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

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, Array.Empty<ProjectGraph>());

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

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, partialGraphs);

        Assert.Equal(2, mergedNodes.Count);
        Assert.Contains("ProjA.New", mergedNodes.Keys);
        Assert.Contains("ProjB.Keep", mergedNodes.Keys);
        Assert.Single(mergedEdges);
        Assert.Equal("ProjB.Keep", mergedEdges[0].FromId);
    }

    [Fact]
    public void Merge_UpdatedProject_ReplacesOutgoingEdgesButKeepsIncomingEdges()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            ["ProjA.Service"] = new() { Id = "ProjA.Service", Name = "Service", Kind = NodeKind.Type },
            ["ProjB.Controller"] = new() { Id = "ProjB.Controller", Name = "Controller", Kind = NodeKind.Type },
            ["ProjB.Dto"] = new() { Id = "ProjB.Dto", Name = "Dto", Kind = NodeKind.Type }
        };
        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = "ProjA.Service", ToId = "ProjB.Dto", Type = EdgeType.Calls },
            new() { FromId = "ProjB.Controller", ToId = "ProjA.Service", Type = EdgeType.DependsOn }
        };
        var partialGraphs = new List<ProjectGraph>
        {
            new()
            {
                ProjectOrNamespace = "ProjA",
                Nodes = new Dictionary<string, GraphNode>
                {
                    ["ProjA.Service"] = new() { Id = "ProjA.Service", Name = "ServiceV2", Kind = NodeKind.Type }
                },
                Edges = new List<GraphEdge>
                {
                    new() { FromId = "ProjA.Service", ToId = "ProjB.Controller", Type = EdgeType.Calls }
                }
            }
        };

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, partialGraphs);

        Assert.Equal("ServiceV2", mergedNodes["ProjA.Service"].Name);
        Assert.DoesNotContain(mergedEdges, edge => edge.FromId == "ProjA.Service" && edge.ToId == "ProjB.Dto");
        Assert.Contains(mergedEdges, edge => edge.FromId == "ProjA.Service" && edge.ToId == "ProjB.Controller");
        Assert.Contains(mergedEdges, edge => edge.FromId == "ProjB.Controller" && edge.ToId == "ProjA.Service");
    }

    [Fact]
    public void Merge_IdStartingWithDot_TreatsWholeIdAsProject()
    {
        var existingNodes = new Dictionary<string, GraphNode>
        {
            [".Hidden"] = new() { Id = ".Hidden", Name = "Hidden", Kind = NodeKind.Type },
            ["Other.Node"] = new() { Id = "Other.Node", Name = "Node", Kind = NodeKind.Type }
        };
        var existingEdges = new List<GraphEdge>
        {
            new() { FromId = ".Hidden", ToId = "Other.Node", Type = EdgeType.Calls },
            new() { FromId = "Other.Node", ToId = ".Hidden", Type = EdgeType.DependsOn }
        };
        var partialGraphs = new List<ProjectGraph>
        {
            new()
            {
                ProjectOrNamespace = ".Hidden",
                Nodes = new Dictionary<string, GraphNode>
                {
                    [".Hidden"] = new() { Id = ".Hidden", Name = "HiddenUpdated", Kind = NodeKind.Type }
                },
                Edges = new List<GraphEdge>
                {
                    new() { FromId = ".Hidden", ToId = "Other.Node", Type = EdgeType.Inherits }
                }
            }
        };

        var (mergedNodes, mergedEdges) = GraphMerger.Merge(existingNodes, existingEdges, partialGraphs);

        Assert.Equal("HiddenUpdated", mergedNodes[".Hidden"].Name);
        Assert.DoesNotContain(mergedEdges, edge => edge.FromId == ".Hidden" && edge.Type == EdgeType.Calls);
        Assert.Contains(mergedEdges, edge => edge.FromId == ".Hidden" && edge.Type == EdgeType.Inherits);
        Assert.Contains(mergedEdges, edge => edge.FromId == "Other.Node" && edge.ToId == ".Hidden");
        Assert.Equal(2, mergedEdges.Count);
    }
}
