using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class RankingStrategyRemainingMutationTests
{
    [Fact]
    public void Rank_EmptyDocComment_IsStillTreatedAsDocumented()
    {
        var documented = new GraphNode { Id = "A.Doc", Name = "Doc", Kind = NodeKind.Method, DocComment = string.Empty };
        var undocumented = new GraphNode { Id = "A.Undoc", Name = "Undoc", Kind = NodeKind.Method, DocComment = null };

        var ranked = RankingStrategy.Rank(
            [undocumented, documented],
            new HashSet<string> { documented.Id, undocumented.Id },
            [],
            "A");

        Assert.Equal("A.Doc", ranked[0].Id);
        Assert.Equal("A.Undoc", ranked[1].Id);
    }

    [Fact]
    public void Rank_ProjectMatch_IsCaseInsensitivePrefix()
    {
        var sameProject = new GraphNode { Id = "MYAPP.Services.Handler", Name = "Handler", Kind = NodeKind.Method };
        var otherProject = new GraphNode { Id = "Other.Services.Handler", Name = "Handler", Kind = NodeKind.Method };

        var ranked = RankingStrategy.Rank(
            [otherProject, sameProject],
            new HashSet<string> { sameProject.Id, otherProject.Id },
            [],
            "myapp.services");

        Assert.Equal("MYAPP.Services.Handler", ranked[0].Id);
        Assert.Equal("Other.Services.Handler", ranked[1].Id);
    }

    [Fact]
    public void Rank_InvalidNodeKind_SortsAfterKnownKinds()
    {
        var known = new GraphNode { Id = "Known", Name = "Known", Kind = NodeKind.Namespace };
        var invalid = new GraphNode { Id = "Invalid", Name = "Invalid", Kind = (NodeKind)999 };

        var ranked = RankingStrategy.Rank(
            [invalid, known],
            new HashSet<string> { known.Id, invalid.Id },
            [],
            null);

        Assert.Equal("Known", ranked[0].Id);
        Assert.Equal("Invalid", ranked[1].Id);
    }
}
