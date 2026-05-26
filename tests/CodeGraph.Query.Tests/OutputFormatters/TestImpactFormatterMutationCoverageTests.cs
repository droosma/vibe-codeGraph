using System.Text.Json;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TestImpactFormatterMutationCoverageTests
{
    [Fact]
    public void Format_ContextOutput_WithNoDirectTests_ShowsNoneMarker()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService");
        var integrationTest = MethodNode("Tests.FullFlow", "FullFlow", "tests/FullFlow.cs", "Tests.FullFlow");
        var workflow = MethodNode("MyApp.Workflow.Execute", "Execute", containingTypeId: "MyApp.Workflow");
        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>(),
            new List<TestCoverage>
            {
                new(integrationTest, new List<GraphNode> { workflow, target })
            },
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Context);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "Affected tests for MyApp.OrderService.PlaceOrder:",
            string.Empty,
            "Direct coverage (tests that call this method):",
            "  (none)",
            string.Empty,
            "Transitive (tests reaching through call chain):",
            "  FullFlow.FullFlow [tests/FullFlow.cs] via Workflow.Execute → OrderService.PlaceOrder"
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_CompactOutput_WithTwoPartTestId_FallsBackToIdSegments()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", containingTypeId: "MyApp.OrderService");
        var directTest = MethodNode("Suite.ShouldRun", "ShouldRun", containingTypeId: string.Empty);
        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>
            {
                new(directTest, new List<GraphNode> { target })
            },
            new List<TestCoverage>(),
            new List<UncoveredCaller>(),
            string.Empty);

        var output = TestImpactFormatter.Format(result, TestImpactOutputFormat.Compact);

        Assert.Equal(string.Join(Environment.NewLine, new[]
        {
            "MyApp.OrderService.PlaceOrder",
            "direct: Suite.ShouldRun"
        }), output);
    }

    [Fact]
    public void FormatJson_UsesFallbackDisplayNameAndViaDisplayName()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", containingTypeId: "MyApp.OrderService");
        var directTest = MethodNode("Suite.ShouldRun", "ShouldRun", containingTypeId: string.Empty);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>
            {
                new(directTest, new List<GraphNode> { target })
            },
            new List<TestCoverage>(),
            new List<UncoveredCaller>(),
            string.Empty);

        var json = TestImpactFormatter.FormatJson(result, options);

        Assert.Contains("\"displayName\":\"Suite.ShouldRun\"", json);
        Assert.Contains("\"via\":\"Suite.ShouldRun\"", json);
    }

    private static GraphNode MethodNode(string id, string name, string? filePath = null, string? containingTypeId = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Method,
            FilePath = filePath ?? string.Empty,
            ContainingTypeId = containingTypeId,
            Accessibility = Accessibility.Public
        };
    }
}
