using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query.Tests.Report;

public class CoverageAnalyzerTests
{
    private static Dictionary<string, GraphNode> CreateNodes(params (string id, string assembly)[] defs)
    {
        var nodes = new Dictionary<string, GraphNode>();
        foreach (var (id, assembly) in defs)
            nodes[id] = new GraphNode { Id = id, Name = id, Kind = NodeKind.Type, AssemblyName = assembly };
        return nodes;
    }

    [Fact]
    public void Analyze_CalculatesCoveragePercentage()
    {
        var nodes = CreateNodes(
            ("A", "Assembly1"),
            ("B", "Assembly1"),
            ("C", "Assembly1"),
            ("T", "Tests"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "T", ToId = "A", Type = EdgeType.Covers }
        };

        var coverage = CoverageAnalyzer.Analyze(nodes, edges);

        var asm1 = coverage.Single(c => c.Assembly == "Assembly1");
        Assert.Equal(3, asm1.TotalTypes);
        Assert.Equal(1, asm1.CoveredTypes);
        Assert.Equal(100.0 / 3, asm1.CoveragePercent, 1);
    }

    [Fact]
    public void Analyze_HandlesNoCoverage()
    {
        var nodes = CreateNodes(("A", "Assembly1"), ("B", "Assembly1"));

        var coverage = CoverageAnalyzer.Analyze(nodes, new List<GraphEdge>());

        var asm1 = coverage.Single(c => c.Assembly == "Assembly1");
        Assert.Equal(0, asm1.CoveredTypes);
        Assert.Equal(0, asm1.CoveragePercent);
    }

    [Fact]
    public void Analyze_CountsCoveredByEdges()
    {
        var nodes = CreateNodes(("A", "Assembly1"), ("T", "Tests"));

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "T", Type = EdgeType.CoveredBy }
        };

        var coverage = CoverageAnalyzer.Analyze(nodes, edges);

        var asm1 = coverage.Single(c => c.Assembly == "Assembly1");
        Assert.Equal(1, asm1.CoveredTypes);
        Assert.Equal(100.0, asm1.CoveragePercent);
    }
}
