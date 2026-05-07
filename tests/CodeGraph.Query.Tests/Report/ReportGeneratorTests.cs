using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class ReportGeneratorTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string name, NodeKind kind, string assembly)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, name, kind, assembly) in defs)
            nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = assembly };
        return nodes;
    }

    private static GraphMetadata CreateMetadata() => new()
    {
        SolutionName = "TestSolution",
        CommitHash = "abc123",
        GeneratedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void Generate_ContainsExpectedSections()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var edges = new List<GraphEdge>();
        var metadata = CreateMetadata();

        var report = ReportGenerator.Generate(nodes, edges, metadata);

        Assert.Contains("# CodeGraph Report", report);
        Assert.Contains("## Hub Types (highest connectivity)", report);
        Assert.Contains("## Assemblies", report);
        Assert.Contains("## Suggested Queries", report);
    }

    [Fact]
    public void Generate_IncludesHubTypes()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("B", "TypeB", NodeKind.Type, "Asm1"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("TypeA", report);
        Assert.Contains("TypeB", report);
    }

    [Fact]
    public void Generate_IncludesAssemblyTable()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "MyAssembly"),
            ("B", "TypeB", NodeKind.Type, "MyAssembly"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("MyAssembly", report);
        Assert.Contains("| Assembly |", report);
    }

    [Fact]
    public void Generate_IncludesMetadata()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
        var metadata = CreateMetadata();

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), metadata);

        Assert.Contains("**Solution:** TestSolution", report);
        Assert.Contains("**Commit:** abc123", report);
    }

    [Fact]
    public void Generate_IncludesCoverageWhenPresent()
    {
        var nodes = CreateNodes(
            ("A", "TypeA", NodeKind.Type, "Asm1"),
            ("T", "TestA", NodeKind.Type, "Tests"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var report = ReportGenerator.Generate(nodes, edges, CreateMetadata());

        Assert.Contains("## Test Coverage (by assembly)", report);
    }

    [Fact]
    public void Generate_OmitsCoverageWhenNonePresent()
    {
        var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));

        var report = ReportGenerator.Generate(nodes, new List<GraphEdge>(), CreateMetadata());

        Assert.DoesNotContain("## Test Coverage", report);
    }
}
