using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class SymbolExplainerTests
{
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildExplainGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.OrderService"] = new GraphNode
            {
                Id = "App.OrderService", Name = "OrderService", Kind = NodeKind.Type,
                FilePath = "src/OrderService.cs", StartLine = 10, EndLine = 100,
                Signature = "public class OrderService", DocComment = "Handles orders."
            },
            ["App.OrderService.PlaceOrder"] = new GraphNode
            {
                Id = "App.OrderService.PlaceOrder", Name = "PlaceOrder", Kind = NodeKind.Method,
                FilePath = "src/OrderService.cs", StartLine = 20, EndLine = 40
            },
            ["App.OrderService.GetOrder"] = new GraphNode
            {
                Id = "App.OrderService.GetOrder", Name = "GetOrder", Kind = NodeKind.Method,
                FilePath = "src/OrderService.cs", StartLine = 50, EndLine = 70
            },
            ["App.Controller"] = new GraphNode
            {
                Id = "App.Controller", Name = "Controller", Kind = NodeKind.Type,
                FilePath = "src/Controller.cs", StartLine = 1, EndLine = 30
            },
            ["App.Repository"] = new GraphNode
            {
                Id = "App.Repository", Name = "Repository", Kind = NodeKind.Type,
                FilePath = "src/Repository.cs", StartLine = 1, EndLine = 50
            },
            ["App.Tests.OrderServiceTest"] = new GraphNode
            {
                Id = "App.Tests.OrderServiceTest", Name = "OrderServiceTest", Kind = NodeKind.Type,
                FilePath = "tests/OrderServiceTest.cs", StartLine = 1, EndLine = 60
            }
        };
        var edges = new List<GraphEdge>
        {
            // Contains (members)
            new GraphEdge { FromId = "App.OrderService", ToId = "App.OrderService.PlaceOrder", Type = EdgeType.Contains },
            new GraphEdge { FromId = "App.OrderService", ToId = "App.OrderService.GetOrder", Type = EdgeType.Contains },
            // Outgoing calls
            new GraphEdge { FromId = "App.OrderService", ToId = "App.Repository", Type = EdgeType.Calls },
            // Incoming calls
            new GraphEdge { FromId = "App.Controller", ToId = "App.OrderService", Type = EdgeType.Calls },
            // Test coverage
            new GraphEdge { FromId = "App.Tests.OrderServiceTest", ToId = "App.OrderService", Type = EdgeType.Covers }
        };
        return (nodes, edges);
    }

    [Fact]
    public void Explain_ReturnsFullNodeInfo()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("App.OrderService");

        Assert.NotNull(result);
        Assert.Equal("App.OrderService", result!.Node.Id);
        Assert.Equal(NodeKind.Type, result.Node.Kind);
        Assert.Equal("public class OrderService", result.Node.Signature);
    }

    [Fact]
    public void Explain_ListsMembers()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("OrderService");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Members.Count);
        Assert.Contains(result.Members, m => m.Name == "PlaceOrder");
        Assert.Contains(result.Members, m => m.Name == "GetOrder");
    }

    [Fact]
    public void Explain_ListsTests()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("OrderService");

        Assert.NotNull(result);
        Assert.Single(result!.Tests);
        Assert.Equal("App.Tests.OrderServiceTest", result.Tests[0].Id);
    }

    [Fact]
    public void Explain_ListsIncomingAndOutgoingEdges()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("OrderService");

        Assert.NotNull(result);
        // Outgoing: 2 Contains + 1 Calls
        Assert.Equal(3, result!.OutgoingEdges.Count);
        // Incoming: 1 Calls + 1 Covers
        Assert.Equal(2, result.IncomingEdges.Count);
    }

    [Fact]
    public void Explain_NotFound_ReturnsNull()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void Explain_MatchesByNameSuffix()
    {
        var (nodes, edges) = BuildExplainGraph();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("PlaceOrder");

        Assert.NotNull(result);
        Assert.Equal("App.OrderService.PlaceOrder", result!.Node.Id);
    }

    [Fact]
    public void Explain_NodeWithNoEdges_ReturnsEmptyCollections()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Isolated"] = new GraphNode { Id = "Isolated", Name = "Isolated", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>();
        var explainer = new SymbolExplainer(nodes, edges);

        var result = explainer.Explain("Isolated");

        Assert.NotNull(result);
        Assert.Empty(result!.OutgoingEdges);
        Assert.Empty(result.IncomingEdges);
        Assert.Empty(result.Members);
        Assert.Empty(result.Tests);
    }
}
