using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class NamespaceFilterTests
{
    [Fact]
    public void Apply_NullPattern_ReturnsAllNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "NS1" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method, ContainingNamespaceId = "NS2" }
        };

        var result = NamespaceFilter.Apply(nodes, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_EmptyPattern_ReturnsAllNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "NS1" }
        };

        var result = NamespaceFilter.Apply(nodes, "");

        Assert.Single(result);
    }

    [Fact]
    public void Apply_WhitespacePattern_ReturnsAllNodes()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "NS1" }
        };

        var result = NamespaceFilter.Apply(nodes, "   ");

        Assert.Single(result);
    }

    [Fact]
    public void Apply_MatchesByContainingNamespaceId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Data" }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services");

        Assert.Single(result);
        Assert.True(result.ContainsKey("A"));
    }

    [Fact]
    public void Apply_MatchesByNamespaceNodeId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services"] = new() { Id = "MyApp.Services", Name = "Services", Kind = NodeKind.Namespace },
            ["MyApp.Data"] = new() { Id = "MyApp.Data", Name = "Data", Kind = NodeKind.Namespace }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services");

        Assert.Single(result);
        Assert.True(result.ContainsKey("MyApp.Services"));
    }

    [Fact]
    public void Apply_MatchesByNodeId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.Foo"] = new() { Id = "MyApp.Services.Foo", Name = "Foo", Kind = NodeKind.Method },
            ["Other.Bar"] = new() { Id = "Other.Bar", Name = "Bar", Kind = NodeKind.Method }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services.Foo");

        Assert.Single(result);
        Assert.True(result.ContainsKey("MyApp.Services.Foo"));
    }

    [Fact]
    public void Apply_WildcardPattern()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Data" },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Method, ContainingNamespaceId = "Other" }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.*");

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("A"));
        Assert.True(result.ContainsKey("B"));
    }

    [Fact]
    public void Apply_CaseInsensitive()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services" }
        };

        var result = NamespaceFilter.Apply(nodes, "myapp.services");

        Assert.Single(result);
    }

    [Fact]
    public void Apply_NamespaceKind_MatchesById()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services"] = new() { Id = "MyApp.Services", Name = "Services", Kind = NodeKind.Namespace, ContainingNamespaceId = null },
            ["Other.NS"] = new() { Id = "Other.NS", Name = "NS", Kind = NodeKind.Namespace, ContainingNamespaceId = null }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services");

        Assert.Single(result);
        Assert.True(result.ContainsKey("MyApp.Services"));
    }

    [Fact]
    public void Apply_NoMatch_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Method, ContainingNamespaceId = "MyApp.Services" }
        };

        var result = NamespaceFilter.Apply(nodes, "NonExistent");

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_ContainingNamespaceId_IsNull_FallsBackToIdMatch()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.Foo"] = new() { Id = "MyApp.Services.Foo", Name = "Foo", Kind = NodeKind.Method, ContainingNamespaceId = null }
        };

        var result = NamespaceFilter.Apply(nodes, "MyApp.Services.Foo");

        Assert.Single(result);
    }
}
