using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TestImpactAnalyzerTests
{
    private static GraphNode MethodNode(string id, string name, string filePath, string? containingTypeId = null) => new()
    {
        Id = id,
        Name = name,
        Kind = NodeKind.Method,
        FilePath = filePath,
        ContainingTypeId = containingTypeId,
        Accessibility = Accessibility.Public
    };

    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.OrderService.PlaceOrder"] = MethodNode(
                "MyApp.OrderService.PlaceOrder",
                "PlaceOrder",
                "src/OrderService.cs",
                "MyApp.OrderService"),
            ["MyApp.OrderWorkflow.Execute"] = MethodNode(
                "MyApp.OrderWorkflow.Execute",
                "Execute",
                "src/OrderWorkflow.cs",
                "MyApp.OrderWorkflow"),
            ["MyApp.BackgroundOrderProcessor.ProcessPending"] = MethodNode(
                "MyApp.BackgroundOrderProcessor.ProcessPending",
                "ProcessPending",
                "src/BackgroundOrderProcessor.cs",
                "MyApp.BackgroundOrderProcessor"),
            ["MyApp.Tests.OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess"] = MethodNode(
                "MyApp.Tests.OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess",
                "PlaceOrder_ValidOrder_ReturnsSuccess",
                "Tests/OrderServiceTests.cs",
                "MyApp.Tests.OrderServiceTests"),
            ["MyApp.Tests.IntegrationTests.FullOrderFlow"] = MethodNode(
                "MyApp.Tests.IntegrationTests.FullOrderFlow",
                "FullOrderFlow",
                "Tests/Integration/OrderFlowTests.cs",
                "MyApp.Tests.IntegrationTests")
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "MyApp.OrderWorkflow.Execute", ToId = "MyApp.OrderService.PlaceOrder", Type = EdgeType.Calls },
            new() { FromId = "MyApp.BackgroundOrderProcessor.ProcessPending", ToId = "MyApp.OrderService.PlaceOrder", Type = EdgeType.Calls },
            new() { FromId = "MyApp.OrderService.PlaceOrder", ToId = "MyApp.Tests.OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess", Type = EdgeType.CoveredBy },
            new() { FromId = "MyApp.OrderWorkflow.Execute", ToId = "MyApp.Tests.IntegrationTests.FullOrderFlow", Type = EdgeType.CoveredBy }
        };

        return (nodes, edges);
    }

    [Fact]
    public void Analyze_FindsDirectAndTransitiveCoverage()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("MyApp.OrderService.PlaceOrder");

        Assert.NotNull(result.Target);
        Assert.Equal("MyApp.OrderService.PlaceOrder", result.Target!.Id);
        Assert.Single(result.DirectTests);
        Assert.Equal("MyApp.Tests.OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess", result.DirectTests[0].TestNode.Id);
        Assert.Single(result.IndirectTests);
        Assert.Equal("MyApp.Tests.IntegrationTests.FullOrderFlow", result.IndirectTests[0].TestNode.Id);
        Assert.Collection(result.IndirectTests[0].PathFromTarget,
            node => Assert.Equal("MyApp.OrderWorkflow.Execute", node.Id),
            node => Assert.Equal("MyApp.OrderService.PlaceOrder", node.Id));
    }

    [Fact]
    public void Analyze_DepthLimit_RestrictsTransitiveCoverage()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = MethodNode("Target", "Target", "src/Target.cs"),
            ["Caller"] = MethodNode("Caller", "Caller", "src/Caller.cs"),
            ["Gateway"] = MethodNode("Gateway", "Gateway", "src/Gateway.cs"),
            ["GatewayTests.ShouldReachTarget"] = MethodNode(
                "GatewayTests.ShouldReachTarget",
                "ShouldReachTarget",
                "Tests/GatewayTests.cs",
                "GatewayTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Gateway", ToId = "Caller", Type = EdgeType.Calls },
            new() { FromId = "Gateway", ToId = "GatewayTests.ShouldReachTarget", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var shallow = analyzer.Analyze("Target", maxDepth: 1);
        var deep = analyzer.Analyze("Target", maxDepth: 2);

        Assert.Empty(shallow.IndirectTests);
        Assert.Single(deep.IndirectTests);
        Assert.Equal("GatewayTests.ShouldReachTarget", deep.IndirectTests[0].TestNode.Id);
    }

    [Fact]
    public void Analyze_DoesNotDuplicateDirectTestsInTransitiveResults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = MethodNode("Target", "Target", "src/Target.cs"),
            ["Caller"] = MethodNode("Caller", "Caller", "src/Caller.cs"),
            ["Tests.TargetTests.ShouldRun"] = MethodNode(
                "Tests.TargetTests.ShouldRun",
                "ShouldRun",
                "Tests/TargetTests.cs",
                "Tests.TargetTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Target", ToId = "Tests.TargetTests.ShouldRun", Type = EdgeType.CoveredBy },
            new() { FromId = "Caller", ToId = "Tests.TargetTests.ShouldRun", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        Assert.Single(result.DirectTests);
        Assert.Empty(result.IndirectTests);
    }

    [Theory]
    [InlineData("PlaceOrder")]
    [InlineData("OrderService.PlaceOrder")]
    public void Analyze_MatchesByNameAndSuffix(string pattern)
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze(pattern);

        Assert.NotNull(result.Target);
        Assert.Equal("MyApp.OrderService.PlaceOrder", result.Target!.Id);
    }

    [Fact]
    public void Analyze_SymbolNotFound_ReturnsEmptyResult()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("MissingSymbol");

        Assert.Null(result.Target);
        Assert.Empty(result.DirectTests);
        Assert.Empty(result.IndirectTests);
    }

    [Fact]
    public void Format_ContextOutput_IncludesPathsAndFiles()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);
        var result = analyzer.Analyze("MyApp.OrderService.PlaceOrder");

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);

        Assert.Contains("Affected tests for MyApp.OrderService.PlaceOrder:", output);
        Assert.Contains("Direct coverage (tests that call this method):", output);
        Assert.Contains("OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess [Tests/OrderServiceTests.cs]", output);
        Assert.Contains("Transitive (tests reaching through call chain):", output);
        Assert.Contains("IntegrationTests.FullOrderFlow [Tests/Integration/OrderFlowTests.cs]", output);
        Assert.Contains("via OrderWorkflow.Execute → OrderService.PlaceOrder", output);
    }

    [Fact]
    public void Format_CompactOutput_IsConcise()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);
        var result = analyzer.Analyze("MyApp.OrderService.PlaceOrder");

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Compact);

        Assert.Contains("MyApp.OrderService.PlaceOrder", output);
        Assert.Contains("direct: OrderServiceTests.PlaceOrder_ValidOrder_ReturnsSuccess", output);
        Assert.Contains("transitive: IntegrationTests.FullOrderFlow via OrderWorkflow.Execute → OrderService.PlaceOrder", output);
        Assert.DoesNotContain("Direct coverage (tests that call this method):", output);
    }

    [Fact]
    public void Format_NoTestsFound_ShowsHelpfulMessage()
    {
        var result = new TestImpactResult(
            "MyApp.OrderService.PlaceOrder",
            MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService"),
            new List<TestCoverage>(),
            new List<TestCoverage>(),
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);

        Assert.Contains("No affected tests found for MyApp.OrderService.PlaceOrder.", output);
    }

    [Fact]
    public void Analyze_DirectTestWithoutContainingType_UsesMethodNameInSuggestedCommand()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = MethodNode("Target", "Target", "src/Target.cs"),
            ["Tests.ShouldRun"] = MethodNode("Tests.ShouldRun", "ShouldRun", "Tests/ShouldRun.cs")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Target", ToId = "Tests.ShouldRun", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        var direct = Assert.Single(result.DirectTests);
        Assert.Equal("Tests.ShouldRun", direct.TestNode.Id);
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~ShouldRun\"", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_SecondLevelUncoveredCaller_RetainsDepth()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = MethodNode("Target", "Target", "src/Target.cs"),
            ["Caller"] = MethodNode("Caller", "Caller", "src/Caller.cs"),
            ["Gateway"] = MethodNode("Gateway", "Gateway", "src/Gateway.cs")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Gateway", ToId = "Caller", Type = EdgeType.Calls }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target", maxDepth: 2);

        Assert.Collection(result.UncoveredCallers,
            caller => Assert.Equal(("Caller", 1), (caller.Caller.Id, caller.Depth)),
            caller => Assert.Equal(("Gateway", 2), (caller.Caller.Id, caller.Depth)));
    }

    [Fact]
    public void Analyze_MissingCallerNode_IsSkippedFromUncoveredCallers()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = MethodNode("Target", "Target", "src/Target.cs")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Missing.Caller", ToId = "Target", Type = EdgeType.Calls }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        Assert.Empty(result.DirectTests);
        Assert.Empty(result.IndirectTests);
        Assert.Empty(result.UncoveredCallers);
        Assert.Equal(string.Empty, result.SuggestedTestCommand);
    }
}
