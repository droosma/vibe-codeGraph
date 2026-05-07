using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class GraphDiffEngineTests
{
    private static GraphMetadata BaseMeta => new() { CommitHash = "abc1234", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow };
    private static GraphMetadata HeadMeta => new() { CommitHash = "def5678", Branch = "feature", GeneratedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void Compare_DetectsNodeAndEdgeChanges()
    {
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

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, baseEdges, HeadMeta, headNodes, headEdges);

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

    [Fact]
    public void Compare_IdenticalGraphs_NoDiff()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "void A()" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type, Signature = "class B" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, edges, HeadMeta, nodes, edges);

        Assert.Empty(diff.AddedNodes);
        Assert.Empty(diff.RemovedNodes);
        Assert.Empty(diff.SignatureChangedNodes);
        Assert.Empty(diff.AddedEdges);
        Assert.Empty(diff.RemovedEdges);
    }

    [Fact]
    public void Compare_AddedNodes_SortedById()
    {
        // Targets L41: OrderBy to OrderByDescending mutation
        var baseNodes = new Dictionary<string, GraphNode>();
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Method },
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Equal(3, diff.AddedNodes.Count);
        Assert.Equal("A", diff.AddedNodes[0].Id);
        Assert.Equal("B", diff.AddedNodes[1].Id);
        Assert.Equal("C", diff.AddedNodes[2].Id);
    }

    [Fact]
    public void Compare_RemovedNodes_SortedById()
    {
        // Targets L46: OrderBy to OrderByDescending mutation
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["Z"] = new() { Id = "Z", Name = "Z", Kind = NodeKind.Method },
            ["M"] = new() { Id = "M", Name = "M", Kind = NodeKind.Method },
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
        };
        var headNodes = new Dictionary<string, GraphNode>();

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Equal(3, diff.RemovedNodes.Count);
        Assert.Equal("A", diff.RemovedNodes[0].Id);
        Assert.Equal("M", diff.RemovedNodes[1].Id);
        Assert.Equal("Z", diff.RemovedNodes[2].Id);
    }

    [Fact]
    public void Compare_SignatureChangedNodes_SortedById()
    {
        // Targets L51: OrderBy to OrderByDescending mutation on signatureChangedNodes
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["Z"] = new() { Id = "Z", Name = "Z", Kind = NodeKind.Method, Signature = "old_z" },
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "old_a" }
        };
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["Z"] = new() { Id = "Z", Name = "Z", Kind = NodeKind.Method, Signature = "new_z" },
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "new_a" }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Equal(2, diff.SignatureChangedNodes.Count);
        Assert.Equal("A", diff.SignatureChangedNodes[0].Current.Id);
        Assert.Equal("Z", diff.SignatureChangedNodes[1].Current.Id);
    }

    [Fact]
    public void Compare_SignatureChange_NullToValue()
    {
        // Targets L55-56: Null coalescing mutations on Signature ?? string.Empty
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = null! }
        };
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "new()" }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Single(diff.SignatureChangedNodes);
    }

    [Fact]
    public void Compare_SignatureChange_ValueToNull()
    {
        // Targets L55-56: null coalescing, ensuring both sides are compared
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = "old()" }
        };
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = null! }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Single(diff.SignatureChangedNodes);
    }

    [Fact]
    public void Compare_SignatureChange_BothNull_NoChange()
    {
        // When both signatures are null, they should be treated as equal (both coalesce to "")
        var baseNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = null! }
        };
        var headNodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, Signature = null! }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, baseNodes, new List<GraphEdge>(), HeadMeta, headNodes, new List<GraphEdge>());

        Assert.Empty(diff.SignatureChangedNodes);
    }

    [Fact]
    public void Compare_AddedEdges_SortedByFromThenToThenType()
    {
        // Targets L69: OrderBy/ThenBy to OrderByDescending/ThenByDescending mutations
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Method }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "C", ToId = "A", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.References },
            new() { FromId = "A", ToId = "A", Type = EdgeType.Calls }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, new List<GraphEdge>(), HeadMeta, nodes, headEdges);

        Assert.Equal(4, diff.AddedEdges.Count);
        // Should be sorted by FromId ASC, then ToId ASC, then Type ASC
        Assert.Equal("A", diff.AddedEdges[0].FromId);
        Assert.Equal("A", diff.AddedEdges[0].ToId);
        Assert.Equal("A", diff.AddedEdges[1].FromId);
        Assert.Equal("B", diff.AddedEdges[1].ToId);
        Assert.Equal(EdgeType.Calls, diff.AddedEdges[1].Type);
        Assert.Equal("A", diff.AddedEdges[2].FromId);
        Assert.Equal("B", diff.AddedEdges[2].ToId);
        Assert.Equal(EdgeType.References, diff.AddedEdges[2].Type);
        Assert.Equal("C", diff.AddedEdges[3].FromId);
    }

    [Fact]
    public void Compare_RemovedEdges_SortedByFromThenToThenType()
    {
        // Targets L77: OrderBy/ThenBy to OrderByDescending/ThenByDescending mutations
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Method }
        };
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "C", ToId = "A", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.References },
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "A", Type = EdgeType.Contains }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, baseEdges, HeadMeta, nodes, new List<GraphEdge>());

        Assert.Equal(4, diff.RemovedEdges.Count);
        Assert.Equal("A", diff.RemovedEdges[0].FromId);
        Assert.Equal("A", diff.RemovedEdges[0].ToId);
        Assert.Equal("A", diff.RemovedEdges[1].FromId);
        Assert.Equal("B", diff.RemovedEdges[1].ToId);
        Assert.True(diff.RemovedEdges[1].Type < diff.RemovedEdges[2].Type);
        Assert.Equal("C", diff.RemovedEdges[3].FromId);
    }

    [Fact]
    public void Compare_DuplicateEdges_DeduplicatedByKey()
    {
        // Targets L64/L67: First() to FirstOrDefault() mutations
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        // Duplicate edges in base — GroupBy + First should handle
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, baseEdges, HeadMeta, nodes, headEdges);

        // Same edges on both sides after dedup — no added/removed
        Assert.Empty(diff.AddedEdges);
        Assert.Empty(diff.RemovedEdges);
    }

    [Fact]
    public void Compare_EdgeKey_IncludesResolution()
    {
        // Targets L104: Null coalescing mutation and string mutation on Resolution ?? string.Empty
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo, Resolution = "IFoo → Foo" }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.ResolvesTo, Resolution = "IFoo → Bar" }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, baseEdges, HeadMeta, nodes, headEdges);

        // Different resolution makes them different edge keys
        Assert.Single(diff.AddedEdges);
        Assert.Single(diff.RemovedEdges);
    }

    [Fact]
    public void Compare_EdgeKey_NullResolution_TreatedAsEmpty()
    {
        // Targets L104: null coalescing — null Resolution should match empty string
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Resolution = null }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, Resolution = null }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, baseEdges, HeadMeta, nodes, headEdges);

        Assert.Empty(diff.AddedEdges);
        Assert.Empty(diff.RemovedEdges);
    }

    [Fact]
    public void Compare_EdgeKey_IncludesIsExternal()
    {
        // Edge key includes IsExternal — same from/to/type but different IsExternal = different
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method }
        };
        var baseEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, IsExternal = false }
        };
        var headEdges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, IsExternal = true }
        };

        var diff = GraphDiffEngine.Compare(BaseMeta, nodes, baseEdges, HeadMeta, nodes, headEdges);

        Assert.Single(diff.AddedEdges);
        Assert.True(diff.AddedEdges[0].IsExternal);
        Assert.Single(diff.RemovedEdges);
        Assert.False(diff.RemovedEdges[0].IsExternal);
    }

    [Fact]
    public void Compare_MetadataPassedThrough()
    {
        var diff = GraphDiffEngine.Compare(
            BaseMeta, new Dictionary<string, GraphNode>(), new List<GraphEdge>(),
            HeadMeta, new Dictionary<string, GraphNode>(), new List<GraphEdge>());

        Assert.Equal("abc1234", diff.BaseMetadata.CommitHash);
        Assert.Equal("def5678", diff.HeadMetadata.CommitHash);
    }
}
