using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class ListEngineStrykerMutationTests
{
    [Fact]
    public void ListInterfaces_ExcludesNonInterfacesThatOnlyLookSimilar()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["IOrderService"] = new() { Id = "IOrderService", Name = "IOrderService", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["XMLParser"] = new() { Id = "XMLParser", Name = "XMLParser", Kind = NodeKind.Type, AssemblyName = "Asm" },
            ["IBackgroundJob.Run"] = new() { Id = "IBackgroundJob.Run", Name = "IBackgroundJob", Kind = NodeKind.Method, AssemblyName = "Asm" }
        };
        var engine = new ListEngine(nodes, []);

        var result = engine.ListInterfaces();

        var match = Assert.Single(result);
        Assert.Equal("IOrderService", match.Name);
    }

    [Fact]
    public void ListNamespaces_WhenTypeCountsTie_PreservesEncounterOrder()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Type"] = new() { Id = "A.Type", Name = "Type", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Ns.A" },
            ["B.Type"] = new() { Id = "B.Type", Name = "Type", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Ns.B" }
        };
        var engine = new ListEngine(nodes, []);

        var result = engine.ListNamespaces();

        Assert.Collection(result,
            first => Assert.Equal("Ns.A", first.Name),
            second => Assert.Equal("Ns.B", second.Name));
    }

    [Fact]
    public void ListInfoRecords_DefaultStringProperties_AreEmpty()
    {
        Assert.Equal(string.Empty, new AssemblyInfo().Name);
        Assert.Equal(string.Empty, new TypeInfo().Id);
        Assert.Equal(string.Empty, new TypeInfo().Name);
        Assert.Equal(string.Empty, new TypeInfo().Assembly);
        Assert.Equal(string.Empty, new InterfaceInfo().Id);
        Assert.Equal(string.Empty, new InterfaceInfo().Name);
        Assert.Equal(string.Empty, new InterfaceInfo().Assembly);
        Assert.Equal(string.Empty, new NamespaceInfo().Name);
    }
}
