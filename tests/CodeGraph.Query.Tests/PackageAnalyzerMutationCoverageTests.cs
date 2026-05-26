using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class PackageAnalyzerMutationCoverageTests
{
    private static GraphNode CreateNode(string id, string assemblyName, NodeKind kind = NodeKind.Type)
    {
        return new GraphNode
        {
            Id = id,
            Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
            Kind = kind,
            AssemblyName = assemblyName,
            Accessibility = Accessibility.Public
        };
    }

    private static GraphEdge CreateExternalEdge(string fromId, string toId, string packageSource, EdgeType type = EdgeType.Calls)
    {
        return new GraphEdge
        {
            FromId = fromId,
            ToId = toId,
            Type = type,
            IsExternal = true,
            PackageSource = packageSource
        };
    }

    [Fact]
    public void ListPackages_SortsByProjectPackageAndVersion()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Zoo.Controller"] = CreateNode("Zoo.Controller", "Zoo"),
            ["Alpha.Service"] = CreateNode("Alpha.Service", "Alpha"),
            ["Alpha.LegacyService"] = CreateNode("Alpha.LegacyService", "Alpha"),
            ["Alpha.Logger"] = CreateNode("Alpha.Logger", "Alpha")
        };
        var edges = new List<GraphEdge>
        {
            CreateExternalEdge("Zoo.Controller", "Zeta.Lib.Client", "Zeta.Lib/2.0.0"),
            CreateExternalEdge("Alpha.Logger", "Serilog.Log", "Serilog/3.0.0"),
            CreateExternalEdge("Alpha.Service", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/13.0.1"),
            CreateExternalEdge("Alpha.LegacyService", "Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json/12.0.3")
        };
        var engine = new PackageQueryEngine(nodes, edges);

        var usages = engine.ListPackages();

        Assert.Collection(usages,
            usage => Assert.Equal(("Alpha", "Newtonsoft.Json", "12.0.3"), (usage.ProjectName, usage.PackageId, usage.Version)),
            usage => Assert.Equal(("Alpha", "Newtonsoft.Json", "13.0.1"), (usage.ProjectName, usage.PackageId, usage.Version)),
            usage => Assert.Equal(("Alpha", "Serilog", "3.0.0"), (usage.ProjectName, usage.PackageId, usage.Version)),
            usage => Assert.Equal(("Zoo", "Zeta.Lib", "2.0.0"), (usage.ProjectName, usage.PackageId, usage.Version)));
    }

    [Fact]
    public void AnalyzeByProject_WhitespaceProjectFilter_DoesNotFilterResults()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Alpha.Service"] = CreateNode("Alpha.Service", "Alpha"),
            ["Zoo.Controller"] = CreateNode("Zoo.Controller", "Zoo")
        };
        var edges = new List<GraphEdge>
        {
            CreateExternalEdge("Alpha.Service", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/13.0.1"),
            CreateExternalEdge("Zoo.Controller", "Serilog.Log", "Serilog/3.0.0")
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var allResults = analyzer.AnalyzeByProject();
        var whitespaceResults = analyzer.AnalyzeByProject("   ");

        Assert.Equal(
            allResults.Select(usage => (usage.ProjectName, usage.PackageId, usage.Version)),
            whitespaceResults.Select(usage => (usage.ProjectName, usage.PackageId, usage.Version)));
    }

    [Fact]
    public void ListPackages_DeduplicatesInternalUsersAndExternalSymbols()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = CreateNode("App.Service", "App")
        };
        var edges = new List<GraphEdge>
        {
            CreateExternalEdge("App.Service", "Pkg.External.Type", "Pkg/1.0.0"),
            CreateExternalEdge("App.Service", "Pkg.External.Type", "Pkg/1.0.0")
        };
        var engine = new PackageQueryEngine(nodes, edges);

        var usage = Assert.Single(engine.ListPackages());

        Assert.Equal(1, usage.InternalUsageCount);
        Assert.Equal(1, usage.ExternalTypeCount);
        Assert.Single(usage.ExampleInternalUsers);
        Assert.Single(usage.ExampleExternalSymbols);
        Assert.Equal("App.Service", usage.ExampleInternalUsers[0]);
        Assert.Equal("Pkg.External.Type", usage.ExampleExternalSymbols[0]);
    }

    [Fact]
    public void FindWhoUses_SortsDependentsAndExternalSymbols()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Alpha.ZType"] = CreateNode("Alpha.ZType", "Alpha", NodeKind.Type),
            ["Alpha.AWorker.Run()"] = CreateNode("Alpha.AWorker.Run()", "Alpha", NodeKind.Method),
            ["Beta.Helper"] = CreateNode("Beta.Helper", "Beta", NodeKind.Type)
        };
        var edges = new List<GraphEdge>
        {
            CreateExternalEdge("Beta.Helper", "Pkg.External.Beta", "Pkg/1.0.0"),
            CreateExternalEdge("Alpha.AWorker.Run()", "Pkg.External.Method", "Pkg/1.0.0"),
            CreateExternalEdge("Alpha.ZType", "Pkg.External.Zeta", "Pkg/1.0.0", EdgeType.DependsOn),
            CreateExternalEdge("Alpha.ZType", "Pkg.External.Alpha", "Pkg/1.0.0")
        };
        var engine = new PackageQueryEngine(nodes, edges);

        var dependents = engine.FindWhoUses("Pkg");

        Assert.Collection(dependents,
            dependent =>
            {
                Assert.Equal("Alpha.ZType", dependent.ConsumerId);
                Assert.Equal(NodeKind.Type, dependent.ConsumerKind);
                Assert.Equal(new[] { "Pkg.External.Alpha", "Pkg.External.Zeta" }, dependent.ExternalSymbols);
            },
            dependent =>
            {
                Assert.Equal("Alpha.AWorker.Run()", dependent.ConsumerId);
                Assert.Equal(NodeKind.Method, dependent.ConsumerKind);
                Assert.Equal(new[] { "Pkg.External.Method" }, dependent.ExternalSymbols);
            },
            dependent =>
            {
                Assert.Equal("Beta.Helper", dependent.ConsumerId);
                Assert.Equal(NodeKind.Type, dependent.ConsumerKind);
                Assert.Equal(new[] { "Pkg.External.Beta" }, dependent.ExternalSymbols);
            });
    }

    [Theory]
    [InlineData("/1.2.3")]
    [InlineData("Package/")]
    public void ParsePackageSource_SlashAtBoundaries_ReturnsWholeSource(string packageSource)
    {
        var (packageId, version) = PackageAnalyzer.ParsePackageSource(packageSource);

        Assert.Equal(packageSource, packageId);
        Assert.Null(version);
    }

    [Fact]
    public void FindWhoUses_MissingNode_InfersMethodConsumerNameAndKind()
    {
        var engine = new PackageQueryEngine(
            new Dictionary<string, GraphNode>(),
            new List<GraphEdge>
            {
                CreateExternalEdge("Unknown.Project.Execute()", "Pkg.External.Type", "Pkg/1.0.0")
            });

        var dependent = Assert.Single(engine.FindWhoUses("Pkg"));

        Assert.Equal("Unknown", dependent.ProjectName);
        Assert.Equal("Execute()", dependent.ConsumerName);
        Assert.Equal(NodeKind.Method, dependent.ConsumerKind);
    }
}
