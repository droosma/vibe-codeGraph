using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public class PackageQueryEngineTests
{
    private static GraphNode MakeNode(string id, string assemblyName, NodeKind kind = NodeKind.Type) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        AssemblyName = assemblyName,
        Accessibility = Accessibility.Public
    };

    private static GraphEdge MakeExternalEdge(string fromId, string toId, string packageSource,
        EdgeType type = EdgeType.Calls) => new()
    {
        FromId = fromId,
        ToId = toId,
        Type = type,
        IsExternal = true,
        PackageSource = packageSource,
        Confidence = EdgeConfidence.Verified
    };

    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["ProjectA.Service"] = MakeNode("ProjectA.Service", "ProjectA"),
            ["ProjectA.Service.Serialize()"] = MakeNode("ProjectA.Service.Serialize()", "ProjectA", NodeKind.Method),
            ["ProjectA.Controller"] = MakeNode("ProjectA.Controller", "ProjectA"),
            ["ProjectB.JsonHandler"] = MakeNode("ProjectB.JsonHandler", "ProjectB"),
            ["ProjectB.JsonHandler.Handle()"] = MakeNode("ProjectB.JsonHandler.Handle()", "ProjectB", NodeKind.Method),
            ["ProjectB.Logger"] = MakeNode("ProjectB.Logger", "ProjectB"),
            ["Newtonsoft.Json.JsonConvert"] = MakeNode("Newtonsoft.Json.JsonConvert", "Newtonsoft.Json", NodeKind.Method),
            ["Newtonsoft.Json.JsonSerializer"] = MakeNode("Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json", NodeKind.Type),
            ["Serilog.Log"] = MakeNode("Serilog.Log", "Serilog", NodeKind.Type),
        };

        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("ProjectA.Service.Serialize()", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/13.0.1"),
            MakeExternalEdge("ProjectA.Controller", "Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json/13.0.1", EdgeType.DependsOn),
            MakeExternalEdge("ProjectB.JsonHandler.Handle()", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/12.0.3"),
            MakeExternalEdge("ProjectB.JsonHandler", "Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json/12.0.3", EdgeType.DependsOn),
            MakeExternalEdge("ProjectB.Logger", "Serilog.Log", "Serilog/3.0.0", EdgeType.DependsOn),
        };

        return (nodes, edges);
    }

    [Fact]
    public void ListPackages_ProjectFilter_ReturnsProjectScopedPackages()
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.ListPackages("ProjectB");

        Assert.Equal(2, result.Count);
        Assert.All(result, usage => Assert.Equal("ProjectB", usage.ProjectName));
        Assert.Contains(result, usage => usage.PackageId == "Newtonsoft.Json" && usage.Version == "12.0.3");
        Assert.Contains(result, usage => usage.PackageId == "Serilog" && usage.Version == "3.0.0");
    }

    [Fact]
    public void ListPackages_GroupsByProjectAndPackageVersion()
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.ListPackages();

        Assert.Equal(3, result.Count);

        var projectANewtonsoft = result.Single(usage => usage.ProjectName == "ProjectA" && usage.PackageId == "Newtonsoft.Json");
        Assert.Equal(2, projectANewtonsoft.InternalUsageCount);
        Assert.Equal(2, projectANewtonsoft.ExternalTypeCount);
        Assert.Contains("ProjectA.Service.Serialize()", projectANewtonsoft.ExampleInternalUsers);
        Assert.Contains("ProjectA.Controller", projectANewtonsoft.ExampleInternalUsers);
    }

    [Fact]
    public void FindWhoUses_ReturnsTypesAndMethodsDependingOnPackage()
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.FindWhoUses("Newtonsoft.Json");

        Assert.Equal(4, result.Count);
        Assert.Contains(result, dependent => dependent.ConsumerId == "ProjectA.Service.Serialize()" && dependent.ConsumerKind == NodeKind.Method);
        Assert.Contains(result, dependent => dependent.ConsumerId == "ProjectA.Controller" && dependent.ConsumerKind == NodeKind.Type);
        Assert.Contains(result, dependent => dependent.ConsumerId == "ProjectB.JsonHandler.Handle()" && dependent.ProjectName == "ProjectB");
        Assert.Contains(result, dependent => dependent.ConsumerId == "ProjectB.JsonHandler" && dependent.ExternalSymbols.Contains("Newtonsoft.Json.JsonSerializer"));
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("newtonsoft.json")]
    public void FindWhoUses_IsCaseInsensitive(string packageName)
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.FindWhoUses(packageName);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void FindConflicts_ReturnsVersionMismatchesAcrossProjects()
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var conflicts = engine.FindConflicts();

        var conflict = Assert.Single(conflicts);
        Assert.Equal("Newtonsoft.Json", conflict.PackageId);
        Assert.Equal("13.0.1", conflict.VersionsByProject["ProjectA"]);
        Assert.Equal("12.0.3", conflict.VersionsByProject["ProjectB"]);
    }

    [Fact]
    public void FindWhoUses_UnknownPackage_ReturnsEmpty()
    {
        var (nodes, edges) = BuildGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.FindWhoUses("Bogus.Package");

        Assert.Empty(result);
    }
}
