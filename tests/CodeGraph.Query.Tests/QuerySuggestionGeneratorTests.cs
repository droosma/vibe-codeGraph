using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class QuerySuggestionGeneratorTests
{
    private static GraphNode MakeNode(string id, NodeKind kind = NodeKind.Type, string assembly = "TestAssembly")
        => new()
        {
            Id = id,
            Name = id,
            Kind = kind,
            FilePath = $"src/{id}.cs",
            Signature = $"public class {id}",
            AssemblyName = assembly
        };

    [Fact]
    public void Generate_EmptyResult_ReturnsNoSuggestions()
    {
        var result = new QueryResult { Metadata = new GraphMetadata() };
        var options = new QueryOptions { Pattern = "Foo", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_DepthOne_SuggestsDeeperTraversal()
    {
        var node = MakeNode("OrderService");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("--depth 2"));
    }

    [Fact]
    public void Generate_HasCalls_SuggestsCallChainExploration()
    {
        var node = MakeNode("OrderService");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("--kind calls"));
    }

    [Fact]
    public void Generate_HasInterfaces_SuggestsDiWiring()
    {
        var iface = MakeNode("IOrderRepository");
        var impl = MakeNode("OrderService");
        var result = new QueryResult
        {
            TargetNode = impl,
            MatchedNodes = new List<GraphNode> { impl },
            Nodes = new Dictionary<string, GraphNode>
            {
                [impl.Id] = impl,
                [iface.Id] = iface
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = impl.Id, ToId = iface.Id, Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("--kind resolves-to"));
    }

    [Fact]
    public void Generate_HasInheritance_SuggestsInheritanceExploration()
    {
        var node = MakeNode("BaseController");
        var child = MakeNode("OrderController");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode>
            {
                [node.Id] = node,
                [child.Id] = child
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = child.Id, ToId = node.Id, Type = EdgeType.Inherits }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "BaseController", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("--kind inherits"));
    }

    [Fact]
    public void Generate_MultipleAssemblies_SuggestsProjectFilter()
    {
        var node1 = MakeNode("OrderService", assembly: "Orders.Core");
        var node2 = MakeNode("PaymentGateway", assembly: "Payments.Infrastructure");
        var result = new QueryResult
        {
            TargetNode = node1,
            MatchedNodes = new List<GraphNode> { node1 },
            Nodes = new Dictionary<string, GraphNode>
            {
                [node1.Id] = node1,
                [node2.Id] = node2
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node1.Id, ToId = node2.Id, Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("--project"));
    }

    [Fact]
    public void Generate_RespectsMaxSuggestions()
    {
        var node = MakeNode("OrderService");
        var iface = MakeNode("IOrderRepository");
        var child = MakeNode("ChildClass");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode>
            {
                [node.Id] = node,
                [iface.Id] = iface,
                [child.Id] = child
            },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node.Id, ToId = iface.Id, Type = EdgeType.Calls },
                new() { FromId = child.Id, ToId = node.Id, Type = EdgeType.Inherits }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrderService", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options, maxSuggestions: 2);

        Assert.True(suggestions.Count <= 2);
    }

    [Fact]
    public void Generate_AlreadyFilteredByCalls_DoesNotSuggestCallsAgain()
    {
        var node = MakeNode("OrderService");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions
        {
            Pattern = "OrderService",
            Depth = 1,
            EdgeTypeFilter = EdgeType.Calls
        };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.DoesNotContain(suggestions, s => s.Contains("--kind calls"));
    }

    [Fact]
    public void FormatHints_EmptyList_ReturnsEmptyString()
    {
        var result = QuerySuggestionGenerator.FormatHints(new List<string>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatHints_WithSuggestions_FormatsCorrectly()
    {
        var suggestions = new List<string>
        {
            "codegraph query Foo --depth 2",
            "codegraph query Foo --kind calls"
        };

        var result = QuerySuggestionGenerator.FormatHints(suggestions);

        Assert.Contains("Suggested next queries", result);
        Assert.Contains("codegraph query Foo --depth 2", result);
        Assert.Contains("codegraph query Foo --kind calls", result);
    }

    [Fact]
    public void Generate_NoEdges_NoSuggestionsForDeeperTraversal()
    {
        var node = MakeNode("OrphanType");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "OrphanType", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.DoesNotContain(suggestions, s => s.Contains("--depth 2"));
    }

    [Fact]
    public void Generate_SymbolWithSpaces_QuotesSymbolName()
    {
        var node = MakeNode("Order Service");
        var result = new QueryResult
        {
            TargetNode = node,
            MatchedNodes = new List<GraphNode> { node },
            Nodes = new Dictionary<string, GraphNode> { [node.Id] = node },
            Edges = new List<GraphEdge>
            {
                new() { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls }
            },
            Metadata = new GraphMetadata()
        };
        var options = new QueryOptions { Pattern = "Order Service", Depth = 1 };

        var suggestions = QuerySuggestionGenerator.Generate(result, options);

        Assert.Contains(suggestions, s => s.Contains("\"Order Service\""));
    }
}
