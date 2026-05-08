using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class SessionAwareSuggestionTests
{
    private static QueryResult BuildResult(int nodeCount, int edgeCount, string pattern, int depth)
    {
        var nodes = new Dictionary<string, GraphNode>();
        for (int i = 0; i < nodeCount; i++)
        {
            nodes[$"Node{i}"] = new GraphNode
            {
                Id = $"Node{i}",
                Name = $"Node{i}",
                Kind = NodeKind.Type
            };
        }

        var edges = new List<GraphEdge>();
        for (int i = 0; i < edgeCount; i++)
        {
            edges.Add(new GraphEdge
            {
                FromId = $"Node{i % nodeCount}",
                ToId = $"Node{(i + 1) % nodeCount}",
                Type = EdgeType.Calls
            });
        }

        var matched = nodes.Values.Take(1).ToList();

        return new QueryResult
        {
            TargetNode = matched.FirstOrDefault(),
            MatchedNodes = matched,
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                CommitHash = "abc", Branch = "main",
                GeneratedAt = DateTimeOffset.UtcNow,
                IndexerVersion = "1.0", Solution = "Test.sln",
                ProjectsIndexed = new[] { "Test" }
            }
        };
    }

    [Fact]
    public void Generate_WithoutSession_ReturnsBasicSuggestions()
    {
        var result = BuildResult(5, 3, "OrderService", 1);
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.NotEmpty(suggestions);
    }

    [Fact]
    public void Generate_WithSession_SkipsAlreadyQueried()
    {
        var result = BuildResult(5, 3, "OrderService", 1);
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };
        var session = new QuerySessionTracker();
        session.Record("OrderService", 1, 5);

        var suggestions = QuerySuggestionGenerator.Generate(result, options, session);

        // Should not suggest re-querying the same pattern at same or lower depth
        Assert.DoesNotContain(suggestions, s => s.Contains("OrderService") && s.Contains("--depth 1"));
    }

    [Fact]
    public void Generate_AfterThreeQueries_SuggestsReport()
    {
        var result = BuildResult(5, 3, "Z", 1);
        var options = new QueryOptions { Pattern = "Z", Depth = 1 };
        var session = new QuerySessionTracker();
        session.Record("A", 1, 3);
        session.Record("B", 1, 4);
        session.Record("C", 1, 2);

        var suggestions = QuerySuggestionGenerator.Generate(result, options, session);

        Assert.Contains(suggestions, s => s.Contains("report"));
    }

    [Fact]
    public void Generate_WithNullSession_DoesNotThrow()
    {
        var result = BuildResult(5, 3, "OrderService", 1);
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options, null);

        Assert.NotNull(suggestions);
    }
}
