using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SearchTokenizerTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("_")]
    [InlineData("-")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData(":")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("[")]
    [InlineData("]")]
    public void TokenizeQuery_Separator_SplitsIntoExpectedTokens(string separator)
    {
        var tokens = SearchTokenizer.TokenizeQuery($"Order{separator}Service");

        AssertTokensEqual(["order", "service"], tokens);
    }

    [Fact]
    public void TokenizeQuery_PascalCaseDigitsAndSeparators_ReturnsExpectedTokens()
    {
        var tokens = SearchTokenizer.TokenizeQuery("HTTPClient2_OrderService");

        AssertTokensEqual(["2", "client", "http", "httpclient2", "order", "orderservice", "service"], tokens);
    }

    [Fact]
    public void TokenizeQuery_SingleCharacterAndDigitBoundary_ReturnsExpectedTokens()
    {
        var tokens = SearchTokenizer.TokenizeQuery("A B2");

        AssertTokensEqual(["2", "a", "b", "b2"], tokens);
    }

    [Fact]
    public void TokenizeQuery_OnlySeparators_ReturnsEmpty()
    {
        var tokens = SearchTokenizer.TokenizeQuery(" ._/\\-,:;()<>[]{} ");

        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeSymbol_AllSearchableFields_ReturnsExpectedTokens()
    {
        var node = CreateNode(
            id: "MyApp.Security.JwtAuth2.ValidateToken",
            name: "JwtAuth2",
            containingNamespaceId: "MyApp.Security.Auth",
            filePath: @"src\Security\Auth\JwtAuth2.cs",
            docComment: "<summary>Validates API tokens in DB.</summary>");

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        AssertTokensEqual(
            [
                "2",
                "api",
                "app",
                "auth",
                "cs",
                "db",
                "in",
                "jwtauth2",
                "jwt",
                "my",
                "myapp",
                "security",
                "src",
                "token",
                "tokens",
                "validate",
                "validates",
                "validatetoken"
            ],
            tokens);
    }

    [Fact]
    public void TokenizeSymbol_DocCommentNoiseWords_AreExcluded()
    {
        var node = new GraphNode
        {
            Id = string.Empty,
            Name = string.Empty,
            Kind = NodeKind.Type,
            FilePath = string.Empty,
            DocComment = "<summary>A b cd e</summary>",
            AssemblyName = "TestAssembly"
        };

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        AssertTokensEqual(["cd"], tokens);
    }

    [Fact]
    public void TokenizeSymbol_EmptyFields_ReturnsEmpty()
    {
        var node = new GraphNode
        {
            Id = string.Empty,
            Name = string.Empty,
            Kind = NodeKind.Type,
            FilePath = string.Empty,
            AssemblyName = "TestAssembly"
        };

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Empty(tokens);
    }

    private static void AssertTokensEqual(IEnumerable<string> expected, IReadOnlyList<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(token => token, StringComparer.Ordinal),
            actual.OrderBy(token => token, StringComparer.Ordinal));
    }

    private static GraphNode CreateNode(
        string id,
        string name,
        string? containingNamespaceId = null,
        string? filePath = null,
        string? docComment = null)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Type,
            ContainingNamespaceId = containingNamespaceId,
            FilePath = filePath ?? string.Empty,
            DocComment = docComment,
            AssemblyName = "TestAssembly"
        };
    }
}