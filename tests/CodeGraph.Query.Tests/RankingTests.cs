using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class RankingTests
{
    [Fact]
    public void DirectNeighbors_RankedBeforeTransitive()
    {
        var directNode = new GraphNode { Id = "Direct", Name = "Direct", Kind = NodeKind.Method };
        var transitiveNode = new GraphNode { Id = "Transitive", Name = "Transitive", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "Direct" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { transitiveNode, directNode },
            directIds, externalIds, null);

        Assert.Equal("Direct", ranked[0].Id);
        Assert.Equal("Transitive", ranked[1].Id);
    }

    [Fact]
    public void InternalNodes_RankedBeforeExternal()
    {
        var internalNode = new GraphNode { Id = "Internal", Name = "Internal", Kind = NodeKind.Method };
        var externalNode = new GraphNode { Id = "External", Name = "External", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "Internal", "External" };
        var externalIds = new HashSet<string> { "External" };

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { externalNode, internalNode },
            directIds, externalIds, null);

        Assert.Equal("Internal", ranked[0].Id);
        Assert.Equal("External", ranked[1].Id);
    }

    [Fact]
    public void Methods_RankedBeforeTypes()
    {
        var method = new GraphNode { Id = "A.Method", Name = "Method", Kind = NodeKind.Method };
        var type = new GraphNode { Id = "A.Type", Name = "Type", Kind = NodeKind.Type };

        var directIds = new HashSet<string> { "A.Method", "A.Type" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { type, method },
            directIds, externalIds, null);

        Assert.Equal("A.Method", ranked[0].Id);
        Assert.Equal("A.Type", ranked[1].Id);
    }

    [Fact]
    public void Types_RankedBeforeNamespaces()
    {
        var type = new GraphNode { Id = "A.Type", Name = "Type", Kind = NodeKind.Type };
        var ns = new GraphNode { Id = "A.NS", Name = "NS", Kind = NodeKind.Namespace };

        var directIds = new HashSet<string> { "A.Type", "A.NS" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { ns, type },
            directIds, externalIds, null);

        Assert.Equal("A.Type", ranked[0].Id);
        Assert.Equal("A.NS", ranked[1].Id);
    }

    [Fact]
    public void DocumentedNodes_RankedBeforeUndocumented()
    {
        var documented = new GraphNode { Id = "A.Doc", Name = "Doc", Kind = NodeKind.Method, DocComment = "Has docs" };
        var undocumented = new GraphNode { Id = "A.NoDoc", Name = "NoDoc", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "A.Doc", "A.NoDoc" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { undocumented, documented },
            directIds, externalIds, null);

        Assert.Equal("A.Doc", ranked[0].Id);
        Assert.Equal("A.NoDoc", ranked[1].Id);
    }

    [Fact]
    public void SameProject_RankedBeforeCrossProject()
    {
        var sameProject = new GraphNode { Id = "MyApp.Services.Foo", Name = "Foo", Kind = NodeKind.Method };
        var crossProject = new GraphNode { Id = "OtherApp.Services.Bar", Name = "Bar", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "MyApp.Services.Foo", "OtherApp.Services.Bar" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { crossProject, sameProject },
            directIds, externalIds, "MyApp.Services");

        Assert.Equal("MyApp.Services.Foo", ranked[0].Id);
        Assert.Equal("OtherApp.Services.Bar", ranked[1].Id);
    }

    [Fact]
    public void NullTargetProject_AllConsideredSameProject()
    {
        var a = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "A", "B" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { b, a },
            directIds, externalIds, null);

        // With null project, should sort by Id as tiebreaker
        Assert.Equal("A", ranked[0].Id);
        Assert.Equal("B", ranked[1].Id);
    }

    [Fact]
    public void Constructors_RankedSameAsMethods()
    {
        var method = new GraphNode { Id = "A.Method", Name = "Method", Kind = NodeKind.Method };
        var ctor = new GraphNode { Id = "A.Ctor", Name = "Ctor", Kind = NodeKind.Constructor };

        var directIds = new HashSet<string> { "A.Method", "A.Ctor" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { method, ctor },
            directIds, externalIds, null);

        // Both have same priority, tiebroken by Id
        Assert.Equal("A.Ctor", ranked[0].Id);
        Assert.Equal("A.Method", ranked[1].Id);
    }

    [Fact]
    public void Properties_RankedBeforeTypes()
    {
        var prop = new GraphNode { Id = "A.Prop", Name = "Prop", Kind = NodeKind.Property };
        var type = new GraphNode { Id = "A.Type", Name = "Type", Kind = NodeKind.Type };

        var directIds = new HashSet<string> { "A.Prop", "A.Type" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { type, prop },
            directIds, externalIds, null);

        Assert.Equal("A.Prop", ranked[0].Id);
        Assert.Equal("A.Type", ranked[1].Id);
    }

    [Fact]
    public void Fields_RankedBeforeTypes()
    {
        var field = new GraphNode { Id = "A.Field", Name = "Field", Kind = NodeKind.Field };
        var type = new GraphNode { Id = "A.Type", Name = "Type", Kind = NodeKind.Type };

        var directIds = new HashSet<string> { "A.Field", "A.Type" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { type, field },
            directIds, externalIds, null);

        Assert.Equal("A.Field", ranked[0].Id);
        Assert.Equal("A.Type", ranked[1].Id);
    }

    [Fact]
    public void Events_RankedBeforeTypes()
    {
        var evt = new GraphNode { Id = "A.Event", Name = "Event", Kind = NodeKind.Event };
        var type = new GraphNode { Id = "A.Type", Name = "Type", Kind = NodeKind.Type };

        var directIds = new HashSet<string> { "A.Event", "A.Type" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { type, evt },
            directIds, externalIds, null);

        Assert.Equal("A.Event", ranked[0].Id);
        Assert.Equal("A.Type", ranked[1].Id);
    }

    [Fact]
    public void Tiebreaker_SortsByIdAlphabetically()
    {
        var a = new GraphNode { Id = "Zebra", Name = "Zebra", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "Apple", Name = "Apple", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "Zebra", "Apple" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { a, b },
            directIds, externalIds, null);

        Assert.Equal("Apple", ranked[0].Id);
        Assert.Equal("Zebra", ranked[1].Id);
    }

    [Fact]
    public void ComplexRanking_DirectInternalMethod_First()
    {
        var directInternalMethod = new GraphNode
        {
            Id = "MyApp.A", Name = "A", Kind = NodeKind.Method, DocComment = "Documented"
        };
        var transitiveExternalType = new GraphNode
        {
            Id = "External.B", Name = "B", Kind = NodeKind.Type
        };
        var directExternalMethod = new GraphNode
        {
            Id = "External.C", Name = "C", Kind = NodeKind.Method
        };

        var directIds = new HashSet<string> { "MyApp.A", "External.C" };
        var externalIds = new HashSet<string> { "External.B", "External.C" };

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { transitiveExternalType, directExternalMethod, directInternalMethod },
            directIds, externalIds, "MyApp");

        Assert.Equal("MyApp.A", ranked[0].Id);
    }

    [Fact]
    public void DirectNeighbor_AlwaysBeforeTransitive_RegardlessOfOtherFactors()
    {
        // Direct transitive with worse other properties should still beat transitive with better ones
        var direct = new GraphNode { Id = "D", Name = "D", Kind = NodeKind.Namespace }; // worse kind
        var transitive = new GraphNode { Id = "T", Name = "T", Kind = NodeKind.Method, DocComment = "Has docs" }; // better kind + docs

        var directIds = new HashSet<string> { "D" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { transitive, direct },
            directIds, externalIds, null);

        Assert.Equal("D", ranked[0].Id);
        Assert.Equal("T", ranked[1].Id);
    }

    [Fact]
    public void SameProject_BeforeCrossProject_WhenBothDirect()
    {
        var sameProj = new GraphNode { Id = "Proj.X", Name = "X", Kind = NodeKind.Method };
        var crossProj = new GraphNode { Id = "Other.Y", Name = "Y", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "Proj.X", "Other.Y" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { crossProj, sameProj },
            directIds, externalIds, "Proj");

        Assert.Equal("Proj.X", ranked[0].Id);
        Assert.Equal("Other.Y", ranked[1].Id);
    }

    [Fact]
    public void Documented_BeforeUndocumented_WhenAllElseEqual()
    {
        // Same kind, same project, same direct, same internal
        var doc = new GraphNode { Id = "A.D", Name = "D", Kind = NodeKind.Method, DocComment = "doc" };
        var noDoc = new GraphNode { Id = "A.N", Name = "N", Kind = NodeKind.Method, DocComment = null };

        var directIds = new HashSet<string> { "A.D", "A.N" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { noDoc, doc },
            directIds, externalIds, "A");

        Assert.Equal("A.D", ranked[0].Id);
        Assert.Equal("A.N", ranked[1].Id);
    }

    [Fact]
    public void NullTargetProject_DoesNotPenalizeAnyNode()
    {
        // With null target project, IsSameProject returns true for all, 
        // so all nodes should be treated equally on this criterion
        var a = new GraphNode { Id = "Proj1.A", Name = "A", Kind = NodeKind.Method };
        var b = new GraphNode { Id = "Proj2.B", Name = "B", Kind = NodeKind.Method };

        var directIds = new HashSet<string> { "Proj1.A", "Proj2.B" };
        var externalIds = new HashSet<string>();

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { b, a },
            directIds, externalIds, null);

        // Should be sorted by Id since everything else is equal
        Assert.Equal("Proj1.A", ranked[0].Id);
        Assert.Equal("Proj2.B", ranked[1].Id);
    }
}
