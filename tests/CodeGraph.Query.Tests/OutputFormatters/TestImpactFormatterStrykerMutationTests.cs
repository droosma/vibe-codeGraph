using System.Text.Json;
using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class TestImpactFormatterStrykerMutationTests
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
    public void FormatJson_WhenTargetIsMissing_SerializesNullTarget()
    {
        var result = new TestImpactResult(
            "MissingMethod",
            null,
            new List<TestCoverage>(),
            new List<TestCoverage>(),
            new List<UncoveredCaller>(),
            string.Empty);

        var json = TestImpactFormatter.FormatJson(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("target").ValueKind);
        Assert.False(document.RootElement.GetProperty("hasTests").GetBoolean());
    }

    [Fact]
    public void FormatJson_UsesArrowSeparator_AndNormalizesWhitespaceFields()
    {
        var target = MethodNode("MyApp.OrderService.PlaceOrder", "PlaceOrder", "src/OrderService.cs", "MyApp.OrderService");
        var test = MethodNode("Tests.OrderTests.ShouldRun", "ShouldRun", "   ", "Tests.OrderTests");
        var workflow = MethodNode("MyApp.OrderWorkflow.Execute", "Execute", containingTypeId: "MyApp.OrderWorkflow");
        var result = new TestImpactResult(
            "PlaceOrder",
            target,
            new List<TestCoverage>
            {
                new(test, new List<GraphNode> { workflow, target })
            },
            new List<TestCoverage>(),
            new List<UncoveredCaller>(),
            "dotnet test");

        var json = TestImpactFormatter.FormatJson(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var document = JsonDocument.Parse(json);

        var targetJson = document.RootElement.GetProperty("target");
        Assert.Equal("src/OrderService.cs", targetJson.GetProperty("filePath").GetString());
        Assert.Equal("dotnet test", document.RootElement.GetProperty("suggestedTestCommand").GetString());

        var directTest = document.RootElement.GetProperty("directTests")[0];
        Assert.Equal(JsonValueKind.Null, directTest.GetProperty("filePath").ValueKind);
        Assert.Equal("OrderWorkflow.Execute -> OrderService.PlaceOrder", directTest.GetProperty("via").GetString());
    }
}
