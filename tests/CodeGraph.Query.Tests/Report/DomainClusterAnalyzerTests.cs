using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class DomainClusterAnalyzerTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string name, NodeKind kind, string assembly)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, name, kind, assembly) in defs)
            nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = assembly };
        return nodes;
    }

    [Fact]
    public void Detect_EmptyGraph_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        Assert.Empty(clusters);
    }

    [Fact]
    public void Detect_SingleDomain_NoClusterOutput()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "MyApp"));
        var edges = new List<GraphEdge>();

        // Only 1 domain so should not output clusters (count > 1 requirement in report)
        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        Assert.Single(clusters);
    }

    [Fact]
    public void Detect_MultipleDomains_GroupedByLastSegment()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Company.Product.Modules.Finance"),
            ("B", "TypeB", NodeKind.Type, "Company.Product.Modules.Finance"),
            ("C", "TypeC", NodeKind.Type, "Company.Product.Modules.Planning"));
        var edges = new List<GraphEdge>();

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Name == "Finance" && c.TypeCount == 2);
        Assert.Contains(clusters, c => c.Name == "Planning" && c.TypeCount == 1);
    }

    [Fact]
    public void Detect_ShortAssemblyNames_UsedAsIs()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Core"),
            ("B", "TypeB", NodeKind.Type, "Api"));
        var edges = new List<GraphEdge>();

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Name == "Core");
        Assert.Contains(clusters, c => c.Name == "Api");
    }

    [Fact]
    public void Detect_KeyTypes_HighestDegreeWithinCluster()
    {
        var nodes = CreateNodes(
            ("A", "HubType", NodeKind.Type, "Company.Modules.Finance"),
            ("B", "LeafType", NodeKind.Type, "Company.Modules.Finance"),
            ("C", "Other", NodeKind.Type, "Company.Modules.Planning"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
            new() { FromId = "A", ToId = "C", Type = EdgeType.DependsOn }
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        var finance = clusters.Single(c => c.Name == "Finance");
        Assert.Contains("HubType", finance.KeyTypes);
    }

    [Fact]
    public void Detect_CrossDomainConnections_Detected()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Company.Modules.Finance"),
            ("B", "TypeB", NodeKind.Type, "Company.Modules.Planning"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        var finance = clusters.Single(c => c.Name == "Finance");
        Assert.Single(finance.CrossDomainConnections);
        Assert.Equal("Planning", finance.CrossDomainConnections[0].TargetDomain);
        Assert.Equal(1, finance.CrossDomainConnections[0].EdgeCount);
    }

    [Fact]
    public void Detect_SortedByTypeCountDescending()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Company.Modules.Small"),
            ("B", "TypeB", NodeKind.Type, "Company.Modules.Big"),
            ("C", "TypeC", NodeKind.Type, "Company.Modules.Big"),
            ("D", "TypeD", NodeKind.Type, "Company.Modules.Big"));
        var edges = new List<GraphEdge>();

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        Assert.Equal("Big", clusters[0].Name);
        Assert.Equal(3, clusters[0].TypeCount);
        Assert.Equal("Small", clusters[1].Name);
        Assert.Equal(1, clusters[1].TypeCount);
    }

    [Fact]
    public void ExtractDomainName_MultiSegment_ReturnsLastSegment()
    {
        Assert.Equal("Financien", DomainClusterAnalyzer.ExtractDomainName("Vertimart.Exquise.BSExquise.Modules.Financien"));
        Assert.Equal("Planning", DomainClusterAnalyzer.ExtractDomainName("Company.Product.Modules.Planning"));
    }

    [Fact]
    public void ExtractDomainName_ShortName_ReturnsFullName()
    {
        Assert.Equal("MyApp", DomainClusterAnalyzer.ExtractDomainName("MyApp"));
        Assert.Equal("My.App", DomainClusterAnalyzer.ExtractDomainName("My.App"));
    }

    [Fact]
    public void ExtractDomainName_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DomainClusterAnalyzer.ExtractDomainName(""));
        Assert.Equal(string.Empty, DomainClusterAnalyzer.ExtractDomainName(null!));
    }

    [Fact]
    public void Detect_ContainsEdges_Excluded()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Company.Modules.Finance"),
            ("B", "TypeB", NodeKind.Type, "Company.Modules.Planning"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Contains }
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        var finance = clusters.Single(c => c.Name == "Finance");
        Assert.Empty(finance.CrossDomainConnections);
        Assert.Empty(finance.KeyTypes);
    }

    [Fact]
    public void Detect_LongAssemblyNames_AreShortenedToLastTwoSegments()
    {
        var nodes = CreateNodes(
            ("A", "FinanceType", NodeKind.Type, "Company.Product.Modules.Finance"),
            ("B", "PlanningType", NodeKind.Type, "Company.Product.Modules.Planning"));

        var clusters = DomainClusterAnalyzer.Detect(nodes, new List<GraphEdge>());

        Assert.Contains("Modules.Finance", clusters.Single(c => c.Name == "Finance").Assemblies);
        Assert.Contains("Modules.Planning", clusters.Single(c => c.Name == "Planning").Assemblies);
    }

    [Fact]
    public void Detect_CrossDomainConnections_AreLimitedToTopThreeByEdgeCount()
    {
        var nodes = CreateNodes(
            ("FinanceType", "FinanceType", NodeKind.Type, "Company.Modules.Finance"),
            ("PlanningType", "PlanningType", NodeKind.Type, "Company.Modules.Planning"),
            ("SalesType", "SalesType", NodeKind.Type, "Company.Modules.Sales"),
            ("HrType", "HrType", NodeKind.Type, "Company.Modules.HR"),
            ("OpsType", "OpsType", NodeKind.Type, "Company.Modules.Operations"));
        var edges = new List<GraphEdge>
        {
            new() { FromId = "FinanceType", ToId = "PlanningType", Type = EdgeType.Calls },
            new() { FromId = "FinanceType", ToId = "PlanningType", Type = EdgeType.DependsOn },
            new() { FromId = "FinanceType", ToId = "PlanningType", Type = EdgeType.References },
            new() { FromId = "FinanceType", ToId = "SalesType", Type = EdgeType.Calls },
            new() { FromId = "FinanceType", ToId = "SalesType", Type = EdgeType.DependsOn },
            new() { FromId = "FinanceType", ToId = "HrType", Type = EdgeType.Calls },
            new() { FromId = "FinanceType", ToId = "OpsType", Type = EdgeType.Calls }
        };

        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);

        var finance = clusters.Single(c => c.Name == "Finance");
        Assert.Equal(3, finance.CrossDomainConnections.Count);
        Assert.Equal(("Planning", 3), (finance.CrossDomainConnections[0].TargetDomain, finance.CrossDomainConnections[0].EdgeCount));
        Assert.Equal(("Sales", 2), (finance.CrossDomainConnections[1].TargetDomain, finance.CrossDomainConnections[1].EdgeCount));
        Assert.Equal(("HR", 1), (finance.CrossDomainConnections[2].TargetDomain, finance.CrossDomainConnections[2].EdgeCount));
    }
}
