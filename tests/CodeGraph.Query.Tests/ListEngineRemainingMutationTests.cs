using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class ListEngineRemainingMutationTests
{
    [Fact]
    public void ListAssemblies_ReturnsExactCountsAndOrdering()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Alpha.Type1"] = new() { Id = "Alpha.Type1", Name = "Type1", Kind = NodeKind.Type, AssemblyName = "Alpha" },
            ["Alpha.Type2"] = new() { Id = "Alpha.Type2", Name = "Type2", Kind = NodeKind.Type, AssemblyName = "Alpha" },
            ["Alpha.Type3"] = new() { Id = "Alpha.Type3", Name = "Type3", Kind = NodeKind.Type, AssemblyName = "Alpha" },
            ["Alpha.Method"] = new() { Id = "Alpha.Method", Name = "Method", Kind = NodeKind.Method, AssemblyName = "Alpha" },
            ["Beta.Type1"] = new() { Id = "Beta.Type1", Name = "Type1", Kind = NodeKind.Type, AssemblyName = "Beta" },
            ["Beta.Method1"] = new() { Id = "Beta.Method1", Name = "Method1", Kind = NodeKind.Method, AssemblyName = "Beta" },
            ["Beta.Method2"] = new() { Id = "Beta.Method2", Name = "Method2", Kind = NodeKind.Method, AssemblyName = "Beta" },
            ["Ignored"] = new() { Id = "Ignored", Name = "Ignored", Kind = NodeKind.Type, AssemblyName = string.Empty }
        };
        var engine = new ListEngine(nodes, []);

        var result = engine.ListAssemblies();

        Assert.Collection(result,
            assembly =>
            {
                Assert.Equal("Alpha", assembly.Name);
                Assert.Equal(3, assembly.TypeCount);
                Assert.Equal(1, assembly.MethodCount);
                Assert.Equal(4, assembly.TotalNodeCount);
            },
            assembly =>
            {
                Assert.Equal("Beta", assembly.Name);
                Assert.Equal(1, assembly.TypeCount);
                Assert.Equal(2, assembly.MethodCount);
                Assert.Equal(3, assembly.TotalNodeCount);
            });
    }

    [Fact]
    public void ListTypes_OrdersByCombinedDegree_ThenAppliesPagination()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "A", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["B"] = new() { Id = "B", Name = "B", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["C"] = new() { Id = "C", Name = "C", Kind = NodeKind.Type, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "B", ToId = "A", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.Calls }
        };
        var engine = new ListEngine(nodes, edges);

        var result = engine.ListTypes(top: 2, skip: 1);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Skip);
        Assert.Equal(2, result.Top);
        Assert.Collection(result.Types,
            type =>
            {
                Assert.Equal("B", type.Id);
                Assert.Equal(1, type.InDegree);
                Assert.Equal(1, type.OutDegree);
            },
            type =>
            {
                Assert.Equal("C", type.Id);
                Assert.Equal(1, type.InDegree);
                Assert.Equal(0, type.OutDegree);
            });
    }

    [Fact]
    public void ListTypes_CombinesAssemblyAndNameFilters()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Data.SqlOrderRepository"] = new() { Id = "Data.SqlOrderRepository", Name = "SqlOrderRepository", Kind = NodeKind.Type, AssemblyName = "MyApp.Data" },
            ["Data.OrderAudit"] = new() { Id = "Data.OrderAudit", Name = "OrderAudit", Kind = NodeKind.Type, AssemblyName = "MyApp.Data" },
            ["Services.OrderRepository"] = new() { Id = "Services.OrderRepository", Name = "OrderRepository", Kind = NodeKind.Type, AssemblyName = "MyApp.Services" }
        };
        var engine = new ListEngine(nodes, []);

        var result = engine.ListTypes(assemblyFilter: "MyApp.Data", filter: "Repository", top: 10);

        var match = Assert.Single(result.Types);
        Assert.Equal("Data.SqlOrderRepository", match.Id);
        Assert.Equal("MyApp.Data", match.Assembly);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public void ListTypes_EmptyFilters_DoNotChangeResults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = new() { Id = "A", Name = "Alpha", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["B"] = new() { Id = "B", Name = "Beta", Kind = NodeKind.Type, AssemblyName = "Asm" }
        };
        var engine = new ListEngine(nodes, []);

        var withoutFilters = engine.ListTypes(top: 10);
        var withEmptyFilters = engine.ListTypes(assemblyFilter: string.Empty, filter: string.Empty, top: 10);

        Assert.Equal(withoutFilters.TotalCount, withEmptyFilters.TotalCount);
        Assert.Equal(withoutFilters.Types.Select(type => type.Id), withEmptyFilters.Types.Select(type => type.Id));
    }

    [Fact]
    public void ListInterfaces_KeepsZeroImplementationInterfacesInSortedResults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["IMulti"] = new() { Id = "IMulti", Name = "IMulti", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["IZero"] = new() { Id = "IZero", Name = "IZero", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["Impl1"] = new() { Id = "Impl1", Name = "Impl1", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["Impl2"] = new() { Id = "Impl2", Name = "Impl2", Kind = NodeKind.Type, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Impl1", ToId = "IMulti", Type = EdgeType.Implements },
            new() { FromId = "Impl2", ToId = "IMulti", Type = EdgeType.Implements }
        };
        var engine = new ListEngine(nodes, edges);

        var result = engine.ListInterfaces();

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("IMulti", item.Id);
                Assert.Equal(2, item.ImplementationCount);
            },
            item =>
            {
                Assert.Equal("IZero", item.Id);
                Assert.Equal(0, item.ImplementationCount);
            });
    }

    [Fact]
    public void ListNamespaces_ReturnsExactCountsAndOrdering()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Type1"] = new() { Id = "A.Type1", Name = "Type1", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Ns.A" },
            ["A.Type2"] = new() { Id = "A.Type2", Name = "Type2", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Ns.A" },
            ["A.Method"] = new() { Id = "A.Method", Name = "Method", Kind = NodeKind.Method, AssemblyName = "Asm", ContainingNamespaceId = "Ns.A" },
            ["A.Property"] = new() { Id = "A.Property", Name = "Property", Kind = NodeKind.Property, AssemblyName = "Asm", ContainingNamespaceId = "Ns.A" },
            ["B.Type"] = new() { Id = "B.Type", Name = "Type", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Ns.B" },
            ["B.Method1"] = new() { Id = "B.Method1", Name = "Method1", Kind = NodeKind.Method, AssemblyName = "Asm", ContainingNamespaceId = "Ns.B" },
            ["B.Method2"] = new() { Id = "B.Method2", Name = "Method2", Kind = NodeKind.Method, AssemblyName = "Asm", ContainingNamespaceId = "Ns.B" },
            ["Ignored"] = new() { Id = "Ignored", Name = "Ignored", Kind = NodeKind.Type, AssemblyName = "Asm" }
        };
        var engine = new ListEngine(nodes, []);

        var result = engine.ListNamespaces();

        Assert.Collection(result,
            ns =>
            {
                Assert.Equal("Ns.A", ns.Name);
                Assert.Equal(2, ns.TypeCount);
                Assert.Equal(1, ns.MethodCount);
                Assert.Equal(4, ns.TotalCount);
            },
            ns =>
            {
                Assert.Equal("Ns.B", ns.Name);
                Assert.Equal(1, ns.TypeCount);
                Assert.Equal(2, ns.MethodCount);
                Assert.Equal(3, ns.TotalCount);
            });
    }
}
