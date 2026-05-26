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

    private static QueryResult CreateResult(
        GraphNode? targetNode,
        IReadOnlyList<GraphNode> matchedNodes,
        IReadOnlyList<GraphNode> nodes,
        params GraphEdge[] edges)
    {
        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = matchedNodes.ToList(),
            Nodes = nodes.ToDictionary(node => node.Id, node => node),
            Edges = edges.ToList(),
            Metadata = new GraphMetadata()
        };
    }

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

    [Fact]
    public void Generate_TargetNodeTakesPrecedenceAndQuotesExactCommands()
    {
        var fallback = MakeNode("FallbackService", assembly: "Orders.Core");
        var target = MakeNode("Order Service", assembly: "Orders.Core");
        var paymentGateway = MakeNode("PaymentGateway", assembly: "Payments.Infrastructure");
        var result = CreateResult(
            target,
            new[] { fallback },
            new[] { fallback, target, paymentGateway },
            new GraphEdge { FromId = target.Id, ToId = paymentGateway.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 1 });

        Assert.Equal(new[]
        {
            "codegraph query \"Order Service\" --depth 2 --format compact",
            "codegraph query \"Order Service\" --depth 3 --kind calls --format compact",
            "codegraph query \"Order Service\" --project Payments.Infrastructure --format compact"
        }, suggestions);
    }

    [Fact]
    public void Generate_InterfaceDiscoveryAndNeighbors_ReturnExactSuggestions()
    {
        var target = MakeNode("OrderService");
        var iface = MakeNode("IOrderRepository");
        var payment = MakeNode("PaymentService");
        var shipping = MakeNode("ShippingService");
        var session = new QuerySessionTracker();
        session.Record(iface.Name, depth: 1, resultCount: 1);
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target, iface, payment, shipping },
            new GraphEdge { FromId = target.Id, ToId = iface.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(
            result,
            new QueryOptions { Pattern = target.Name, Depth = 1 },
            session,
            maxSuggestions: 5);

        Assert.Equal(new[]
        {
            "codegraph query OrderService --depth 2 --format compact",
            "codegraph query OrderService --depth 3 --kind calls --format compact",
            "codegraph query IOrderRepository --kind resolves-to --format compact",
            "codegraph query PaymentService --depth 1 --format compact",
            "codegraph query ShippingService --depth 1 --format compact"
        }, suggestions);
    }

    [Fact]
    public void Generate_ImplementationsAndInheritance_ReturnExactSuggestions()
    {
        var target = MakeNode("OrderService");
        var iface = MakeNode("IOrderRepository");
        var child = MakeNode("DerivedOrderService");
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target, iface, child },
            new GraphEdge { FromId = target.Id, ToId = iface.Id, Type = EdgeType.ResolvesTo },
            new GraphEdge { FromId = child.Id, ToId = target.Id, Type = EdgeType.Inherits });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2 });

        Assert.Equal(new[]
        {
            "codegraph query IOrderRepository --kind implements --format compact",
            "codegraph query OrderService --kind inherits --depth 3 --format compact"
        }, suggestions);
    }

    [Fact]
    public void Generate_SessionAndProjectFilterSuppressRepeatedSuggestions()
    {
        var target = MakeNode("OrderService", assembly: "Orders.Core");
        var payment = MakeNode("PaymentService", kind: NodeKind.Method, assembly: "Payments.Infrastructure");
        var session = new QuerySessionTracker();
        session.Record(target.Name, depth: 2, resultCount: 2);
        session.Record(target.Name + " --kind calls", depth: 1, resultCount: 1);
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target, payment },
            new GraphEdge { FromId = target.Id, ToId = payment.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(
            result,
            new QueryOptions { Pattern = target.Name, Depth = 1, ProjectFilter = payment.AssemblyName },
            session,
            maxSuggestions: 5);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_AfterThreeQueries_ReturnsExactReportSuggestion()
    {
        var node = MakeNode("OrderService");
        var session = new QuerySessionTracker();
        session.Record("A", depth: 1, resultCount: 1);
        session.Record("B", depth: 1, resultCount: 1);
        session.Record("C", depth: 2, resultCount: 1);
        var result = CreateResult(
            node,
            new[] { node },
            new[] { node },
            new GraphEdge { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = node.Name, Depth = 1 }, session, maxSuggestions: 5);

        Assert.Equal(new[] { "codegraph report --format compact" }, suggestions);
    }

    [Fact]
    public void Generate_TargetNodeNullAndNoMatches_ReturnsEmpty()
    {
        var node = MakeNode("OrderService");
        var result = CreateResult(null, Array.Empty<GraphNode>(), new[] { node });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = node.Name, Depth = 1 });

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_TargetNodeNull_UsesFirstMatchedNodeForSuggestions()
    {
        var target = MakeNode("OrderService", assembly: "Orders.Core");
        var payment = MakeNode("PaymentGateway", assembly: "Payments.Infrastructure");
        var result = CreateResult(
            null,
            new[] { target },
            new[] { target, payment },
            new GraphEdge { FromId = target.Id, ToId = payment.Id, Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 1 });

        Assert.Equal(new[]
        {
            "codegraph query OrderService --depth 2 --format compact",
            "codegraph query OrderService --depth 3 --kind calls --format compact",
            "codegraph query OrderService --project Payments.Infrastructure --format compact"
        }, suggestions);
    }

    [Fact]
    public void Generate_WildcardSymbol_QuotesCommands()
    {
        var node = MakeNode("Order*");
        var result = CreateResult(
            node,
            new[] { node },
            new[] { node },
            new GraphEdge { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = node.Name, Depth = 1 });

        Assert.Contains("codegraph query \"Order*\" --depth 2 --format compact", suggestions);
        Assert.Contains("codegraph query \"Order*\" --depth 3 --kind calls --format compact", suggestions);
    }

    [Fact]
    public void Generate_ImplementsFilter_SuppressesImplementationSuggestion()
    {
        var target = MakeNode("OrderService");
        var iface = MakeNode("IOrderRepository");
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target, iface },
            new GraphEdge { FromId = target.Id, ToId = iface.Id, Type = EdgeType.ResolvesTo });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2, EdgeTypeFilter = EdgeType.Implements });

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_InheritsFilter_SuppressesInheritanceSuggestion()
    {
        var target = MakeNode("OrderService");
        var child = MakeNode("DerivedOrderService");
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target, child },
            new GraphEdge { FromId = child.Id, ToId = target.Id, Type = EdgeType.Inherits });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2, EdgeTypeFilter = EdgeType.Inherits });

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Generate_MaxSuggestionsZero_ReturnsEmpty()
    {
        var node = MakeNode("OrderService");
        var result = CreateResult(
            node,
            new[] { node },
            new[] { node },
            new GraphEdge { FromId = node.Id, ToId = "Other", Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = node.Name, Depth = 1 }, maxSuggestions: 0);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void FormatHints_WithSuggestions_ReturnsExactHintBlock()
    {
        var result = QuerySuggestionGenerator.FormatHints(new List<string>
        {
            "codegraph query Foo --depth 2 --format compact",
            "codegraph query Foo --kind calls --format compact"
        });

        var expected = string.Join("\n", new[]
        {
            string.Empty,
            "💡 Suggested next queries:",
            "   codegraph query Foo --depth 2 --format compact",
            "   codegraph query Foo --kind calls --format compact"
        });

        Assert.Equal(expected, result.ReplaceLineEndings("\n"));
    }
}
