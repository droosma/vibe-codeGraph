using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TestImpactAnalyzerTests
{
    /// <summary>
    /// Builds a graph with:
    /// - ProductionMethod1 (target)
    /// - ProductionMethod2 → calls ProductionMethod1, covered by TestMethod2
    /// - ProductionMethod3 → calls ProductionMethod1, has NO test coverage (uncovered caller)
    /// - TestMethod1 → covers ProductionMethod1 (direct CoveredBy)
    /// - TestMethod2 → covers ProductionMethod2 (indirect)
    /// </summary>
    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildTestGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.ProductionMethod1"] = new GraphNode
                { Id = "App.ProductionMethod1", Name = "ProductionMethod1", Kind = NodeKind.Method },
            ["App.ProductionMethod2"] = new GraphNode
                { Id = "App.ProductionMethod2", Name = "ProductionMethod2", Kind = NodeKind.Method },
            ["App.ProductionMethod3"] = new GraphNode
                { Id = "App.ProductionMethod3", Name = "ProductionMethod3", Kind = NodeKind.Method },
            ["App.Tests.TestMethod1"] = new GraphNode
            {
                Id = "App.Tests.TestMethod1", Name = "TestMethod1", Kind = NodeKind.Method,
                ContainingTypeId = "App.Tests.TestClass1"
            },
            ["App.Tests.TestMethod2"] = new GraphNode
            {
                Id = "App.Tests.TestMethod2", Name = "TestMethod2", Kind = NodeKind.Method,
                ContainingTypeId = "App.Tests.TestClass2"
            },
        };

        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "App.ProductionMethod2", ToId = "App.ProductionMethod1", Type = EdgeType.Calls },
            new GraphEdge { FromId = "App.ProductionMethod3", ToId = "App.ProductionMethod1", Type = EdgeType.Calls },
            new GraphEdge
            {
                FromId = "App.ProductionMethod1", ToId = "App.Tests.TestMethod1", Type = EdgeType.CoveredBy
            },
            new GraphEdge
            {
                FromId = "App.ProductionMethod2", ToId = "App.Tests.TestMethod2", Type = EdgeType.CoveredBy
            },
        };

        return (nodes, edges);
    }

    [Fact]
    public void Analyze_DirectCoverage_Found()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.ProductionMethod1");

        Assert.NotNull(result.Target);
        Assert.Equal("App.ProductionMethod1", result.Target!.Id);
        Assert.Single(result.DirectTests);
        Assert.Equal("App.Tests.TestMethod1", result.DirectTests[0].TestNode.Id);
    }

    [Fact]
    public void Analyze_IndirectCoverage_ViaCallers()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.ProductionMethod1");

        Assert.Single(result.IndirectTests);
        Assert.Equal("App.Tests.TestMethod2", result.IndirectTests[0].TestNode.Id);
        // Path should go from caller to target
        Assert.Equal(2, result.IndirectTests[0].PathFromTarget.Count);
        Assert.Equal("App.ProductionMethod2", result.IndirectTests[0].PathFromTarget[0].Id);
        Assert.Equal("App.ProductionMethod1", result.IndirectTests[0].PathFromTarget[1].Id);
    }

    [Fact]
    public void Analyze_UncoveredCallers_Identified()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.ProductionMethod1");

        Assert.Single(result.UncoveredCallers);
        Assert.Equal("App.ProductionMethod3", result.UncoveredCallers[0].Caller.Id);
        Assert.Equal(1, result.UncoveredCallers[0].Depth);
    }

    [Fact]
    public void Analyze_SymbolNotFound_ReturnsEmptyResult()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("NonExistent");

        Assert.Null(result.Target);
        Assert.Empty(result.DirectTests);
        Assert.Empty(result.IndirectTests);
        Assert.Empty(result.UncoveredCallers);
        Assert.Equal("", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_NoCoverage_AllCallersUncovered()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = new GraphNode { Id = "App.Target", Name = "Target", Kind = NodeKind.Method },
            ["App.Caller1"] = new GraphNode { Id = "App.Caller1", Name = "Caller1", Kind = NodeKind.Method },
            ["App.Caller2"] = new GraphNode { Id = "App.Caller2", Name = "Caller2", Kind = NodeKind.Method },
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "App.Caller1", ToId = "App.Target", Type = EdgeType.Calls },
            new GraphEdge { FromId = "App.Caller2", ToId = "App.Target", Type = EdgeType.Calls },
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.Target");

        Assert.NotNull(result.Target);
        Assert.Empty(result.DirectTests);
        Assert.Empty(result.IndirectTests);
        Assert.Equal(2, result.UncoveredCallers.Count);
        Assert.Equal("", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_TestCommand_CorrectFilterSyntax()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("App.ProductionMethod1");

        Assert.StartsWith("dotnet test --filter", result.SuggestedTestCommand);
        Assert.Contains("FullyQualifiedName~TestClass1", result.SuggestedTestCommand);
        Assert.Contains("FullyQualifiedName~TestClass2", result.SuggestedTestCommand);
        Assert.Contains("|", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_DepthLimiting_WorksCorrectly()
    {
        // Chain: A → B → C → Target, test covers A only
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = new GraphNode { Id = "Target", Name = "Target", Kind = NodeKind.Method },
            ["C"] = new GraphNode { Id = "C", Name = "C", Kind = NodeKind.Method },
            ["B"] = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method },
            ["A"] = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method },
            ["TestA"] = new GraphNode
                { Id = "TestA", Name = "TestA", Kind = NodeKind.Method, ContainingTypeId = "ATests" },
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "C", ToId = "Target", Type = EdgeType.Calls },
            new GraphEdge { FromId = "B", ToId = "C", Type = EdgeType.Calls },
            new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new GraphEdge { FromId = "A", ToId = "TestA", Type = EdgeType.CoveredBy },
        };

        var analyzer = new TestImpactAnalyzer(nodes, edges);

        // maxDepth=2 should NOT reach A (at depth 3)
        var shallow = analyzer.Analyze("Target", maxDepth: 2);
        Assert.Empty(shallow.IndirectTests);
        Assert.Equal(2, shallow.UncoveredCallers.Count);

        // maxDepth=3 should reach A
        var deep = analyzer.Analyze("Target", maxDepth: 3);
        Assert.Single(deep.IndirectTests);
        Assert.Equal("TestA", deep.IndirectTests[0].TestNode.Id);
    }

    [Fact]
    public void Analyze_MatchesByName()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("ProductionMethod1");

        Assert.NotNull(result.Target);
        Assert.Equal("App.ProductionMethod1", result.Target!.Id);
    }

    [Fact]
    public void Analyze_MatchesBySuffix()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("ProductionMethod1");

        Assert.NotNull(result.Target);
        Assert.Single(result.DirectTests);
    }

    [Fact]
    public void Formatter_SymbolNotFound_ShowsMessage()
    {
        var result = new TestImpactResult("Missing", null, new List<TestCoverage>(),
            new List<TestCoverage>(), new List<UncoveredCaller>(), "");

        var output = TestImpactFormatter.Format(result);

        Assert.Contains("No nodes found matching 'Missing'", output);
    }

    [Fact]
    public void Formatter_FullResult_ContainsAllSections()
    {
        var (nodes, edges) = BuildTestGraph();
        var analyzer = new TestImpactAnalyzer(nodes, edges);
        var result = analyzer.Analyze("App.ProductionMethod1");

        var output = TestImpactFormatter.Format(result);

        Assert.Contains("# Test impact for App.ProductionMethod1", output);
        Assert.Contains("## Direct coverage (1 test)", output);
        Assert.Contains("\u2713 App.Tests.TestMethod1", output);
        Assert.Contains("## Indirect coverage (1 test)", output);
        Assert.Contains("\u26a0 App.Tests.TestMethod2", output);
        Assert.Contains("via:", output);
        Assert.Contains("## Uncovered callers (1 path)", output);
        Assert.Contains("\u2717 App.ProductionMethod3 [depth: 1]", output);
        Assert.Contains("## Suggested test command", output);
        Assert.Contains("dotnet test --filter", output);
    }
}
