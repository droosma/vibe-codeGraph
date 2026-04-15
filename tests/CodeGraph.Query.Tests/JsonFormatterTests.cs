using System.Text.Json;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class JsonFormatterTests
{
    [Fact]
    public void Format_ReturnsValidJson()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var json = JsonFormatter.Format(result);

        // Should be valid JSON
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void Format_IsIndented()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var json = JsonFormatter.Format(result);

        // Indented JSON contains newlines
        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    [Fact]
    public void Format_UsesCamelCase()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var json = JsonFormatter.Format(result);

        Assert.Contains("\"matchedNodes\"", json);
        Assert.Contains("\"totalMatchCount\"", json);
    }

    [Fact]
    public void Format_EnumsAsStrings()
    {
        var result = new QueryResult
        {
            MatchedNodes = new List<GraphNode>
            {
                new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method }
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var json = JsonFormatter.Format(result);

        Assert.Contains("\"method\"", json);
        Assert.Contains("\"calls\"", json);
    }

    [Fact]
    public void Format_NullsOmitted()
    {
        var result = new QueryResult
        {
            TargetNode = null,
            MatchedNodes = new List<GraphNode>(),
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var json = JsonFormatter.Format(result);

        Assert.DoesNotContain("\"targetNode\"", json);
    }
}
