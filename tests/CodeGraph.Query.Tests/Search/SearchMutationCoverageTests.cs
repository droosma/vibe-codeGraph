using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SearchMutationCoverageTests
{
    [Fact]
    public void TokenizeSymbol_NameNamespaceAndDocCommentContributeUniqueTokens()
    {
        var node = new GraphNode
        {
            Id = string.Empty,
            Name = "OrderHTTP",
            Kind = NodeKind.Type,
            ContainingNamespaceId = "Only.NamespaceSource",
            FilePath = string.Empty,
            DocComment = "<summary>Alpha</summary><remarks>Beta</remarks>",
            AssemblyName = "TestAssembly"
        };

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("orderhttp", tokens);
        Assert.Contains("http", tokens);
        Assert.Contains("namespacesource", tokens);
        Assert.Contains("namespace", tokens);
        Assert.Contains("alpha", tokens);
        Assert.Contains("beta", tokens);
        Assert.DoesNotContain("alphabeta", tokens);
    }

    [Fact]
    public void TokenizeSymbol_FilePathContributesPathAndExtensionTokens()
    {
        var node = new GraphNode
        {
            Id = string.Empty,
            Name = string.Empty,
            Kind = NodeKind.Type,
            FilePath = @"src\Handlers\CheckoutFlow.cs",
            AssemblyName = "TestAssembly"
        };

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("src", tokens);
        Assert.Contains("handlers", tokens);
        Assert.Contains("checkout", tokens);
        Assert.Contains("flow", tokens);
        Assert.Contains("checkoutflow", tokens);
        Assert.Contains("cs", tokens);
    }

    [Fact]
    public void Search_PathOnlyMatch_UsesNamespacePathReasonWhenNamespaceIsEmpty()
    {
        var node = CreateNode(
            id: "MyApp.ConfigLoader",
            name: "ConfigLoader",
            kind: NodeKind.Property,
            filePath: @"src\Controllers\ConfigLoader.cs");
        var engine = new SemanticSearchEngine(new Dictionary<string, GraphNode> { [node.Id] = node });

        var result = Assert.Single(engine.Search("controllers"));

        Assert.Equal(new[] { "token 'controllers' in namespace/path" }, result.MatchReasons);
    }

    [Fact]
    public void Search_DocCommentWithAdjacentXmlElements_SplitsWordsCorrectly()
    {
        var node = CreateNode(
            id: "MyApp.Tracker",
            name: "Tracker",
            kind: NodeKind.Type,
            docComment: "<summary>Alpha</summary><remarks>Beta</remarks>");
        var engine = new SemanticSearchEngine(new Dictionary<string, GraphNode> { [node.Id] = node });

        var alpha = Assert.Single(engine.Search("alpha"));
        var beta = Assert.Single(engine.Search("beta"));

        Assert.Contains("token 'alpha' in doc comment", alpha.MatchReasons);
        Assert.Contains("token 'beta' in doc comment", beta.MatchReasons);
    }

    private static GraphNode CreateNode(
        string id,
        string name,
        NodeKind kind,
        string? containingNamespaceId = null,
        string? filePath = null,
        string? docComment = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = kind,
            ContainingNamespaceId = containingNamespaceId,
            FilePath = filePath ?? string.Empty,
            DocComment = docComment,
            AssemblyName = "TestAssembly"
        };
    }
}
