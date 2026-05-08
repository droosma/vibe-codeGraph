using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SemanticSearchEngineTests
{
    private static Dictionary<string, GraphNode> BuildTestNodes()
    {
        return new Dictionary<string, GraphNode>
        {
            ["MyApp.Services.OrderService"] = new()
            {
                Id = "MyApp.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Services",
                FilePath = "src/Services/OrderService.cs",
                DocComment = "<summary>Handles order placement and tracking</summary>",
                AssemblyName = "MyApp"
            },
            ["MyApp.Services.IOrderService"] = new()
            {
                Id = "MyApp.Services.IOrderService",
                Name = "IOrderService",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Services",
                FilePath = "src/Services/IOrderService.cs",
                AssemblyName = "MyApp"
            },
            ["MyApp.Services.OrderService.PlaceOrder"] = new()
            {
                Id = "MyApp.Services.OrderService.PlaceOrder",
                Name = "PlaceOrder",
                Kind = NodeKind.Method,
                ContainingNamespaceId = "MyApp.Services",
                ContainingTypeId = "MyApp.Services.OrderService",
                FilePath = "src/Services/OrderService.cs",
                AssemblyName = "MyApp"
            },
            ["MyApp.Data.OrderRepository"] = new()
            {
                Id = "MyApp.Data.OrderRepository",
                Name = "OrderRepository",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Data",
                FilePath = "src/Data/OrderRepository.cs",
                DocComment = "<summary>Database access for orders</summary>",
                AssemblyName = "MyApp"
            },
            ["MyApp.Auth.JwtTokenHandler"] = new()
            {
                Id = "MyApp.Auth.JwtTokenHandler",
                Name = "JwtTokenHandler",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Auth",
                FilePath = "src/Auth/JwtTokenHandler.cs",
                DocComment = "<summary>Handles JWT authentication and token validation</summary>",
                AssemblyName = "MyApp"
            },
            ["MyApp.Auth.JwtTokenHandler.Validate"] = new()
            {
                Id = "MyApp.Auth.JwtTokenHandler.Validate",
                Name = "Validate",
                Kind = NodeKind.Method,
                ContainingNamespaceId = "MyApp.Auth",
                ContainingTypeId = "MyApp.Auth.JwtTokenHandler",
                FilePath = "src/Auth/JwtTokenHandler.cs",
                AssemblyName = "MyApp"
            },
            ["MyApp.Config.AppSettings"] = new()
            {
                Id = "MyApp.Config.AppSettings",
                Name = "AppSettings",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Config",
                FilePath = "src/Config/AppSettings.cs",
                DocComment = "<summary>Application configuration settings</summary>",
                AssemblyName = "MyApp"
            },
            ["MyApp.Models.Order._status"] = new()
            {
                Id = "MyApp.Models.Order._status",
                Name = "_status",
                Kind = NodeKind.Field,
                ContainingNamespaceId = "MyApp.Models",
                ContainingTypeId = "MyApp.Models.Order",
                FilePath = "src/Models/Order.cs",
                AssemblyName = "MyApp"
            },
            ["MyApp.Models.Order.Status"] = new()
            {
                Id = "MyApp.Models.Order.Status",
                Name = "Status",
                Kind = NodeKind.Property,
                ContainingNamespaceId = "MyApp.Models",
                ContainingTypeId = "MyApp.Models.Order",
                FilePath = "src/Models/Order.cs",
                AssemblyName = "MyApp"
            },
            ["MyApp.Caching.RedisCache"] = new()
            {
                Id = "MyApp.Caching.RedisCache",
                Name = "RedisCache",
                Kind = NodeKind.Type,
                ContainingNamespaceId = "MyApp.Caching",
                FilePath = "src/Caching/RedisCache.cs",
                DocComment = "<summary>Distributed caching using Redis</summary>",
                AssemblyName = "MyApp"
            },
        };
    }

    [Fact]
    public void Search_ExactNameMatch_RanksHighest()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("OrderService");

        Assert.NotEmpty(results);
        Assert.Equal("OrderService", results[0].Node.Name);
        Assert.True(results[0].Score > results.Skip(1).Max(r => r.Score),
            "Exact match should have the highest score");
    }

    [Fact]
    public void Search_SynonymMatch_FindsRelatedSymbols()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        // "auth" should find JwtTokenHandler via synonym (jwt → auth group)
        var results = engine.Search("auth");

        Assert.Contains(results, r => r.Node.Name == "JwtTokenHandler");
        Assert.Contains(results, r =>
            r.MatchReasons.Any(reason => reason.Contains("synonym", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Search_DocCommentMatching_Works()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        // "tracking" appears only in OrderService's doc comment
        var results = engine.Search("tracking");

        Assert.Contains(results, r => r.Node.Name == "OrderService");
        Assert.Contains(results, r =>
            r.MatchReasons.Any(reason => reason.Contains("doc comment", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Search_KindFilter_FiltersCorrectly()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("Order", kindFilter: NodeKind.Method);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(NodeKind.Method, r.Node.Kind));
        Assert.Contains(results, r => r.Node.Name == "PlaceOrder");
    }

    [Fact]
    public void Search_KindFilterType_ExcludesMethods()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("Order", kindFilter: NodeKind.Type);

        Assert.All(results, r => Assert.Equal(NodeKind.Type, r.Node.Kind));
        Assert.DoesNotContain(results, r => r.Node.Name == "PlaceOrder");
    }

    [Fact]
    public void Search_TopN_LimitsResults()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("MyApp", top: 3);

        Assert.True(results.Count <= 3);
    }

    [Fact]
    public void Search_DeterministicOrdering()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results1 = engine.Search("Order");
        var results2 = engine.Search("Order");

        Assert.Equal(results1.Count, results2.Count);
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i].Node.Id, results2[i].Node.Id);
            Assert.Equal(results1[i].Score, results2[i].Score);
        }
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsEmpty()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("   ");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("XYZNonexistent123");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_TypesAndMethodsBoosted_OverFieldsAndProperties()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        // "status" should match both the field _status and property Status.
        // The type/method boost should not apply to fields/properties.
        var results = engine.Search("status");
        var field = results.FirstOrDefault(r => r.Node.Kind == NodeKind.Field);
        var prop = results.FirstOrDefault(r => r.Node.Kind == NodeKind.Property);

        // Both should be found
        Assert.NotNull(field);
        Assert.NotNull(prop);

        // Neither should have type/method boost
        Assert.DoesNotContain(field.MatchReasons, r => r.Contains("type/method boost"));
        Assert.DoesNotContain(prop.MatchReasons, r => r.Contains("type/method boost"));
    }

    [Fact]
    public void Search_SynonymDb_FindsRepository()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        // "db" should find OrderRepository via synonym expansion
        var results = engine.Search("db");

        Assert.Contains(results, r => r.Node.Name == "OrderRepository");
    }

    [Fact]
    public void Search_CacheSearch_FindsRedis()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("cache");

        Assert.Contains(results, r => r.Node.Name == "RedisCache");
    }

    [Fact]
    public void Search_ConfigSearch_FindsAppSettings()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("config");

        Assert.Contains(results, r => r.Node.Name == "AppSettings");
    }

    [Fact]
    public void SearchResult_ContainsMatchReasons()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("OrderService");

        var top = results[0];
        Assert.NotEmpty(top.MatchReasons);
        Assert.Contains(top.MatchReasons, r => r.Contains("exact name match"));
    }

    [Fact]
    public void Search_ScoresArePositive()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        var results = engine.Search("Order");

        Assert.All(results, r => Assert.True(r.Score > 0, $"Score for {r.Node.Name} should be positive"));
    }

    [Fact]
    public void Search_PascalCaseQueryMatchesTokens()
    {
        var engine = new SemanticSearchEngine(BuildTestNodes());

        // "PlaceOrder" query should find the PlaceOrder method and OrderService
        var results = engine.Search("PlaceOrder");

        Assert.Contains(results, r => r.Node.Name == "PlaceOrder");
    }
}
