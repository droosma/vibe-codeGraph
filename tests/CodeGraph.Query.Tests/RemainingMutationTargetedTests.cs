using System.Reflection;
using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;
using CodeGraph.Query.Report;
using CodeGraph.Query.Search;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests;

public class SearchAndSemanticMutationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddPascalCaseTokens_NullOrEmptyValue_LeavesTokenSetUnchanged(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal) { "seed" };

        InvokePrivateStatic(typeof(SearchTokenizer), "AddPascalCaseTokens", tokens, value);

        Assert.Equal(["seed"], tokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddPathTokens_NullOrEmptyPath_LeavesTokenSetUnchanged(string? path)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal) { "seed" };

        InvokePrivateStatic(typeof(SearchTokenizer), "AddPathTokens", tokens, path);

        Assert.Equal(["seed"], tokens);
    }

    [Fact]
    public void BuildSynonymSets_TokenWithoutSynonyms_ExcludesEmptyExpansionEntry()
    {
        var synonymSets = InvokePrivateStatic<Dictionary<string, HashSet<string>>>(
            typeof(SemanticSearchEngine),
            "BuildSynonymSets",
            new List<string> { "zzznosynonym" });

        Assert.Empty(synonymSets);
    }

    [Fact]
    public void Search_TopGreaterThanResultCount_ReturnsAllMatchingResults()
    {
        var alpha = CreateNode("Alpha", "Alpha", NodeKind.Type);
        var engine = new SemanticSearchEngine(new Dictionary<string, GraphNode> { [alpha.Id] = alpha });

        var results = engine.Search("Alpha", top: 5);

        var result = Assert.Single(results);
        Assert.Equal(alpha.Id, result.Node.Id);
    }

    [Fact]
    public void TryFindSynonymMatch_MissingToken_ReturnsFalseAndLeavesSynonymEmpty()
    {
        var parameters = new object?[]
        {
            "auth",
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            new List<string> { "orders" },
            null
        };

        var method = GetPrivateStaticMethod(typeof(SemanticSearchEngine), "TryFindSynonymMatch");
        var matched = Assert.IsType<bool>(method.Invoke(null, parameters));

        Assert.False(matched);
        Assert.Equal(string.Empty, Assert.IsType<string>(parameters[3]));
    }

    private static GraphNode CreateNode(string id, string name, NodeKind kind)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = kind,
            AssemblyName = "TestAssembly"
        };
    }

    private static void InvokePrivateStatic(Type type, string methodName, params object?[] parameters)
    {
        var method = GetPrivateStaticMethod(type, methodName);
        _ = method.Invoke(null, parameters);
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] parameters)
    {
        var method = GetPrivateStaticMethod(type, methodName);
        return Assert.IsType<T>(method.Invoke(null, parameters));
    }

    private static MethodInfo GetPrivateStaticMethod(Type type, string methodName)
    {
        return type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find {type.FullName}.{methodName}.");
    }
}

public class FilterAndRankingMutationTests
{
    [Fact]
    public void Parse_LowercaseEnumNameWithoutAlias_UsesCaseInsensitiveEnumFallback()
    {
        var result = EdgeTypeFilter.Parse("dependson");

        Assert.Equal(EdgeType.DependsOn, result);
    }

    [Fact]
    public void Parse_UnknownKind_ListsCallsAliasSeparatelyInErrorMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => EdgeTypeFilter.Parse("bad-edge"));

        Assert.StartsWith("Unknown edge type 'bad-edge'. Valid values: ", ex.Message);
        Assert.Contains("calls-to, calls, calls-from", ex.Message);
    }

    [Fact]
    public void Rank_DocumentedNodeWithLaterId_SortsBeforeUndocumentedNode()
    {
        var documented = new GraphNode { Id = "Z.Documented", Name = "Documented", Kind = NodeKind.Method, DocComment = "docs" };
        var undocumented = new GraphNode { Id = "A.Undocumented", Name = "Undocumented", Kind = NodeKind.Method };

        var ranked = RankingStrategy.Rank(
            new List<GraphNode> { undocumented, documented },
            new HashSet<string> { documented.Id, undocumented.Id },
            new HashSet<string>(),
            "MyApp");

        Assert.Equal([documented.Id, undocumented.Id], ranked.Select(node => node.Id));
    }

    [Fact]
    public void Apply_ExactPattern_MatchesOnlyExactNamespaceNode()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Company.Services"] = new() { Id = "Company.Services", Name = "Services", Kind = NodeKind.Namespace },
            ["Company.Services.Internal"] = new() { Id = "Company.Services.Internal", Name = "Internal", Kind = NodeKind.Namespace }
        };

        var result = NamespaceFilter.Apply(nodes, "Company.Services");

        var match = Assert.Single(result);
        Assert.Equal("Company.Services", match.Key);
    }
}

