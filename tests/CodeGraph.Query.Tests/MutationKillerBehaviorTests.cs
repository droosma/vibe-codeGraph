using System.Reflection;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class MutationKillerBehaviorTests
{
    private static GraphNode Node(
        string id,
        string name,
        NodeKind kind = NodeKind.Type,
        string assemblyName = "App",
        string? containingTypeId = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = kind,
            AssemblyName = assemblyName,
            ContainingTypeId = containingTypeId,
            Accessibility = Accessibility.Public
        };
    }

    private static GraphEdge ExternalEdge(string fromId, string toId, string packageSource, EdgeType type = EdgeType.Calls)
        => new()
        {
            FromId = fromId,
            ToId = toId,
            Type = type,
            IsExternal = true,
            PackageSource = packageSource
        };

    [Fact]
    public void FindWhoUses_SortsDependentsAlphabeticallyWithinSameProjectAndKind()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Alpha.ZWorker.Run()"] = Node("Alpha.ZWorker.Run()", "ZRun", NodeKind.Method, "Alpha"),
            ["Alpha.AWorker.Run()"] = Node("Alpha.AWorker.Run()", "ARun", NodeKind.Method, "Alpha")
        };
        var edges = new List<GraphEdge>
        {
            ExternalEdge("Alpha.ZWorker.Run()", "Pkg.External.Zeta", "Pkg/1.0.0"),
            ExternalEdge("Alpha.AWorker.Run()", "Pkg.External.Alpha", "Pkg/1.0.0")
        };

        var result = new PackageQueryEngine(nodes, edges).FindWhoUses("Pkg");

        Assert.Equal(new[] { "Alpha.AWorker.Run()", "Alpha.ZWorker.Run()" }, result.Select(dependent => dependent.ConsumerId));
    }

    [Fact]
    public void FindConflicts_SortsPackagesAlphabetically()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.A"] = Node("App.A", "A", assemblyName: "App"),
            ["Other.A"] = Node("Other.A", "A", assemblyName: "Other")
        };
        var edges = new List<GraphEdge>
        {
            ExternalEdge("App.A", "Zeta.Type", "Zeta/1.0.0"),
            ExternalEdge("Other.A", "Zeta.Type", "Zeta/2.0.0"),
            ExternalEdge("App.A", "Alpha.Type", "Alpha/1.0.0"),
            ExternalEdge("Other.A", "Alpha.Type", "Alpha/2.0.0")
        };

        var conflicts = new PackageAnalyzer(nodes, edges).FindConflicts();

        Assert.Equal(new[] { "Alpha", "Zeta" }, conflicts.Select(conflict => conflict.PackageId));
    }

    [Fact]
    public void AnalyzeByProject_IgnoresInternalEdgesEvenWhenPackageSourceIsPresent()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = Node("App.Service", "Service", assemblyName: "App")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "App.Service", ToId = "Pkg.Internal", Type = EdgeType.Calls, IsExternal = false, PackageSource = "Ignored/9.9.9" },
            ExternalEdge("App.Service", "Pkg.External", "Kept/1.0.0")
        };

        var usage = Assert.Single(new PackageAnalyzer(nodes, edges).AnalyzeByProject());

        Assert.Equal("Kept", usage.PackageId);
    }

    [Fact]
    public void FindWhoUses_KnownNodeUsesNodeNameAndAssemblyName()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["DifferentPrefix.Component.Handle()"] = Node("DifferentPrefix.Component.Handle()", "HandleOrder", NodeKind.Method, "Billing")
        };
        var edges = new List<GraphEdge>
        {
            ExternalEdge("DifferentPrefix.Component.Handle()", "Pkg.Type", "Pkg/1.0.0")
        };

        var dependent = Assert.Single(new PackageQueryEngine(nodes, edges).FindWhoUses("Pkg"));

        Assert.Equal("Billing", dependent.ProjectName);
        Assert.Equal("HandleOrder", dependent.ConsumerName);
    }

    [Fact]
    public void FindWhoUses_MissingNodeWithoutDot_UsesFullIdForProjectAndName()
    {
        var edges = new List<GraphEdge>
        {
            ExternalEdge("Standalone", "Pkg.Type", "Pkg/1.0.0")
        };

        var dependent = Assert.Single(new PackageQueryEngine(new Dictionary<string, GraphNode>(), edges).FindWhoUses("Pkg"));

        Assert.Equal("Standalone", dependent.ProjectName);
        Assert.Equal("Standalone", dependent.ConsumerName);
    }

    [Fact]
    public void FindWhoUses_MissingNodeWithTrailingDot_UsesFullIdForConsumerName()
    {
        var edges = new List<GraphEdge>
        {
            ExternalEdge("Standalone.", "Pkg.Type", "Pkg/1.0.0")
        };

        var dependent = Assert.Single(new PackageQueryEngine(new Dictionary<string, GraphNode>(), edges).FindWhoUses("Pkg"));

        Assert.Equal("Standalone", dependent.ProjectName);
        Assert.Equal("Standalone.", dependent.ConsumerName);
    }

    [Fact]
    public void FindWhoUses_MissingNodeStartingWithDot_UsesSuffixNameButFullProjectId()
    {
        var edges = new List<GraphEdge>
        {
            ExternalEdge(".HiddenType", "Pkg.Type", "Pkg/1.0.0")
        };

        var dependent = Assert.Single(new PackageQueryEngine(new Dictionary<string, GraphNode>(), edges).FindWhoUses("Pkg"));

        Assert.Equal(".HiddenType", dependent.ProjectName);
        Assert.Equal("HiddenType", dependent.ConsumerName);
    }

    [Fact]
    public void Analyze_MatchesBySuffixWhenNodeNameDoesNotMatch()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Special.Repository"] = Node("App.Special.Repository", "DataLayer")
        };

        var result = new ImpactAnalyzer(nodes, new List<GraphEdge>()).Analyze("Repository");

        Assert.Equal("App.Special.Repository", result.Target!.Id);
    }

    [Fact]
    public void Analyze_Cycle_DoesNotRevisitTarget()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target"),
            ["Caller"] = Node("Caller", "Caller")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Target", ToId = "Caller", Type = EdgeType.Calls }
        };

        var result = new ImpactAnalyzer(nodes, edges).Analyze("Target", maxDepth: 3);

        var layer = Assert.Single(result.Layers);
        var node = Assert.Single(layer.Nodes);
        Assert.Equal("Caller", node.Id);
    }

    [Fact]
    public void Analyze_DoesNotMatchIdsThatOnlyEndWithPatternWithoutSeparator()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.SpecialRepository"] = Node("App.SpecialRepository", "DataLayer")
        };

        var result = new ImpactAnalyzer(nodes, new List<GraphEdge>()).Analyze("Repository");

        Assert.Null(result.Target);
    }

    [Fact]
    public void Analyze_SymbolNotFound_ReturnsEmptySuggestedCommand()
    {
        var result = new TestImpactAnalyzer(new Dictionary<string, GraphNode>(), new List<GraphEdge>()).Analyze("Missing");

        Assert.Equal(string.Empty, result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_DirectCoverage_PathContainsOnlyTarget()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType"),
            ["Tests.TargetTests.ShouldRun"] = Node("Tests.TargetTests.ShouldRun", "ShouldRun", NodeKind.Method, containingTypeId: "Tests.TargetTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Target", ToId = "Tests.TargetTests.ShouldRun", Type = EdgeType.CoveredBy }
        };

        var direct = Assert.Single(new TestImpactAnalyzer(nodes, edges).Analyze("Target").DirectTests);

        var pathNode = Assert.Single(direct.PathFromTarget);
        Assert.Equal("Target", pathNode.Id);
    }

    [Fact]
    public void Analyze_DirectCoverage_MissingTestNode_IsIgnored()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Target", ToId = "Missing.Test", Type = EdgeType.CoveredBy }
        };

        var result = new TestImpactAnalyzer(nodes, edges).Analyze("Target");

        Assert.Empty(result.DirectTests);
        Assert.Equal(string.Empty, result.SuggestedTestCommand);
    }

    [Fact]
    public void Analyze_IndirectTestsAreSortedAlphabeticallyByTestId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType"),
            ["Caller"] = Node("Caller", "Caller", NodeKind.Method, containingTypeId: "App.CallerType"),
            ["Tests.ZuluTests.ShouldRun"] = Node("Tests.ZuluTests.ShouldRun", "ShouldRun", NodeKind.Method, containingTypeId: "Tests.ZuluTests"),
            ["Tests.AlphaTests.ShouldRun"] = Node("Tests.AlphaTests.ShouldRun", "ShouldRun", NodeKind.Method, containingTypeId: "Tests.AlphaTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Caller", ToId = "Tests.ZuluTests.ShouldRun", Type = EdgeType.CoveredBy },
            new() { FromId = "Caller", ToId = "Tests.AlphaTests.ShouldRun", Type = EdgeType.CoveredBy }
        };

        var result = new TestImpactAnalyzer(nodes, edges).Analyze("Target");

        Assert.Equal(new[] { "Tests.AlphaTests.ShouldRun", "Tests.ZuluTests.ShouldRun" }, result.IndirectTests.Select(test => test.TestNode.Id));
    }

    [Fact]
    public void Analyze_DuplicateDirectCoverageStillMarksCallerAsCovered()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType"),
            ["Caller"] = Node("Caller", "Caller", NodeKind.Method, containingTypeId: "App.CallerType"),
            ["Tests.TargetTests.ShouldRun"] = Node("Tests.TargetTests.ShouldRun", "ShouldRun", NodeKind.Method, containingTypeId: "Tests.TargetTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Target", ToId = "Tests.TargetTests.ShouldRun", Type = EdgeType.CoveredBy },
            new() { FromId = "Caller", ToId = "Tests.TargetTests.ShouldRun", Type = EdgeType.CoveredBy }
        };

        var result = new TestImpactAnalyzer(nodes, edges).Analyze("Target");

        Assert.Empty(result.UncoveredCallers);
    }

    [Fact]
    public void Analyze_IndirectCoverageWithCycle_DoesNotAddTargetAsUncoveredCaller()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType"),
            ["Caller"] = Node("Caller", "Caller", NodeKind.Method, containingTypeId: "App.CallerType"),
            ["Tests.CallerTests.ShouldRun"] = Node("Tests.CallerTests.ShouldRun", "ShouldRun", NodeKind.Method, containingTypeId: "Tests.CallerTests")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Target", ToId = "Caller", Type = EdgeType.Calls },
            new() { FromId = "Caller", ToId = "Tests.CallerTests.ShouldRun", Type = EdgeType.CoveredBy }
        };

        var result = new TestImpactAnalyzer(nodes, edges).Analyze("Target", maxDepth: 3);

        Assert.Single(result.IndirectTests);
        Assert.Empty(result.UncoveredCallers);
    }

    [Fact]
    public void Analyze_IndirectCoverage_MissingTestNodeLeavesCallerUncovered()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType"),
            ["Caller"] = Node("Caller", "Caller", NodeKind.Method, containingTypeId: "App.CallerType")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Caller", ToId = "Target", Type = EdgeType.Calls },
            new() { FromId = "Caller", ToId = "Missing.Test", Type = EdgeType.CoveredBy }
        };

        var result = new TestImpactAnalyzer(nodes, edges).Analyze("Target");

        Assert.Empty(result.IndirectTests);
        var uncovered = Assert.Single(result.UncoveredCallers);
        Assert.Equal("Caller", uncovered.Caller.Id);
    }

    [Fact]
    public void BuildPath_WithMissingParentMap_AddsSourceAndTargetOnly()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Caller"] = Node("Caller", "Caller", NodeKind.Method, containingTypeId: "App.CallerType"),
            ["Target"] = Node("Target", "Target", NodeKind.Method, containingTypeId: "App.TargetType")
        };
        var analyzer = new TestImpactAnalyzer(nodes, new List<GraphEdge>());
        var method = typeof(TestImpactAnalyzer).GetMethod("BuildPath", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var path = (List<GraphNode>)method.Invoke(analyzer, new object[] { "Caller", "Target", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) })!;

        Assert.Equal(new[] { "Caller", "Target" }, path.Select(node => node.Id));
    }

    [Fact]
    public void BuildTestCommand_WithEmptyClassNames_ReturnsEmpty()
    {
        var method = typeof(TestImpactAnalyzer).GetMethod("BuildTestCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
        var direct = new List<TestCoverage>
        {
            new(Node("Tests.MissingName", string.Empty, NodeKind.Method, containingTypeId: null), new List<GraphNode>())
        };

        var command = (string)method.Invoke(null, new object[] { direct, new List<TestCoverage>() })!;

        Assert.Equal(string.Empty, command);
    }

    [Fact]
    public void Explain_SuffixMatch_UsesIdSuffixWhenNameDiffers()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service.Execute"] = Node("App.Service.Execute", "Handle", NodeKind.Method)
        };

        var result = new SymbolExplainer(nodes, new List<GraphEdge>()).Explain("Execute");

        Assert.Equal("App.Service.Execute", result!.Node.Id);
    }

    [Fact]
    public void Explain_IncomingAndOutgoingCoverageIncludeBothDistinctTests()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = Node("App.Service", "Service"),
            ["Tests.Incoming"] = Node("Tests.Incoming", "IncomingTest"),
            ["Tests.Outgoing"] = Node("Tests.Outgoing", "OutgoingTest")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Tests.Incoming", ToId = "App.Service", Type = EdgeType.Covers },
            new() { FromId = "App.Service", ToId = "Tests.Outgoing", Type = EdgeType.CoveredBy }
        };

        var result = new SymbolExplainer(nodes, edges).Explain("Service");

        Assert.Equal(2, result!.Tests.Count);
        Assert.Contains(result.Tests, node => node.Id == "Tests.Incoming");
        Assert.Contains(result.Tests, node => node.Id == "Tests.Outgoing");
    }

    [Fact]
    public void Explain_DoesNotMatchIdsThatOnlyEndWithPatternWithoutSeparator()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.SpecialExecute"] = Node("App.SpecialExecute", "Handle", NodeKind.Method)
        };

        var result = new SymbolExplainer(nodes, new List<GraphEdge>()).Explain("Execute");

        Assert.Null(result);
    }
}
