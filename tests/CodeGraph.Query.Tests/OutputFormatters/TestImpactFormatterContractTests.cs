using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TestImpactFormatterContractTests
{
    private static GraphNode MethodNode(string id, string name, string? filePath = null, string? containingTypeId = null) => new()
    {
        Id = id,
        Name = name,
        Kind = NodeKind.Method,
        FilePath = filePath ?? string.Empty,
        ContainingTypeId = containingTypeId,
        Accessibility = Accessibility.Public
    };

    [Fact]
    public void Format_ContextOutput_UsesExactSectionsOrderingAndSpacing()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService");
        var directWithEmptyContainingType = MethodNode("Tests.AlphaTests.ShouldRun", "ShouldRun", containingTypeId: string.Empty);
        var directWithFile = MethodNode("Tests.ZetaTests.ShouldReach", "ShouldReach", "tests/ZetaTests.cs", "Tests.ZetaTests");
        var indirect = MethodNode("Tests.IntegrationTests.FullFlow", "FullFlow", "tests/IntegrationTests.cs", "Tests.IntegrationTests");
        var workflow = MethodNode("MyApp.OrderWorkflow.Execute", "Execute", containingTypeId: "MyApp.OrderWorkflow");

        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>
            {
                new(directWithFile, new List<GraphNode> { workflow, target }),
                new(directWithEmptyContainingType, new List<GraphNode> { workflow, target })
            },
            new List<TestCoverage>
            {
                new(indirect, new List<GraphNode> { workflow, target })
            },
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "Affected tests for MyApp.OrderService.PlaceOrder:",
            string.Empty,
            "Direct coverage (tests that call this method):",
            "  AlphaTests.ShouldRun",
            "  ZetaTests.ShouldReach [tests/ZetaTests.cs]",
            string.Empty,
            "Transitive (tests reaching through call chain):",
            "  IntegrationTests.FullFlow [tests/IntegrationTests.cs] via OrderWorkflow.Execute → OrderService.PlaceOrder"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_CompactOutput_UsesExactLinesAndSuppressesDirectVia()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService");
        var directWithEmptyContainingType = MethodNode("Tests.AlphaTests.ShouldRun", "ShouldRun", containingTypeId: string.Empty);
        var directWithFile = MethodNode("Tests.ZetaTests.ShouldReach", "ShouldReach", "tests/ZetaTests.cs", "Tests.ZetaTests");
        var indirect = MethodNode("Tests.IntegrationTests.FullFlow", "FullFlow", "tests/IntegrationTests.cs", "Tests.IntegrationTests");
        var workflow = MethodNode("MyApp.OrderWorkflow.Execute", "Execute", containingTypeId: "MyApp.OrderWorkflow");

        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>
            {
                new(directWithFile, new List<GraphNode> { workflow, target }),
                new(directWithEmptyContainingType, new List<GraphNode> { workflow, target })
            },
            new List<TestCoverage>
            {
                new(indirect, new List<GraphNode> { workflow, target })
            },
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Compact);
        var expected = string.Join(Environment.NewLine, new[]
        {
            "MyApp.OrderService.PlaceOrder",
            "direct: ZetaTests.ShouldReach [tests/ZetaTests.cs]",
            "direct: AlphaTests.ShouldRun",
            "transitive: IntegrationTests.FullFlow via OrderWorkflow.Execute → OrderService.PlaceOrder [tests/IntegrationTests.cs]"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_TransitiveOutput_WithSingleNodePath_OmitsViaClause()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService");
        var indirect = MethodNode("Tests.SingleTests.ShouldRun", "ShouldRun", "tests/SingleTests.cs", "Tests.SingleTests");

        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>(),
            new List<TestCoverage>
            {
                new(indirect, new List<GraphNode> { target })
            },
            new List<UncoveredCaller>(),
            string.Empty);

        var contextOutput = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);
        var compactOutput = TestImpactFormatter.Format(result, TestImpactOutputFormat.Compact);

        Assert.Contains("  SingleTests.ShouldRun [tests/SingleTests.cs]", contextOutput);
        Assert.DoesNotContain(" via ", contextOutput);
        Assert.Contains("transitive: SingleTests.ShouldRun [tests/SingleTests.cs]", compactOutput);
        Assert.DoesNotContain(" via ", compactOutput);
    }
}
