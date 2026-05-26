using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class NamespaceFilterRemainingMutationTests
{
    [Fact]
    public void Apply_ExactPattern_DoesNotMatchLongerNamespacesOrIds()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Exact"] = new() { Id = "Exact", Name = "Exact", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services" },
            ["LongerNamespace"] = new() { Id = "LongerNamespace", Name = "LongerNamespace", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services.More" },
            ["MyApp.Services.More.Order"] = new() { Id = "MyApp.Services.More.Order", Name = "Order", Kind = NodeKind.Method }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services");

        var match = Assert.Single(result);
        Assert.Equal("Exact", match.Key);
    }
}
