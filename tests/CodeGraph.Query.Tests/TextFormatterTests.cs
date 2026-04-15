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
}
