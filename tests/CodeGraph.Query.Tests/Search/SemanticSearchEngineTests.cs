using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SemanticSearchEngineTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]{}()-_/\\")]
    [InlineData(".,;:<>")]
    public void Search_EmptyOrSeparatorOnlyQuery_ReturnsEmpty(string query)
    {
        var engine = CreateEngine(CreateNode("Alpha", "Alpha", NodeKind.Type));

        var results = engine.Search(query);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_ExactNameMatch_ReturnsExpectedScoreAndReasons()
    {
        var engine = CreateEngine(CreateNode("Status", "Status", NodeKind.Property));

        var result = Assert.Single(engine.Search("Status"));

        Assert.Equal(15.470, result.Score, 3);
        Assert.Equal(new[] { "exact name match", "token 'status' in name" }, result.MatchReasons);
    }

    [Fact]
    public void Search_DocCommentMatch_ReturnsExpectedScoreAndReasons()
    {
        var engine = CreateEngine(CreateNode(
            "Tracker",
            "Tracker",
            NodeKind.Type,
            docComment: "<summary>Handles tracking.</summary>"));

        var result = Assert.Single(engine.Search("tracking"));

        Assert.Equal(4.465, result.Score, 3);
        Assert.Equal(new[] { "token 'tracking' in doc comment", "type/method boost" }, result.MatchReasons);
    }

    [Fact]
    public void Search_SynonymMatch_ReturnsExpectedScoreAndReason()
    {
        var engine = CreateEngine(CreateNode("JwtStore", "JwtStore", NodeKind.Property));

        var result = Assert.Single(engine.Search("auth"));

        Assert.Equal(2.460, result.Score, 3);
        Assert.Equal(new[] { "synonym 'jwt' of 'auth'" }, result.MatchReasons);
    }

    [Fact]
    public void Search_NamespacePathMatch_ReturnsExpectedScoreAndReason()
    {
        var engine = CreateEngine(CreateNode(
            "Alpha",
            "Alpha",
            NodeKind.Property,
            containingNamespaceId: "MyApp.Controllers",
            filePath: @"src\Controllers\Alpha.cs"));

        var result = Assert.Single(engine.Search("controllers"));

        Assert.Equal(1.475, result.Score, 3);
        Assert.Equal(new[] { "token 'controllers' in namespace/path" }, result.MatchReasons);
    }

    [Fact]
    public void Search_MethodBoost_RanksMethodAbovePropertyWhenBaseMatchIsEqual()
    {
        var engine = CreateEngine(
            CreateNode("Service.Run", "Run", NodeKind.Method),
            CreateNode("Config.Run", "Run", NodeKind.Property));

        var results = engine.Search("Run");

        Assert.Collection(
            results,
            first =>
            {
                Assert.Equal(NodeKind.Method, first.Node.Kind);
                Assert.Equal(16.485, first.Score, 3);
                Assert.Equal(new[] { "exact name match", "token 'run' in name", "type/method boost" }, first.MatchReasons);
            },
            second =>
            {
                Assert.Equal(NodeKind.Property, second.Node.Kind);
                Assert.Equal(15.485, second.Score, 3);
                Assert.Equal(new[] { "exact name match", "token 'run' in name" }, second.MatchReasons);
            });
    }

    [Fact]
    public void Search_KindFilter_ReturnsOnlyRequestedKind()
    {
        var engine = CreateEngine(
            CreateNode("Worker.Run", "Run", NodeKind.Method),
            CreateNode("Worker", "Run", NodeKind.Type),
            CreateNode("Worker.RunProperty", "Run", NodeKind.Property));

        var results = engine.Search("Run", kindFilter: NodeKind.Method);

        var result = Assert.Single(results);
        Assert.Equal(NodeKind.Method, result.Node.Kind);
    }

    [Fact]
    public void Search_TopLimit_ReturnsHighestScoredResults()
    {
        var engine = CreateEngine(
            CreateNode("One", "One", NodeKind.Property, containingNamespaceId: "Shared"),
            CreateNode("Four", "Four", NodeKind.Property, containingNamespaceId: "Shared"),
            CreateNode("Seven", "Seven", NodeKind.Property, containingNamespaceId: "Shared"));

        var results = engine.Search("shared", top: 2);

        Assert.Collection(
            results,
            first => Assert.Equal("One", first.Node.Name),
            second => Assert.Equal("Four", second.Node.Name));
    }

    [Fact]
    public void Search_LongNamesWithEqualScores_SortByShorterNameFirst()
    {
        var shorterName = new string('A', 101);
        var longerName = new string('B', 150);
        var engine = CreateEngine(
            CreateNode("Shared.Short", shorterName, NodeKind.Property, containingNamespaceId: "Shared"),
            CreateNode("Shared.Long", longerName, NodeKind.Property, containingNamespaceId: "Shared"));

        var results = engine.Search("shared");

        Assert.Collection(
            results,
            first => Assert.Equal(shorterName, first.Node.Name),
            second => Assert.Equal(longerName, second.Node.Name));
    }

    [Fact]
    public void Search_EqualScoreAndLength_SortsAlphabetically()
    {
        var alphaName = new string('A', 100);
        var bravoName = new string('B', 100);
        var engine = CreateEngine(
            CreateNode("Shared.Bravo", bravoName, NodeKind.Property, containingNamespaceId: "Shared"),
            CreateNode("Shared.Alpha", alphaName, NodeKind.Property, containingNamespaceId: "Shared"));

        var results = engine.Search("shared");

        Assert.Collection(
            results,
            first => Assert.Equal(alphaName, first.Node.Name),
            second => Assert.Equal(bravoName, second.Node.Name));
    }

    [Fact]
    public void Search_XmlOnlyDocComment_DoesNotProduceSearchableTokens()
    {
        var engine = CreateEngine(CreateNode(
            "XmlOnly",
            "XmlOnly",
            NodeKind.Type,
            docComment: "<summary><see cref=\"Tracking\"/></summary>"));

        var results = engine.Search("tracking");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_UnknownQuery_ReturnsEmpty()
    {
        var engine = CreateEngine(CreateNode("Alpha", "Alpha", NodeKind.Type));

        var results = engine.Search("xyznonexistent123");

        Assert.Empty(results);
    }

    private static SemanticSearchEngine CreateEngine(params GraphNode[] nodes)
    {
        return new SemanticSearchEngine(nodes.ToDictionary(node => node.Id, StringComparer.Ordinal));
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