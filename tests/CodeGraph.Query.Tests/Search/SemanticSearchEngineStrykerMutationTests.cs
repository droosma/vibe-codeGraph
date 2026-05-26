using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SemanticSearchEngineStrykerMutationTests
{
    [Fact]
    public void Search_DocCommentTagBoundaries_PreserveSeparatedWords()
    {
        var node = new GraphNode
        {
            Id = "Tracker",
            Name = "Tracker",
            Kind = NodeKind.Type,
            DocComment = "<summary>Tracks</summary><remarks>orders</remarks>",
            AssemblyName = "TestAssembly"
        };
        var engine = new SemanticSearchEngine(new Dictionary<string, GraphNode> { [node.Id] = node });

        var result = Assert.Single(engine.Search("orders"));

        Assert.Contains("token 'orders' in doc comment", result.MatchReasons);
    }
}
