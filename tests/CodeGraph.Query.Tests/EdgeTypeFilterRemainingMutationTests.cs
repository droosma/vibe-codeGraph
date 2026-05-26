using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class EdgeTypeFilterRemainingMutationTests
{
    [Theory]
    [InlineData("handles-route", EdgeType.HandlesRoute)]
    [InlineData("binds-configuration", EdgeType.BindsConfiguration)]
    [InlineData("uses-middleware", EdgeType.UsesMiddleware)]
    [InlineData("maps-to-table", EdgeType.MapsToTable)]
    [InlineData("navigates-to", EdgeType.NavigatesTo)]
    [InlineData("configured-by", EdgeType.ConfiguredBy)]
    public void Parse_ExtendedAliases_ReturnExpectedEdgeType(string input, EdgeType expected)
    {
        var result = EdgeTypeFilter.Parse(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Parse_ErrorMessage_ContainsExtendedAliases()
    {
        var ex = Assert.Throws<ArgumentException>(() => EdgeTypeFilter.Parse("not-a-real-edge"));

        Assert.Contains("handles-route", ex.Message);
        Assert.Contains("binds-configuration", ex.Message);
        Assert.Contains("uses-middleware", ex.Message);
        Assert.Contains("maps-to-table", ex.Message);
        Assert.Contains("navigates-to", ex.Message);
        Assert.Contains("configured-by", ex.Message);
    }

    [Fact]
    public void Apply_NullFilter_ReturnsOriginalListInstance()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var filtered = EdgeTypeFilter.Apply(edges, null);

        Assert.Same(edges, filtered);
    }

    [Fact]
    public void Apply_FiltersExtendedAliasEdgeTypeOnly()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.ConfiguredBy },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls }
        };

        var filtered = EdgeTypeFilter.Apply(edges, EdgeTypeFilter.Parse("configured-by"));

        var edge = Assert.Single(filtered);
        Assert.Equal(EdgeType.ConfiguredBy, edge.Type);
        Assert.Equal("B", edge.ToId);
    }
}
