using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SearchTokenizerTests
{
    private static GraphNode MakeNode(
        string name,
        string? id = null,
        string? namespacee = null,
        string? filePath = null,
        string? docComment = null) =>
        new()
        {
            Id = id ?? $"Ns.{name}",
            Name = name,
            Kind = NodeKind.Type,
            ContainingNamespaceId = namespacee,
            FilePath = filePath ?? $"src/{name}.cs",
            DocComment = docComment,
            AssemblyName = "TestAssembly"
        };

    // --- PascalCase splitting ---

    [Fact]
    public void TokenizeQuery_PascalCase_SplitsIntoWords()
    {
        var tokens = SearchTokenizer.TokenizeQuery("OrderService");

        Assert.Contains("order", tokens);
        Assert.Contains("service", tokens);
        Assert.Contains("orderservice", tokens);
    }

    [Fact]
    public void TokenizeQuery_CamelCase_SplitsIntoWords()
    {
        var tokens = SearchTokenizer.TokenizeQuery("placeOrder");

        Assert.Contains("place", tokens);
        Assert.Contains("order", tokens);
    }

    [Fact]
    public void TokenizeSymbol_PascalCaseName_SplitsCorrectly()
    {
        var node = MakeNode("PaymentProcessor");
        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("payment", tokens);
        Assert.Contains("processor", tokens);
        Assert.Contains("paymentprocessor", tokens);
    }

    // --- Dot/slash splitting ---

    [Fact]
    public void TokenizeQuery_DotSeparated_SplitsOnDots()
    {
        var tokens = SearchTokenizer.TokenizeQuery("MyApp.Payments");

        Assert.Contains("myapp", tokens);
        Assert.Contains("payments", tokens);
    }

    [Fact]
    public void TokenizeQuery_SlashSeparated_SplitsOnSlashes()
    {
        var tokens = SearchTokenizer.TokenizeQuery("src/Services/OrderService.cs");

        Assert.Contains("src", tokens);
        Assert.Contains("services", tokens);
        Assert.Contains("order", tokens);
        Assert.Contains("service", tokens);
    }

    [Fact]
    public void TokenizeSymbol_NamespaceId_ExtractsTokens()
    {
        var node = MakeNode("Foo", namespacee: "MyApp.Services.Ordering");
        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("myapp", tokens);
        Assert.Contains("services", tokens);
        Assert.Contains("ordering", tokens);
    }

    // --- Doc comment word extraction ---

    [Fact]
    public void TokenizeSymbol_DocComment_ExtractsWords()
    {
        var node = MakeNode("Foo", docComment: "<summary>Processes payment transactions</summary>");
        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("processes", tokens);
        Assert.Contains("payment", tokens);
        Assert.Contains("transactions", tokens);
    }

    [Fact]
    public void TokenizeSymbol_DocComment_StripsXmlTags()
    {
        var node = MakeNode("Foo", docComment: "<summary>The <see cref=\"Order\"/> handler</summary>");
        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("handler", tokens);
        Assert.DoesNotContain("summary", tokens);
        Assert.DoesNotContain("<summary>", tokens);
    }

    // --- Edge cases ---

    [Fact]
    public void TokenizeQuery_Acronym_HTTP_Preserved()
    {
        var tokens = SearchTokenizer.TokenizeQuery("HTTP");

        Assert.Contains("http", tokens);
    }

    [Fact]
    public void TokenizeQuery_AcronymFollowedByWord_SplitsCorrectly()
    {
        var tokens = SearchTokenizer.TokenizeQuery("HTTPClient");

        Assert.Contains("http", tokens);
        Assert.Contains("client", tokens);
    }

    [Fact]
    public void TokenizeQuery_SingleWord_ReturnsSelf()
    {
        var tokens = SearchTokenizer.TokenizeQuery("Order");

        Assert.Contains("order", tokens);
    }

    [Fact]
    public void TokenizeQuery_EmptyString_ReturnsEmpty()
    {
        var tokens = SearchTokenizer.TokenizeQuery("");

        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeQuery_Whitespace_ReturnsEmpty()
    {
        var tokens = SearchTokenizer.TokenizeQuery("   ");

        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeQuery_AllLowercase_ReturnsSelf()
    {
        var tokens = SearchTokenizer.TokenizeQuery("order");

        Assert.Contains("order", tokens);
    }

    [Fact]
    public void TokenizeSymbol_NullDocComment_DoesNotThrow()
    {
        var node = MakeNode("Foo", docComment: null);
        var tokens = SearchTokenizer.TokenizeSymbol(node);

        // Should still have tokens from name, id, path
        Assert.NotEmpty(tokens);
        Assert.Contains("foo", tokens);
    }

    [Fact]
    public void TokenizeQuery_NumbersInName_SplitsAtBoundary()
    {
        var tokens = SearchTokenizer.TokenizeQuery("getItem2");

        Assert.Contains("get", tokens);
        Assert.Contains("item", tokens);
        Assert.Contains("2", tokens);
    }
}
