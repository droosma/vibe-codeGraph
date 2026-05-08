using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

/// <summary>
/// Mutation-killing tests for ImpactFormatter.
/// Verifies exact output, conditional branches, and formatting.
/// </summary>
public class ImpactFormatterMutationTests
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
    public void Format_NullTarget_ExactMessage()
    {
        var result = new ImpactResult("MyPattern", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Equal("No nodes found matching 'MyPattern'.", output);
    }

    [Fact]
    public void Format_NullTarget_PatternInSingleQuotes()
    {
        var result = new ImpactResult("Abc.Def*", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.Contains("'Abc.Def*'", output);
        Assert.DoesNotContain("\"Abc.Def*\"", output);
    }

    [Fact]
    public void Format_NullTarget_NoOtherContent()
    {
        var result = new ImpactResult("X", null, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        Assert.DoesNotContain("# Impact", output);
        Assert.DoesNotContain("Total affected", output);
        Assert.DoesNotContain("## Layer", output);
    }

    [Fact]
    public void Format_WithTarget_ExactHeaderLine()
    {
        var target = MakeNode("Svc.Run");
        var result = new ImpactResult("Svc.Run", target, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);
        var lines = output.Split('\n');

        Assert.Equal("# Impact analysis: Svc.Run", lines[0].TrimEnd());
    }

    [Fact]
    public void Format_WithTarget_TotalAffectedLine_ExactFormat()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("A") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("Total affected: 1 nodes across 1 layer(s)", output);
    }

    [Fact]
    public void Format_TotalAffectedCount_SumsAcrossLayers()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("A"), MakeNode("B") }, new List<GraphEdge>()),
            new(2, new List<GraphNode> { MakeNode("C"), MakeNode("D"), MakeNode("E") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("Total affected: 5 nodes across 2 layer(s)", output);
    }

    [Fact]
    public void Format_LayerHeader_ExactFormat()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(3, new List<GraphNode> { MakeNode("X"), MakeNode("Y") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("## Layer 3 (2 nodes)", output);
    }

    [Fact]
    public void Format_NodeWithFile_ExactBulletFormat()
    {
        var target = MakeNode("Root");
        var node = MakeNode("Service.Method", NodeKind.Method, "src/Svc.cs", 42);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { node }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        var lines = output.Split('\n');
        var nodeLine = lines.First(l => l.Contains("Service.Method"));
        Assert.Equal("- [Method] Service.Method  (src/Svc.cs:42)", nodeLine.TrimEnd());
    }

    [Fact]
    public void Format_NodeWithoutFile_NoBracketedFilePath()
    {
        var target = MakeNode("Root");
        var node = MakeNode("NoFile.Node", NodeKind.Type);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { node }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        var lines = output.Split('\n');
        var nodeLine = lines.First(l => l.Contains("NoFile.Node"));
        Assert.Equal("- [Type] NoFile.Node", nodeLine.TrimEnd());
    }

    [Fact]
    public void Format_NodeFilePath_HasTwoSpacesBeforeParen()
    {
        var target = MakeNode("Root");
        var node = MakeNode("X", NodeKind.Field, "f.cs", 1);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { node }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        // Two spaces between node.Id and the paren
        Assert.Contains("X  (f.cs:1)", output);
        Assert.DoesNotContain("X (f.cs:1)", output.Replace("X  (", "REPLACED"));
    }

    [Fact]
    public void Format_FilePathStartLine_UsesColon()
    {
        var target = MakeNode("Root");
        var node = MakeNode("N", NodeKind.Method, "path/file.cs", 100);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { node }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("(path/file.cs:100)", output);
    }

    [Fact]
    public void Format_MultipleNodesInLayer_AllRendered()
    {
        var target = MakeNode("Root");
        var nodes = new List<GraphNode>
        {
            MakeNode("A", NodeKind.Type),
            MakeNode("B", NodeKind.Method),
            MakeNode("C", NodeKind.Property)
        };
        var layers = new List<ImpactLayer>
        {
            new(1, nodes, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("- [Type] A", output);
        Assert.Contains("- [Method] B", output);
        Assert.Contains("- [Property] C", output);
    }

    [Fact]
    public void Format_LayerDepth_MatchesProvidedValue()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(7, new List<GraphNode> { MakeNode("Deep") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("## Layer 7 (1 nodes)", output);
        Assert.DoesNotContain("## Layer 1", output);
    }

    [Fact]
    public void Format_OutputIsTrimmed_NoTrailingWhitespace()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("N") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Equal(output, output.TrimEnd());
        Assert.False(output.EndsWith('\n'));
    }

    [Fact]
    public void Format_BlankLineAfterTotalAffected()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("N") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);
        var lines = output.Split('\n');

        // Line 0: header, Line 1: total affected, Line 2: blank
        Assert.Equal("", lines[2].TrimEnd());
    }

    [Fact]
    public void Format_BlankLineAfterEachLayer()
    {
        var target = MakeNode("Root");
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("A") }, new List<GraphEdge>()),
            new(2, new List<GraphNode> { MakeNode("B") }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        // After layer 1's content, there should be a blank line before layer 2
        var layer2Idx = output.IndexOf("## Layer 2");
        var beforeLayer2 = output[..layer2Idx];
        Assert.True(beforeLayer2.TrimEnd().Length < beforeLayer2.Length - 1);
    }

    [Fact]
    public void Format_UsesNodeId_NotNodeName()
    {
        var target = MakeNode("Root");
        var node = new GraphNode
        {
            Id = "Full.Qualified.Id",
            Name = "Id",
            Kind = NodeKind.Method,
            FilePath = "",
            Accessibility = Accessibility.Public
        };
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { node }, new List<GraphEdge>())
        };
        var result = new ImpactResult("Root", target, layers);
        var output = ImpactFormatter.Format(result);

        Assert.Contains("- [Method] Full.Qualified.Id", output);
    }

    [Fact]
    public void Format_TargetIdInHeader_NotPattern()
    {
        var target = MakeNode("Actual.Target.Id");
        var result = new ImpactResult("Wildcard*", target, new List<ImpactLayer>());
        var output = ImpactFormatter.Format(result);

        // Header uses target.Id, not the pattern
        Assert.Contains("# Impact analysis: Actual.Target.Id", output);
        Assert.DoesNotContain("Wildcard*", output);
    }

    [Fact]
    public void Format_AllNodeKinds_InBrackets()
    {
        var target = MakeNode("Root");
        foreach (var kind in Enum.GetValues<NodeKind>())
        {
            var node = MakeNode("N", kind);
            var layers = new List<ImpactLayer>
            {
                new(1, new List<GraphNode> { node }, new List<GraphEdge>())
            };
            var result = new ImpactResult("Root", target, layers);
            var output = ImpactFormatter.Format(result);

            Assert.Contains($"- [{kind}] N", output);
        }
    }
}
