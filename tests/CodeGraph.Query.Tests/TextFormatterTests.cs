using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TextFormatterTests
{
    [Fact]
    public void Format_WithTargetNode_ContainsTargetLine()
    {
        var result = new QueryResult
        {
            TargetNode = new GraphNode
            {
                Id = "MyClass.MyMethod",
                Kind = NodeKind.Method,
                FilePath = "src/MyClass.cs",
                StartLine = 10,
                EndLine = 20,
                Signature = "void MyMethod()"
            },
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Target: MyClass.MyMethod (Method)", text);
        Assert.Contains("File: src/MyClass.cs:10-20", text);
        Assert.Contains("Sig:  void MyMethod()", text);
    }

    [Fact]
    public void Format_TargetNodeWithNoSignature_SkipsSigLine()
    {
        var result = new QueryResult
        {
            TargetNode = new GraphNode
            {
                Id = "MyClass",
                Kind = NodeKind.Type,
                FilePath = "src/MyClass.cs",
                StartLine = 1,
                EndLine = 50,
                Signature = ""
            },
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Target: MyClass (Type)", text);
        Assert.DoesNotContain("Sig:", text);
    }

    [Fact]
    public void Format_NoTargetWithMatchedNodes_ShowsMatchedNodesList()
    {
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method },
                new() { Id = "B", Name = "B", Kind = NodeKind.Type }
            },
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Matched Nodes (2):", text);
        Assert.Contains("- A (Method)", text);
        Assert.Contains("- B (Type)", text);
    }

    [Fact]
    public void Format_EdgesGroupedByType()
    {
        var nodeA = new GraphNode { Id = "A", Name = "ClassA", Kind = NodeKind.Type };
        var nodeB = new GraphNode { Id = "B", Name = "ClassB", Kind = NodeKind.Type };
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = nodeA,
                ["B"] = nodeB
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
                new() { FromId = "B", ToId = "A", Type = EdgeType.Calls },
                new() { FromId = "A", ToId = "B", Type = EdgeType.Inherits }
            }
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Calls (2):", text);
        Assert.Contains("Inherits (1):", text);
    }

    [Fact]
    public void Format_EdgeWithKnownNodes_UsesName()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "ClassA", Kind = NodeKind.Type },
                ["B"] = new() { Id = "B", Name = "ClassB", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            }
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("ClassA -> ClassB", text);
    }

    [Fact]
    public void Format_EdgeWithUnknownNodes_UsesRawId()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>
            {
                new() { FromId = "Unknown.From", ToId = "Unknown.To", Type = EdgeType.Calls }
            }
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Unknown.From -> Unknown.To", text);
    }

    [Fact]
    public void Format_WasTruncatedTrue_ShowsTruncationWarning()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            WasTruncated = true,
            TotalMatchCount = 500
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("⚠ Results truncated", text);
        Assert.Contains("500", text);
    }

    [Fact]
    public void Format_WasTruncatedFalse_NoTruncationMessage()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            WasTruncated = false
        };

        var text = TextFormatter.Format(result);

        Assert.DoesNotContain("truncated", text);
    }

    [Fact]
    public void Format_EmptyResult_MinimalOutput()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Subgraph: 0 nodes, 0 edges", text);
        Assert.DoesNotContain("Target:", text);
        Assert.DoesNotContain("Matched Nodes", text);
        Assert.DoesNotContain("truncated", text);
    }

    [Fact]
    public void Format_TargetNodeWithNullSignature_SkipsSigLine()
    {
        // Targets L18: Statement mutation on Signature line
        var result = new QueryResult
        {
            TargetNode = new GraphNode
            {
                Id = "X.Y",
                Kind = NodeKind.Type,
                FilePath = "src/X.cs",
                StartLine = 1,
                EndLine = 10,
                Signature = null!
            },
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Target: X.Y (Type)", text);
        Assert.DoesNotContain("Sig:", text);
    }

    [Fact]
    public void Format_MatchedNodesWithTarget_DoesNotShowMatchedList()
    {
        // Targets L28: Statement mutation — when TargetNode is not null, matched node list is skipped
        var target = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method,
            FilePath = "a.cs", StartLine = 1, EndLine = 5 };
        var result = new QueryResult
        {
            TargetNode = target,
            MatchedNodes = new List<GraphNode> { target },
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>()
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Target:", text);
        Assert.DoesNotContain("Matched Nodes", text);
    }

    [Fact]
    public void Format_SubgraphLine_ShowsCorrectCounts()
    {
        // Targets L32: Statement mutation on subgraph line
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type },
                ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type }
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
                new() { FromId = "B", ToId = "A", Type = EdgeType.References },
                new() { FromId = "A", ToId = "B", Type = EdgeType.Inherits }
            }
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("Subgraph: 2 nodes, 3 edges", text);
    }

    [Fact]
    public void Format_EdgesGroupedByType_OrderedByEnumValue()
    {
        // Targets L35: OrderBy to OrderByDescending mutation
        var nodeA = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Type };
        var nodeB = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Type };
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode> { ["A"] = nodeA, ["B"] = nodeB },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.References },
                new() { FromId = "A", ToId = "B", Type = EdgeType.Contains },
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            }
        };

        var text = TextFormatter.Format(result);

        // Contains (0) < Calls (1) < References (8)
        var containsIdx = text.IndexOf("Contains (1):");
        var callsIdx = text.IndexOf("Calls (1):");
        var refsIdx = text.IndexOf("References (1):");

        Assert.True(containsIdx >= 0);
        Assert.True(callsIdx >= 0);
        Assert.True(refsIdx >= 0);
        Assert.True(containsIdx < callsIdx, "Contains should appear before Calls");
        Assert.True(callsIdx < refsIdx, "Calls should appear before References");
    }

    [Fact]
    public void Format_TruncationMessage_ContainsExactCount()
    {
        // Targets L45: Statement mutation on truncation message
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            WasTruncated = true,
            TotalMatchCount = 42
        };

        var text = TextFormatter.Format(result);

        Assert.Contains("⚠ Results truncated. Showing subset of 42 total matches.", text);
    }
}
