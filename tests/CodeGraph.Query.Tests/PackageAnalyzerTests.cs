using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class PackageAnalyzerTests
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
        PackageSource = packageSource
    };

    private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildPackageGraph()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            // Project A nodes
            ["ProjectA.Service"] = MakeNode("ProjectA.Service", "ProjectA"),
            ["ProjectA.Controller"] = MakeNode("ProjectA.Controller", "ProjectA"),
            // Project B nodes
            ["ProjectB.Handler"] = MakeNode("ProjectB.Handler", "ProjectB"),
            // External nodes (targets of external edges)
            ["Newtonsoft.Json.JsonConvert"] = MakeNode("Newtonsoft.Json.JsonConvert", "Newtonsoft.Json"),
            ["Newtonsoft.Json.JsonSerializer"] = MakeNode("Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json"),
            ["Serilog.Log"] = MakeNode("Serilog.Log", "Serilog"),
        };

        var edges = new List<GraphEdge>
        {
            // ProjectA uses Newtonsoft.Json v13.0.1
            MakeExternalEdge("ProjectA.Service", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/13.0.1"),
            MakeExternalEdge("ProjectA.Controller", "Newtonsoft.Json.JsonSerializer", "Newtonsoft.Json/13.0.1"),
            // ProjectA uses Serilog v3.0.0
            MakeExternalEdge("ProjectA.Service", "Serilog.Log", "Serilog/3.0.0"),
            // ProjectB uses Newtonsoft.Json v12.0.3 (conflict!)
            MakeExternalEdge("ProjectB.Handler", "Newtonsoft.Json.JsonConvert", "Newtonsoft.Json/12.0.3"),
            // Internal edge (should be ignored by package analysis)
            new GraphEdge
            {
                FromId = "ProjectA.Controller",
                ToId = "ProjectA.Service",
                Type = EdgeType.Calls,
                IsExternal = false
            },
        };

        return (nodes, edges);
    }

    #region AnalyzeByProject

    [Fact]
    public void AnalyzeByProject_ReturnsAllPackages()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void AnalyzeByProject_GroupsByProjectAndPackage()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        // ProjectA: Newtonsoft.Json + Serilog = 2 entries
        var projectA = result.Where(r => r.ProjectName == "ProjectA").ToList();
        Assert.Equal(2, projectA.Count);

        // ProjectB: Newtonsoft.Json = 1 entry
        var projectB = result.Where(r => r.ProjectName == "ProjectB").ToList();
        Assert.Single(projectB);
    }

    [Fact]
    public void AnalyzeByProject_CountsExternalTypes()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        // ProjectA Newtonsoft.Json: JsonConvert + JsonSerializer = 2 external types
        var projectAJson = result.First(r => r.ProjectName == "ProjectA" && r.PackageId == "Newtonsoft.Json");
        Assert.Equal(2, projectAJson.ExternalTypeCount);
    }

    [Fact]
    public void AnalyzeByProject_CountsInternalUsers()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        // ProjectA Newtonsoft.Json: Service + Controller = 2 internal users
        var projectAJson = result.First(r => r.ProjectName == "ProjectA" && r.PackageId == "Newtonsoft.Json");
        Assert.Equal(2, projectAJson.InternalUsageCount);
    }

    [Fact]
    public void AnalyzeByProject_IncludesVersion()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        var projectAJson = result.First(r => r.ProjectName == "ProjectA" && r.PackageId == "Newtonsoft.Json");
        Assert.Equal("13.0.1", projectAJson.Version);

        var projectBJson = result.First(r => r.ProjectName == "ProjectB" && r.PackageId == "Newtonsoft.Json");
        Assert.Equal("12.0.3", projectBJson.Version);
    }

    [Fact]
    public void AnalyzeByProject_WithFilter_ReturnsOnlyMatchingProject()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject("ProjectB");

        Assert.Single(result);
        Assert.Equal("ProjectB", result[0].ProjectName);
        Assert.Equal("Newtonsoft.Json", result[0].PackageId);
    }

    [Fact]
    public void AnalyzeByProject_FilterIsCaseInsensitive()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject("projectb");

        Assert.Single(result);
        Assert.Equal("ProjectB", result[0].ProjectName);
    }

    [Fact]
    public void AnalyzeByProject_PopulatesExampleSymbols()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        var projectAJson = result.First(r => r.ProjectName == "ProjectA" && r.PackageId == "Newtonsoft.Json");
        Assert.Contains("Newtonsoft.Json.JsonConvert", projectAJson.ExampleExternalSymbols);
        Assert.Contains("Newtonsoft.Json.JsonSerializer", projectAJson.ExampleExternalSymbols);
        Assert.Contains("ProjectA.Service", projectAJson.ExampleInternalUsers);
        Assert.Contains("ProjectA.Controller", projectAJson.ExampleInternalUsers);
    }

    #endregion

    #region AnalyzeByPackage

    [Fact]
    public void AnalyzeByPackage_ReturnsUsageAcrossProjects()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByPackage("Newtonsoft.Json");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.ProjectName == "ProjectA");
        Assert.Contains(result, r => r.ProjectName == "ProjectB");
    }

    [Fact]
    public void AnalyzeByPackage_IsCaseInsensitive()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByPackage("newtonsoft.json");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void AnalyzeByPackage_UnknownPackage_ReturnsEmpty()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByPackage("NonExistent.Package");

        Assert.Empty(result);
    }

    [Fact]
    public void AnalyzeByPackage_SingleProjectPackage_ReturnsSingleEntry()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByPackage("Serilog");

        Assert.Single(result);
        Assert.Equal("ProjectA", result[0].ProjectName);
        Assert.Equal("3.0.0", result[0].Version);
    }

    #endregion

    #region FindConflicts

    [Fact]
    public void FindConflicts_DetectsVersionMismatch()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflicts = analyzer.FindConflicts();

        Assert.Single(conflicts);
        Assert.Equal("Newtonsoft.Json", conflicts[0].PackageId);
        Assert.Equal("13.0.1", conflicts[0].VersionsByProject["ProjectA"]);
        Assert.Equal("12.0.3", conflicts[0].VersionsByProject["ProjectB"]);
    }

    [Fact]
    public void FindConflicts_NoConflicts_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = MakeNode("App.Service", "App"),
            ["App.Controller"] = MakeNode("App.Controller", "App"),
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("App.Service", "Ext.Type1", "SomePackage/1.0.0"),
            MakeExternalEdge("App.Controller", "Ext.Type2", "SomePackage/1.0.0"),
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflicts = analyzer.FindConflicts();

        Assert.Empty(conflicts);
    }

    [Fact]
    public void FindConflicts_SameVersionAcrossProjects_NoConflict()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["ProjA.Svc"] = MakeNode("ProjA.Svc", "ProjA"),
            ["ProjB.Svc"] = MakeNode("ProjB.Svc", "ProjB"),
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("ProjA.Svc", "Ext.Type", "Pkg/2.0.0"),
            MakeExternalEdge("ProjB.Svc", "Ext.Type", "Pkg/2.0.0"),
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflicts = analyzer.FindConflicts();

        Assert.Empty(conflicts);
    }

    #endregion

    #region NoPackages

    [Fact]
    public void AnalyzeByProject_NoExternalEdges_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = MakeNode("App.Service", "App"),
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge
            {
                FromId = "App.Service",
                ToId = "App.Other",
                Type = EdgeType.Calls,
                IsExternal = false
            }
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        Assert.Empty(result);
    }

    [Fact]
    public void AnalyzeByProject_EmptyGraph_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        Assert.Empty(result);
    }

    [Fact]
    public void FindConflicts_EmptyGraph_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflicts = analyzer.FindConflicts();

        Assert.Empty(conflicts);
    }

    #endregion

    #region ParsePackageSource

    [Fact]
    public void ParsePackageSource_WithVersion_SplitsCorrectly()
    {
        var (packageId, version) = PackageAnalyzer.ParsePackageSource("Newtonsoft.Json/13.0.1");

        Assert.Equal("Newtonsoft.Json", packageId);
        Assert.Equal("13.0.1", version);
    }

    [Fact]
    public void ParsePackageSource_WithoutVersion_ReturnsNullVersion()
    {
        var (packageId, version) = PackageAnalyzer.ParsePackageSource("Newtonsoft.Json");

        Assert.Equal("Newtonsoft.Json", packageId);
        Assert.Null(version);
    }

    [Fact]
    public void ParsePackageSource_PreReleaseVersion_ParsesCorrectly()
    {
        var (packageId, version) = PackageAnalyzer.ParsePackageSource("MyLib/1.0.0-beta.1");

        Assert.Equal("MyLib", packageId);
        Assert.Equal("1.0.0-beta.1", version);
    }

    #endregion

    #region FallbackProjectName

    [Fact]
    public void AnalyzeByProject_NodeNotInDictionary_FallsBackToIdPrefix()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("Unknown.Caller", "Ext.Type", "Pkg/1.0.0"),
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject();

        Assert.Single(result);
        Assert.Equal("Unknown", result[0].ProjectName);
    }

    [Fact]
    public void AnalyzeByProject_FilterWithoutMatches_ReturnsEmpty()
    {
        var (nodes, edges) = BuildPackageGraph();
        var analyzer = new PackageAnalyzer(nodes, edges);

        var result = analyzer.AnalyzeByProject("MissingProject");

        Assert.Empty(result);
    }

    [Fact]
    public void AnalyzeByProject_IgnoresBlankPackageSourceAndBlankPackageId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = MakeNode("App.Service", "App")
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("App.Service", "Ext.Type1", string.Empty),
            MakeExternalEdge("App.Service", "Ext.Type2", "   "),
            MakeExternalEdge("App.Service", "Ext.Type3", "   /1.0.0"),
            MakeExternalEdge("App.Service", "Ext.Type4", "Valid.Package/2.0.0")
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var usage = Assert.Single(analyzer.AnalyzeByProject());

        Assert.Equal("Valid.Package", usage.PackageId);
        Assert.Equal("2.0.0", usage.Version);
    }

    [Fact]
    public void AnalyzeByProject_NodeWithBlankAssemblyAndName_FallsBackToNodeIdParts()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Fallback.Project.Handler"] = new GraphNode
            {
                Id = "Fallback.Project.Handler",
                Name = string.Empty,
                Kind = NodeKind.Type,
                AssemblyName = string.Empty,
                Accessibility = Accessibility.Public
            }
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("Fallback.Project.Handler", "Pkg.External.Type", "Pkg/1.0.0")
        };
        var analyzer = new PackageAnalyzer(nodes, edges);
        var engine = new PackageQueryEngine(nodes, edges);

        var usage = Assert.Single(analyzer.AnalyzeByProject());
        var dependent = Assert.Single(engine.FindWhoUses("Pkg"));

        Assert.Equal("Fallback", usage.ProjectName);
        Assert.Equal("Fallback.Project.Handler", dependent.ConsumerName);
        Assert.Equal(NodeKind.Type, dependent.ConsumerKind);
    }

    [Fact]
    public void FindWhoUses_WhitespacePackageName_ReturnsEmpty()
    {
        var (nodes, edges) = BuildPackageGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.FindWhoUses("   ");

        Assert.Empty(result);
    }

    [Fact]
    public void FindWhoUses_ProjectFilter_ReturnsOnlyMatchingProject()
    {
        var (nodes, edges) = BuildPackageGraph();
        var engine = new PackageQueryEngine(nodes, edges);

        var result = engine.FindWhoUses("Newtonsoft.Json", "ProjectB");

        var dependent = Assert.Single(result);
        Assert.Equal("ProjectB", dependent.ProjectName);
        Assert.Equal("ProjectB.Handler", dependent.ConsumerId);
    }

    [Fact]
    public void FindWhoUses_MissingNodeWithoutMethodSignature_InfersTypeConsumerNameAndKind()
    {
        var engine = new PackageQueryEngine(
            new Dictionary<string, GraphNode>(),
            new List<GraphEdge>
            {
                MakeExternalEdge("Unknown.Project.TypeName", "Pkg.External.Type", "Pkg/1.0.0")
            });

        var dependent = Assert.Single(engine.FindWhoUses("Pkg"));

        Assert.Equal("Unknown", dependent.ProjectName);
        Assert.Equal("TypeName", dependent.ConsumerName);
        Assert.Equal(NodeKind.Type, dependent.ConsumerKind);
    }

    [Fact]
    public void FindConflicts_DuplicateReferencesInSameProject_KeepsFirstVersion()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["App.Service"] = MakeNode("App.Service", "App"),
            ["Other.Service"] = MakeNode("Other.Service", "Other")
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("App.Service", "Pkg.TypeA", "Pkg/1.0.0"),
            MakeExternalEdge("App.Service", "Pkg.TypeB", "Pkg/9.9.9"),
            MakeExternalEdge("Other.Service", "Pkg.TypeC", "Pkg/2.0.0")
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflict = Assert.Single(analyzer.FindConflicts());

        Assert.Equal("Pkg", conflict.PackageId);
        Assert.Equal("1.0.0", conflict.VersionsByProject["App"]);
        Assert.Equal("2.0.0", conflict.VersionsByProject["Other"]);
    }

    [Fact]
    public void FindConflicts_VersionsDifferOnlyByCase_AreNotConflicts()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["ProjA.Service"] = MakeNode("ProjA.Service", "ProjA"),
            ["ProjB.Service"] = MakeNode("ProjB.Service", "ProjB")
        };
        var edges = new List<GraphEdge>
        {
            MakeExternalEdge("ProjA.Service", "Pkg.Type", "Pkg/1.0.0-BETA"),
            MakeExternalEdge("ProjB.Service", "Pkg.Type", "Pkg/1.0.0-beta")
        };
        var analyzer = new PackageAnalyzer(nodes, edges);

        var conflicts = analyzer.FindConflicts();

        Assert.Empty(conflicts);
    }

    #endregion

    #region PackageFormatter

    [Fact]
    public void FormatUsage_EmptyList_ShowsNoPackageMessage()
    {
        var output = PackageFormatter.FormatUsage(Array.Empty<PackageUsage>());

        Assert.Contains("No package usage found.", output);
    }

    [Fact]
    public void FormatUsage_WithEntries_ShowsProjectAndPackageHeaders()
    {
        var usages = new List<PackageUsage>
        {
            new("Newtonsoft.Json", "13.0.1", "ProjectA", 2, 1,
                new List<string> { "ProjectA.Service" },
                new List<string> { "Newtonsoft.Json.JsonConvert" })
        };

        var output = PackageFormatter.FormatUsage(usages);

        Assert.Contains("# Package usage (1 entries)", output);
        Assert.Contains("## ProjectA", output);
        Assert.Contains("- Newtonsoft.Json v13.0.1", output);
        Assert.Contains("Types: 2, Usages: 1", output);
    }

    [Fact]
    public void FormatUsage_NullVersion_OmitsVersionSuffix()
    {
        var usages = new List<PackageUsage>
        {
            new("SomeLib", null, "App", 1, 1,
                new List<string> { "App.Svc" },
                new List<string> { "SomeLib.Type" })
        };

        var output = PackageFormatter.FormatUsage(usages);

        Assert.Contains("- SomeLib", output);
        Assert.DoesNotContain("- SomeLib v", output);
    }

    [Fact]
    public void FormatConflicts_EmptyList_ShowsNoConflictsMessage()
    {
        var output = PackageFormatter.FormatConflicts(Array.Empty<PackageConflict>());

        Assert.Contains("No version conflicts detected.", output);
    }

    [Fact]
    public void FormatConflicts_WithEntries_ShowsProjectVersions()
    {
        var conflicts = new List<PackageConflict>
        {
            new("Newtonsoft.Json", new Dictionary<string, string?>
            {
                ["ProjectA"] = "13.0.1",
                ["ProjectB"] = "12.0.3"
            })
        };

        var output = PackageFormatter.FormatConflicts(conflicts);

        Assert.Contains("# Version conflicts (1 packages)", output);
        Assert.Contains("## Newtonsoft.Json", output);
        Assert.Contains("- ProjectA: 13.0.1", output);
        Assert.Contains("- ProjectB: 12.0.3", output);
    }

    [Fact]
    public void FormatConflicts_NullVersion_ShowsUnknown()
    {
        var conflicts = new List<PackageConflict>
        {
            new("Pkg", new Dictionary<string, string?>
            {
                ["ProjA"] = "1.0.0",
                ["ProjB"] = null
            })
        };

        var output = PackageFormatter.FormatConflicts(conflicts);

        Assert.Contains("- ProjB: (unknown)", output);
    }

    [Fact]
    public void FormatUsageAsJson_ProducesValidJson()
    {
        var usages = new List<PackageUsage>
        {
            new("Pkg", "1.0.0", "App", 1, 1,
                new List<string> { "App.Svc" },
                new List<string> { "Pkg.Type" })
        };

        var json = PackageFormatter.FormatUsageAsJson(usages);

        Assert.Contains("\"packageId\"", json);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"projectName\"", json);
    }

    [Fact]
    public void FormatConflictsAsJson_ProducesValidJson()
    {
        var conflicts = new List<PackageConflict>
        {
            new("Pkg", new Dictionary<string, string?>
            {
                ["ProjA"] = "1.0.0",
                ["ProjB"] = "2.0.0"
            })
        };

        var json = PackageFormatter.FormatConflictsAsJson(conflicts);

        Assert.Contains("\"packageId\"", json);
        Assert.Contains("\"versionsByProject\"", json);
    }

    [Fact]
    public void FormatUsage_OutputIsTrimmed()
    {
        var usages = new List<PackageUsage>
        {
            new("Pkg", "1.0.0", "App", 1, 1,
                new List<string> { "App.Svc" },
                new List<string> { "Pkg.Type" })
        };

        var output = PackageFormatter.FormatUsage(usages);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void FormatConflicts_OutputIsTrimmed()
    {
        var conflicts = new List<PackageConflict>
        {
            new("Pkg", new Dictionary<string, string?>
            {
                ["ProjA"] = "1.0.0",
                ["ProjB"] = "2.0.0"
            })
        };

        var output = PackageFormatter.FormatConflicts(conflicts);

        Assert.Equal(output, output.TrimEnd());
    }

    #endregion
}
