using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class ImpactFormatterTests
{
    private static GraphNode MakeNode(
        string id,
        NodeKind kind = NodeKind.Method,
        string? filePath = null,
        int startLine = 0) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        FilePath = filePath ?? string.Empty,
        StartLine = startLine,
        Accessibility = Accessibility.Public
    };

    [Fact]
    public void Format_NullTarget_ShowsNoNodesMessage()
    {
        var result = new ImpactResult("Foo*", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Contains("No nodes found matching 'Foo*'.", output);
    }

    [Fact]
    public void Format_NullTarget_DoesNotContainImpactHeader()
    {
        var result = new ImpactResult("Foo*", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.DoesNotContain("# Impact analysis:", output);
    }

    [Fact]
    public void Format_WithTarget_ShowsImpactHeader()
    {
        var target = MakeNode("Svc.Run");
        var result = new ImpactResult("Svc.Run", target, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Contains("# Impact analysis: Svc.Run", output);
    }

    [Fact]
    public void Format_WithTarget_ShowsTotalAffectedCount()
    {
        var target = MakeNode("Svc.Run");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("A"), MakeNode("B") }, new List<GraphEdge>()),
            new(2, new List<GraphNode> { MakeNode("C") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Svc.Run", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("Total affected: 3 nodes across 2 layer(s)", output);
    }

    [Fact]
    public void Format_SingleLayer_ShowsLayerDepthAndCount()
    {
        var target = MakeNode("Svc.Run");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("Dep.A"), MakeNode("Dep.B") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Svc.Run", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("## Layer 1 (2 nodes)", output);
    }

    [Fact]
    public void Format_NodeWithFile_ShowsKindIdAndFilePath()
    {
        var target = MakeNode("Svc.Run");
        var nodeInLayer = MakeNode("Dep.A", NodeKind.Type, "src/Dep.cs", 15);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { nodeInLayer }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Svc.Run", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("- [Type] Dep.A", output);
        Assert.Contains("(src/Dep.cs:15)", output);
    }

    [Fact]
    public void Format_NodeWithoutFile_OmitsFilePath()
    {
        var target = MakeNode("Svc.Run");
        var nodeInLayer = MakeNode("Dep.A", NodeKind.Method);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { nodeInLayer }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Svc.Run", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("- [Method] Dep.A", output);
        Assert.DoesNotContain("(", output.Split('\n').First(l => l.Contains("Dep.A")));
    }

    [Fact]
    public void Format_MultipleLayers_AllRendered()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("L1A") }, new List<GraphEdge>()),
            new(2, new List<GraphNode> { MakeNode("L2A"), MakeNode("L2B") }, new List<GraphEdge>()),
            new(3, new List<GraphNode> { MakeNode("L3A") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("## Layer 1 (1 nodes)", output);
        Assert.Contains("## Layer 2 (2 nodes)", output);
        Assert.Contains("## Layer 3 (1 nodes)", output);
        Assert.Contains("Total affected: 4 nodes across 3 layer(s)", output);
    }

    [Fact]
    public void Format_EmptyLayers_ShowsZeroAffected()
    {
        var target = MakeNode("Root");
        var result = new ImpactResult("Root", target, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Contains("Total affected: 0 nodes across 0 layer(s)", output);
    }

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var target = MakeNode("Root");
        var result = new ImpactResult("Root", target, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void Format_PatternAppearsInNoNodesMessage()
    {
        var result = new ImpactResult("Some.Pattern*", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Contains("Some.Pattern*", output);
    }

    [Fact]
    public void Format_MixedNodesWithAndWithoutFiles_RendersCorrectly()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode>
            {
                MakeNode("WithFile", NodeKind.Type, "src/File.cs", 10),
                MakeNode("NoFile", NodeKind.Method)
            }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        var lines = output.Split('\n');
        var withFileLine = lines.First(l => l.Contains("WithFile"));
        var noFileLine = lines.First(l => l.Contains("NoFile"));

        Assert.Contains("(src/File.cs:10)", withFileLine);
        Assert.DoesNotContain("(", noFileLine);
    }
}
