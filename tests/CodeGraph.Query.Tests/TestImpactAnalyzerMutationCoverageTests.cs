using System.Threading.Tasks;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class TestImpactAnalyzerMutationCoverageTests
{
    private static GraphNode CreateMethodNode(string id, string name, string? containingTypeId = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Method,
            FilePath = $"{name}.cs",
            ContainingTypeId = containingTypeId,
            Accessibility = Accessibility.Public
        };
    }

    [Fact]
    public void Analyze_MultipleMatches_SelectsAlphabeticallyFirstTargetAndSortsDirectTests()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Alpha.Target"] = CreateMethodNode("App.Alpha.Target", "Target", "App.Alpha.TargetType"),
            ["App.Beta.Target"] = CreateMethodNode("App.Beta.Target", "Target", "App.Beta.TargetType"),
            ["Tests.AlphaTests.ShouldPass"] = CreateMethodNode("Tests.AlphaTests.ShouldPass", "ShouldPass", "Tests.AlphaTests"),
            ["Tests.ZuluTests.ShouldPass"] = CreateMethodNode("Tests.ZuluTests.ShouldPass", "ShouldPass", "Tests.ZuluTests"),
            ["Tests.BetaTests.ShouldPass"] = CreateMethodNode("Tests.BetaTests.ShouldPass", "ShouldPass", "Tests.BetaTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Beta.Target", ToId = "Tests.BetaTests.ShouldPass", Type = EdgeType.CoveredBy },
            new() { FromId = "App.Alpha.Target", ToId = "Tests.ZuluTests.ShouldPass", Type = EdgeType.CoveredBy },
            new() { FromId = "App.Alpha.Target", ToId = "Tests.AlphaTests.ShouldPass", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        Assert.Equal("App.Alpha.Target", result.Target!.Id);
        Assert.Collection(result.DirectTests,
            coverage => Assert.Equal("Tests.AlphaTests.ShouldPass", coverage.TestNode.Id),
            coverage => Assert.Equal("Tests.ZuluTests.ShouldPass", coverage.TestNode.Id));
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~AlphaTests|FullyQualifiedName~ZuluTests\"", result.SuggestedTestCommand);
        Assert.Empty(result.IndirectTests);
    }

    [Fact]
    public void Analyze_IndirectCoverage_BuildsFullPathInOrder()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = CreateMethodNode("App.Target", "Target", "App.TargetType"),
            ["App.Workflow"] = CreateMethodNode("App.Workflow", "Workflow", "App.WorkflowType"),
            ["App.Api"] = CreateMethodNode("App.Api", "Api", "App.ApiType"),
            ["Tests.ApiTests.ShouldCoverTarget"] = CreateMethodNode("Tests.ApiTests.ShouldCoverTarget", "ShouldCoverTarget", "Tests.ApiTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Workflow", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "App.Api", ToId = "App.Workflow", Type = EdgeType.Calls },
            new() { FromId = "App.Api", ToId = "Tests.ApiTests.ShouldCoverTarget", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target", maxDepth: 2);

        var coverage = Assert.Single(result.IndirectTests);
        Assert.Equal("Tests.ApiTests.ShouldCoverTarget", coverage.TestNode.Id);
        Assert.Collection(coverage.PathFromTarget,
            node => Assert.Equal("App.Api", node.Id),
            node => Assert.Equal("App.Workflow", node.Id),
            node => Assert.Equal("App.Target", node.Id));
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~ApiTests\"", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_UncoveredCallers_AreOrderedAlphabeticallyWithDepth()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = CreateMethodNode("App.Target", "Target", "App.TargetType"),
            ["Callers.Alpha"] = CreateMethodNode("Callers.Alpha", "Alpha", "Callers.AlphaType"),
            ["Callers.Zeta"] = CreateMethodNode("Callers.Zeta", "Zeta", "Callers.ZetaType")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Callers.Zeta", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "Callers.Alpha", ToId = "App.Target", Type = EdgeType.Calls }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        Assert.Empty(result.DirectTests);
        Assert.Empty(result.IndirectTests);
        Assert.Collection(result.UncoveredCallers,
            caller => Assert.Equal(("Callers.Alpha", 1), (caller.Caller.Id, caller.Depth)),
            caller => Assert.Equal(("Callers.Zeta", 1), (caller.Caller.Id, caller.Depth)));
        Assert.Equal(string.Empty, result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_SuggestedTestCommand_DeduplicatesAndSortsClassNamesAcrossDirectAndIndirectCoverage()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = CreateMethodNode("App.Target", "Target", "App.TargetType"),
            ["App.Caller"] = CreateMethodNode("App.Caller", "Caller", "App.CallerType"),
            ["Tests.ZuluTests.Direct"] = CreateMethodNode("Tests.ZuluTests.Direct", "Direct", "Tests.ZuluTests"),
            ["Tests.AlphaTests.Direct"] = CreateMethodNode("Tests.AlphaTests.Direct", "Direct", "Tests.AlphaTests"),
            ["Tests.AlphaTests.Indirect"] = CreateMethodNode("Tests.AlphaTests.Indirect", "Indirect", "Tests.AlphaTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Target", ToId = "Tests.ZuluTests.Direct", Type = EdgeType.CoveredBy },
            new() { FromId = "App.Target", ToId = "Tests.AlphaTests.Direct", Type = EdgeType.CoveredBy },
            new() { FromId = "App.Caller", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "App.Caller", ToId = "Tests.AlphaTests.Indirect", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target");

        Assert.Collection(result.DirectTests,
            coverage => Assert.Equal("Tests.AlphaTests.Direct", coverage.TestNode.Id),
            coverage => Assert.Equal("Tests.ZuluTests.Direct", coverage.TestNode.Id));
        var indirect = Assert.Single(result.IndirectTests);
        Assert.Equal("Tests.AlphaTests.Indirect", indirect.TestNode.Id);
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~AlphaTests|FullyQualifiedName~ZuluTests\"", result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_SharedCallerPath_UsesAlphabeticallyFirstParent()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = CreateMethodNode("App.Target", "Target", "App.TargetType"),
            ["Callers.Alpha"] = CreateMethodNode("Callers.Alpha", "Alpha", "Callers.AlphaType"),
            ["Callers.Zeta"] = CreateMethodNode("Callers.Zeta", "Zeta", "Callers.ZetaType"),
            ["Callers.Root"] = CreateMethodNode("Callers.Root", "Root", "Callers.RootType"),
            ["Tests.RootTests.ShouldRun"] = CreateMethodNode("Tests.RootTests.ShouldRun", "ShouldRun", "Tests.RootTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Callers.Alpha", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "Callers.Zeta", ToId = "App.Target", Type = EdgeType.Calls },
            new() { FromId = "Callers.Root", ToId = "Callers.Alpha", Type = EdgeType.Calls },
            new() { FromId = "Callers.Root", ToId = "Callers.Zeta", Type = EdgeType.Calls },
            new() { FromId = "Callers.Root", ToId = "Tests.RootTests.ShouldRun", Type = EdgeType.CoveredBy }
        };
        var analyzer = new TestImpactAnalyzer(nodes, edges);

        var result = analyzer.Analyze("Target", maxDepth: 2);

        var coverage = Assert.Single(result.IndirectTests);
        Assert.Collection(coverage.PathFromTarget,
            node => Assert.Equal("Callers.Root", node.Id),
            node => Assert.Equal("Callers.Alpha", node.Id),
            node => Assert.Equal("App.Target", node.Id));
    }

    [Fact]
    public void Analyze_NoAdditionalCallers_CompletesQuicklyEvenWithHugeDepth()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Target"] = CreateMethodNode("App.Target", "Target", "App.TargetType")
        };
        var analyzer = new TestImpactAnalyzer(nodes, new List<GraphEdge>());

        var task = Task.Run(() => analyzer.Analyze("Target", maxDepth: int.MaxValue));

        Assert.True(task.Wait(TimeSpan.FromSeconds(1)), "Analyze should stop once no further callers are found.");
        Assert.Equal("App.Target", task.Result.Target!.Id);
    }
}