public class QuerySuggestionMutationTests
{
    [Fact]
    public void Generate_PseudoInterfaces_DoNotProduceResolveToSuggestion()
    {
        var target = MakeNode("OrderService");
        var xmlParser = MakeNode("XMLParser");
        var singleLetter = MakeNode("I");
        var lowerCase = MakeNode("iOrderRepository");
        var result = CreateResult(target, new[] { target }, new[] { target, xmlParser, singleLetter, lowerCase });

        var suggestions = QuerySuggestionGenerator.Generate(
            result,
            new QueryOptions { Pattern = target.Name, Depth = 2 },
            maxSuggestions: 5);

        Assert.DoesNotContain(suggestions, suggestion => suggestion.Contains("--kind resolves-to", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_SingleNodeSession_DoesNotSuggestTheTargetAsAnUnexploredNeighbor()
    {
        var target = MakeNode("OrderService");
        var session = new QuerySessionTracker();
        var result = CreateResult(
            target,
            new[] { target },
            new[] { target },
            new GraphEdge { FromId = target.Id, ToId = "Other", Type = EdgeType.Calls });

        var suggestions = QuerySuggestionGenerator.Generate(
            result,
            new QueryOptions { Pattern = target.Name, Depth = 1 },
            session,
            maxSuggestions: 5);

        Assert.DoesNotContain(
            $"codegraph query {target.Name} --depth 1 --format compact",
            suggestions);
    }

    private static GraphNode MakeNode(string id, NodeKind kind = NodeKind.Type, string assembly = "Orders.Core")
    {
        return new GraphNode
        {
            Id = id,
            Name = id,
            Kind = kind,
            FilePath = $"src/{id}.cs",
            AssemblyName = assembly
        };
    }

    private static QueryResult CreateResult(
        GraphNode? targetNode,
        IReadOnlyList<GraphNode> matchedNodes,
        IReadOnlyList<GraphNode> nodes,
        params GraphEdge[] edges)
    {
        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = matchedNodes.ToList(),
            Nodes = nodes.ToDictionary(node => node.Id, node => node),
            Edges = edges.ToList(),
            Metadata = new GraphMetadata()
        };
    }
}

public class ReportMutationTests
{
    [Fact]
    public void Analyze_MethodOnlyAssembly_ReturnsNoCoverageEntries()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Asm.Run"] = new() { Id = "Asm.Run", Name = "Run", Kind = NodeKind.Method, AssemblyName = "Asm" }
        };

        var coverage = CoverageAnalyzer.Analyze(nodes, new List<GraphEdge>());

        Assert.Empty(coverage);
    }

    [Fact]
    public void Detect_SameDomainAndUnknownTargets_AreExcludedFromCrossDomainConnections()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Finance.A"] = CreateTypeNode("Finance.A", "TypeA", "Company.Modules.Finance"),
            ["Finance.B"] = CreateTypeNode("Finance.B", "TypeB", "Company.Product.Finance"),
            ["Planning.C"] = CreateTypeNode("Planning.C", "TypeC", "Company.Modules.Planning")
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Finance.A", ToId = "Finance.B", Type = EdgeType.Calls },
            new() { FromId = "Finance.A", ToId = "Missing.Type", Type = EdgeType.Calls }
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        var finance = clusters.Single(cluster => cluster.Name == "Finance");
        Assert.Empty(finance.CrossDomainConnections);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("My.App")]
    public void ShortenAssemblyName_OneOrTwoSegments_ReturnsOriginalName(string assemblyName)
    {
        var result = InvokePrivateStatic<string>(typeof(DomainClusterAnalyzer), "ShortenAssemblyName", assemblyName);

        Assert.Equal(assemblyName, result);
    }

    private static GraphNode CreateTypeNode(string id, string name, string assemblyName)
    {
        return new GraphNode
        {
            Id = id,
            Name = name,
            Kind = NodeKind.Type,
            AssemblyName = assemblyName
        };
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] parameters)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find {type.FullName}.{methodName}.");
        return Assert.IsType<T>(method.Invoke(null, parameters));
    }
}

public sealed class WikiRemainingMutationTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(AppContext.BaseDirectory, $"wiki-remaining-{Guid.NewGuid():N}");

    [Fact]
    public void SanitizeFileName_AngleBrackets_AreReplacedWithUnderscores()
    {
        Assert.Equal("My_Assembly_", WikiGenerator.SanitizeFileName("My<Assembly>"));
    }

    [Fact]
    public void Generate_AssemblyNameWithAngleBrackets_CreatesSanitizedAssemblyPage()
    {
        var assemblyName = "My<Assembly>";
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Type"] = new GraphNode { Id = "Type", Name = "Type", Kind = NodeKind.Type, AssemblyName = assemblyName }
        };
        var metadata = new GraphMetadata
        {
            SolutionName = "WikiSolution",
            CommitHash = "abc123",
            GeneratedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        WikiGenerator.Generate(_outputDir, nodes, new List<GraphEdge>(), metadata);

        Assert.True(File.Exists(Path.Combine(_outputDir, "assemblies", "My_Assembly_.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }
}
