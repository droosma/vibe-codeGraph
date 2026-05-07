using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class QuerySuggesterTests
{
    [Fact]
    public void Suggest_EmptyInputs_ReturnsEmptyList()
    {
        var hubs = new List<HubAnalyzer.HubNode>();
        var clusters = new List<ClusterAnalyzer.ClusterInfo>();

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Empty(result);
    }

    [Fact]
    public void Suggest_SingleHub_TwoSuggestions()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "OrderService", Core.Models.NodeKind.Type, 5, 3, 8)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>();

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Equal(2, result.Count);
        Assert.Contains("codegraph query OrderService --depth 1 --mode focused", result);
        Assert.Contains("codegraph query OrderService --depth 2 --kind calls", result);
    }

    [Fact]
    public void Suggest_ThreeHubs_SixSuggestions()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "Alpha", Core.Models.NodeKind.Type, 5, 3, 8),
            new("id2", "Beta", Core.Models.NodeKind.Type, 4, 2, 6),
            new("id3", "Gamma", Core.Models.NodeKind.Type, 3, 1, 4)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>();

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Equal(6, result.Count);
        Assert.Contains("codegraph query Alpha --depth 1 --mode focused", result);
        Assert.Contains("codegraph query Alpha --depth 2 --kind calls", result);
        Assert.Contains("codegraph query Beta --depth 1 --mode focused", result);
        Assert.Contains("codegraph query Beta --depth 2 --kind calls", result);
        Assert.Contains("codegraph query Gamma --depth 1 --mode focused", result);
        Assert.Contains("codegraph query Gamma --depth 2 --kind calls", result);
    }

    [Fact]
    public void Suggest_MoreThanThreeHubs_OnlyTopThree()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "A", Core.Models.NodeKind.Type, 5, 3, 8),
            new("id2", "B", Core.Models.NodeKind.Type, 4, 2, 6),
            new("id3", "C", Core.Models.NodeKind.Type, 3, 1, 4),
            new("id4", "D", Core.Models.NodeKind.Type, 2, 1, 3)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>();

        var result = QuerySuggester.Suggest(hubs, clusters);

        // Only 3 hubs * 2 = 6 suggestions, hub D is excluded
        Assert.Equal(6, result.Count);
        Assert.DoesNotContain("codegraph query D --depth 1 --mode focused", result);
    }

    [Fact]
    public void Suggest_ClusterWithMoreExternalThanInternal_AddsListCommand()
    {
        var hubs = new List<HubAnalyzer.HubNode>();
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("LeakyAssembly", 5, 2, 10)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Single(result);
        Assert.Contains("codegraph list types --assembly LeakyAssembly", result);
    }

    [Fact]
    public void Suggest_ClusterWithEqualExternalAndInternal_NoListCommand()
    {
        var hubs = new List<HubAnalyzer.HubNode>();
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("BalancedAssembly", 5, 5, 5)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Empty(result);
    }

    [Fact]
    public void Suggest_ClusterWithLessExternalThanInternal_NoListCommand()
    {
        var hubs = new List<HubAnalyzer.HubNode>();
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("TightAssembly", 5, 10, 2)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Empty(result);
    }

    [Fact]
    public void Suggest_MoreThanTwoLeakyClusters_OnlyTopTwo()
    {
        var hubs = new List<HubAnalyzer.HubNode>();
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("Leak1", 5, 1, 10),
            new("Leak2", 5, 1, 10),
            new("Leak3", 5, 1, 10)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Equal(2, result.Count);
        Assert.Contains("codegraph list types --assembly Leak1", result);
        Assert.Contains("codegraph list types --assembly Leak2", result);
    }

    [Fact]
    public void Suggest_HubsAndClusters_Combined()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "HubType", Core.Models.NodeKind.Type, 5, 3, 8)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("LeakyAsm", 5, 1, 10)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Equal(3, result.Count);
        Assert.Contains("codegraph query HubType --depth 1 --mode focused", result);
        Assert.Contains("codegraph query HubType --depth 2 --kind calls", result);
        Assert.Contains("codegraph list types --assembly LeakyAsm", result);
    }

    [Fact]
    public void Suggest_AllCommandsStartWithCodegraph()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "TypeA", Core.Models.NodeKind.Type, 5, 3, 8)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>
        {
            new("Asm1", 5, 1, 10)
        };

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.All(result, s => Assert.StartsWith("codegraph ", s));
    }

    [Fact]
    public void Suggest_FocusedAndCallsSuggestions_HaveCorrectFlags()
    {
        var hubs = new List<HubAnalyzer.HubNode>
        {
            new("id1", "MyType", Core.Models.NodeKind.Type, 5, 3, 8)
        };
        var clusters = new List<ClusterAnalyzer.ClusterInfo>();

        var result = QuerySuggester.Suggest(hubs, clusters);

        Assert.Contains(result, s => s.Contains("--mode focused"));
        Assert.Contains(result, s => s.Contains("--kind calls"));
        Assert.Contains(result, s => s.Contains("--depth 1"));
        Assert.Contains(result, s => s.Contains("--depth 2"));
    }
}
