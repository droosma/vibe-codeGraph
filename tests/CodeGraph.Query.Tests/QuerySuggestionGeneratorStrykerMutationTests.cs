using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class QuerySuggestionGeneratorStrykerMutationTests
{
    private static GraphNode MakeNode(string id, NodeKind kind = NodeKind.Type, string assembly = "Orders.Core")
        => new()
        {
            Id = id,
            Name = id,
            Kind = kind,
            FilePath = $"src/{id}.cs",
            AssemblyName = assembly
        };

    private static QueryResult CreateResult(GraphNode targetNode, IReadOnlyList<GraphNode> nodes, params GraphEdge[] edges)
    {
        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = new List<GraphNode> { targetNode },
            Nodes = nodes.ToDictionary(node => node.Id, node => node),
            Edges = edges.ToList(),
            Metadata = new GraphMetadata()
        };
    }

    [Fact]
    public void Generate_UsesActualInterfaceName_WhenOtherTypesOnlyLookLikeInterfaces()
    {
        var target = MakeNode("OrderService");
        var xmlParser = MakeNode("XMLParser");
        var singleLetter = MakeNode("I");
        var iface = MakeNode("IOrderRepository");
        var result = CreateResult(target, new[] { target, xmlParser, singleLetter, iface });

        var suggestions = QuerySuggestionGenerator.Generate(result, new QueryOptions { Pattern = target.Name, Depth = 2 }, maxSuggestions: 5);

        Assert.Equal(new[] { "codegraph query IOrderRepository --kind resolves-to --format compact" }, suggestions);
    }

    [Fact]
    public void Generate_SingleAssemblyWithoutTargetAssembly_DoesNotSuggestProjectFilter()
    {
        var target = MakeNode("OrderService", assembly: string.Empty);
        var peer = MakeNode("InvoiceService", assembly: "Orders.Core");
        var result = CreateResult(target, new[] { target, peer });

        var suggestions = QuerySuggestionGenerator.Generate(
            result,
            new QueryOptions { Pattern = target.Name, Depth = 2, EdgeTypeFilter = EdgeType.Calls },
            maxSuggestions: 5);

        Assert.Empty(suggestions);
    }
}
