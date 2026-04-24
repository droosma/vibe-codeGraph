using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class EdgeTypeFilterTests
{
    [Theory]
    [InlineData("calls-to", EdgeType.Calls)]
    [InlineData("calls", EdgeType.Calls)]
    [InlineData("calls-from", EdgeType.Calls)]
    [InlineData("inherits", EdgeType.Inherits)]
    [InlineData("implements", EdgeType.Implements)]
    [InlineData("depends-on", EdgeType.DependsOn)]
    [InlineData("resolves-to", EdgeType.ResolvesTo)]
    [InlineData("covers", EdgeType.Covers)]
    [InlineData("covered-by", EdgeType.CoveredBy)]
    [InlineData("references", EdgeType.References)]
    [InlineData("overrides", EdgeType.Overrides)]
    [InlineData("contains", EdgeType.Contains)]
    public void Parse_ValidStrings_ReturnsCorrectEdgeType(string input, EdgeType expected)
    {
        var result = EdgeTypeFilter.Parse(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Parse_All_ReturnsNull()
    {
        var result = EdgeTypeFilter.Parse("all");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_Null_ReturnsNull()
    {
        var result = EdgeTypeFilter.Parse(null);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = EdgeTypeFilter.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsNull()
    {
        var result = EdgeTypeFilter.Parse("   ");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("CALLS-TO")]
    [InlineData("Calls-To")]
    [InlineData("CALLS")]
    [InlineData("Inherits")]
    [InlineData("IMPLEMENTS")]
    [InlineData("Depends-On")]
    [InlineData("RESOLVES-TO")]
    [InlineData("COVERS")]
    [InlineData("COVERED-BY")]
    [InlineData("REFERENCES")]
    [InlineData("OVERRIDES")]
    [InlineData("CONTAINS")]
    [InlineData("ALL")]
    public void Parse_CaseInsensitive_AllMappings(string input)
    {
        // Should not throw
        var result = EdgeTypeFilter.Parse(input);
        // Just ensure it doesn't throw - all these are valid
    }

    [Fact]
    public void Parse_UnknownString_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => EdgeTypeFilter.Parse("invalid-type"));
        Assert.Contains("Unknown edge type", ex.Message);
        Assert.Contains("invalid-type", ex.Message);
    }

    [Fact]
    public void Parse_UnknownString_ErrorContainsValidValues()
    {
        var ex = Assert.Throws<ArgumentException>(() => EdgeTypeFilter.Parse("bad"));
        Assert.Contains("calls", ex.Message);
    }

    [Theory]
    [InlineData("Calls", EdgeType.Calls)]
    [InlineData("Inherits", EdgeType.Inherits)]
    [InlineData("Implements", EdgeType.Implements)]
    [InlineData("DependsOn", EdgeType.DependsOn)]
    [InlineData("ResolvesTo", EdgeType.ResolvesTo)]
    [InlineData("Covers", EdgeType.Covers)]
    [InlineData("References", EdgeType.References)]
    [InlineData("Overrides", EdgeType.Overrides)]
    [InlineData("Contains", EdgeType.Contains)]
    public void Parse_EnumName_FallbackWorks(string enumName, EdgeType expected)
    {
        var result = EdgeTypeFilter.Parse(enumName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Parse_EnumName_CaseInsensitive()
    {
        Assert.Equal(EdgeType.Calls, EdgeTypeFilter.Parse("calls"));
        Assert.Equal(EdgeType.Inherits, EdgeTypeFilter.Parse("INHERITS"));
    }

    [Fact]
    public void Apply_WithFilter_ReturnsOnlyMatchingEdges()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Inherits },
            new() { FromId = "B", ToId = "C", Type = EdgeType.Calls }
        };

        var filtered = EdgeTypeFilter.Apply(edges, EdgeType.Calls);

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, e => Assert.Equal(EdgeType.Calls, e.Type));
    }

    [Fact]
    public void Apply_WithNull_ReturnsAllEdges()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Inherits }
        };

        var filtered = EdgeTypeFilter.Apply(edges, null);

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void Apply_NoMatchingEdges_ReturnsEmpty()
    {
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var filtered = EdgeTypeFilter.Apply(edges, EdgeType.Inherits);

        Assert.Empty(filtered);
    }

    [Fact]
    public void Apply_EmptyList_ReturnsEmpty()
    {
        var filtered = EdgeTypeFilter.Apply(new List<GraphEdge>(), EdgeType.Calls);

        Assert.Empty(filtered);
    }

    [Theory]
    [InlineData("calls-to", "calls")]
    [InlineData("calls-from", "calls")]
    public void Parse_MultipleAliases_MapToSameType(string alias1, string alias2)
    {
        var result1 = EdgeTypeFilter.Parse(alias1);
        var result2 = EdgeTypeFilter.Parse(alias2);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Parse_EachAlias_ReturnsDistinctFromOtherTypes()
    {
        // Verify each alias maps to its specific type and not another
        Assert.Equal(EdgeType.Inherits, EdgeTypeFilter.Parse("inherits"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("inherits"));

        Assert.Equal(EdgeType.Implements, EdgeTypeFilter.Parse("implements"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("implements"));

        Assert.Equal(EdgeType.DependsOn, EdgeTypeFilter.Parse("depends-on"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("depends-on"));

        Assert.Equal(EdgeType.ResolvesTo, EdgeTypeFilter.Parse("resolves-to"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("resolves-to"));

        Assert.Equal(EdgeType.Covers, EdgeTypeFilter.Parse("covers"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("covers"));

        Assert.Equal(EdgeType.References, EdgeTypeFilter.Parse("references"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("references"));

        Assert.Equal(EdgeType.Overrides, EdgeTypeFilter.Parse("overrides"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("overrides"));

        Assert.Equal(EdgeType.Contains, EdgeTypeFilter.Parse("contains"));
        Assert.NotEqual(EdgeType.Calls, EdgeTypeFilter.Parse("contains"));
    }

    [Fact]
    public void Parse_CallsFrom_SpecificallyMapsToCallsType()
    {
        // If "calls-from" is mutated to "", it should fail
        var result = EdgeTypeFilter.Parse("calls-from");
        Assert.Equal(EdgeType.Calls, result);
    }

    [Fact]
    public void Parse_ErrorMessage_ContainsValidKeys()
    {
        var ex = Assert.Throws<ArgumentException>(() => EdgeTypeFilter.Parse("xyz"));
        Assert.Contains("calls-to", ex.Message);
        Assert.Contains("inherits", ex.Message);
        Assert.Contains("implements", ex.Message);
        Assert.Contains("depends-on", ex.Message);
        Assert.Contains("resolves-to", ex.Message);
        Assert.Contains("covers", ex.Message);
        Assert.Contains("references", ex.Message);
        Assert.Contains("overrides", ex.Message);
        Assert.Contains("contains", ex.Message);
    }
}
