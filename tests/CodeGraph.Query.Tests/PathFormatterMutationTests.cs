using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

/// <summary>
/// Mutation-killing tests for PathFormatter.
/// Verifies exact output strings, line structure, and edge cases.
/// </summary>
public class PathFormatterMutationTests
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

    private static GraphEdge MakeEdge(string from, string to, EdgeType type) =>
        new() { FromId = from, ToId = to, Type = type };

    [Fact]
    public void Format_HeaderLine_ExactFormat()
    {
        var result = new PathResult("Start.Node", "End.Node", null, new List<PathStep>());
        var output = PathFormatter.Format(result);
        var lines = output.Split('\n');

        Assert.Equal("# Path: Start.Node → End.Node", lines[0].TrimEnd());
    }

    [Fact]
    public void Format_StepsLine_ExactFormat()
    {
        var steps = new List<PathStep>
        {
            new("A", "B", MakeEdge("A", "B", EdgeType.Calls), null),
            new("B", "C", MakeEdge("B", "C", EdgeType.Calls), null)
        };
        var result = new PathResult("A", "C", null, steps);
        var output = PathFormatter.Format(result);
        var lines = output.Split('\n');

        Assert.Equal("Steps: 2", lines[1].TrimEnd());
    }

    [Fact]
    public void Format_FromNodeWithKind_ExactTwoSpaceIndent()
    {
        var fromNode = MakeNode("My.Class", NodeKind.Type, "src/x.cs", 7);
        var result = new PathResult("My.Class", "Target", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("  [Type] My.Class", output);
    }

    [Fact]
    public void Format_FromNodeFile_ExactFourSpaceIndent()
    {
        var fromNode = MakeNode("My.Class", NodeKind.Type, "src/x.cs", 7);
        var result = new PathResult("My.Class", "Target", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("    File: src/x.cs:7", output);
    }

    [Fact]
    public void Format_FromNodeNull_BareIdWithTwoSpaces()
    {
        var result = new PathResult("Bare.Id", "Target", null, new List<PathStep>());
        var output = PathFormatter.Format(result);
        var lines = output.Split('\n');

        // Skip the header line and find the indented bare id line
        var bareLine = lines.First(l => l.TrimEnd() == "  Bare.Id");
        Assert.Equal("  Bare.Id", bareLine.TrimEnd());
    }

    [Fact]
    public void Format_StepEdge_ExactFourSpaceIndentAndTrailingSpace()
    {
        var step = new PathStep("A", "B", MakeEdge("A", "B", EdgeType.Inherits), null);
        var result = new PathResult("A", "B", null, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        // The edge line has 4 spaces, --, type, -->, and trailing space
        Assert.Contains("    --Inherits--> ", output);
    }

    [Fact]
    public void Format_StepToNode_ExactTwoSpaceIndent()
    {
        var toNode = MakeNode("B.Class", NodeKind.Type, "b.cs", 99);
        var step = new PathStep("A", "B.Class", MakeEdge("A", "B.Class", EdgeType.Calls), toNode);
        var result = new PathResult("A", "B.Class", null, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        Assert.Contains("  [Type] B.Class", output);
        Assert.Contains("    File: b.cs:99", output);
    }

    [Fact]
    public void Format_StepToNodeNull_BareToIdWithTwoSpaces()
    {
        var step = new PathStep("A", "B.Missing", MakeEdge("A", "B.Missing", EdgeType.Calls), null);
        var result = new PathResult("A", "B.Missing", null, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        var lines = output.Split('\n');
        var missingLine = lines.First(l => l.TrimEnd() == "  B.Missing");
        Assert.Equal("  B.Missing", missingLine.TrimEnd());
    }

    [Fact]
    public void Format_StepToNodeWithoutFile_NoFileLineAfterNode()
    {
        var toNode = MakeNode("B", NodeKind.Method); // no file
        var step = new PathStep("A", "B", MakeEdge("A", "B", EdgeType.Calls), toNode);
        var result = new PathResult("A", "B", null, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        Assert.Contains("  [Method] B", output);
        // No File: line anywhere for step without file
        var lines = output.Split('\n');
        var nodeLineIdx = Array.FindIndex(lines, l => l.Contains("[Method] B"));
        if (nodeLineIdx + 1 < lines.Length)
            Assert.DoesNotContain("File:", lines[nodeLineIdx + 1]);
    }

    [Fact]
    public void Format_MultipleSteps_EachStepHasEdgeAndNode()
    {
        var n1 = MakeNode("Mid", NodeKind.Property, "m.cs", 5);
        var n2 = MakeNode("End", NodeKind.Field);
        var steps = new List<PathStep>
        {
            new("Start", "Mid", MakeEdge("Start", "Mid", EdgeType.Calls), n1),
            new("Mid", "End", MakeEdge("Mid", "End", EdgeType.References), n2)
        };
        var result = new PathResult("Start", "End", null, steps);
        var output = PathFormatter.Format(result);

        Assert.Contains("    --Calls--> ", output);
        Assert.Contains("  [Property] Mid", output);
        Assert.Contains("    File: m.cs:5", output);
        Assert.Contains("    --References--> ", output);
        Assert.Contains("  [Field] End", output);
    }

    [Fact]
    public void Format_HeaderUsesUnicodeArrow_NotAscii()
    {
        var result = new PathResult("X", "Y", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("→", output); // Unicode arrow in header
        Assert.DoesNotContain("->", output.Split('\n')[0]); // Not ASCII arrow
    }

    [Fact]
    public void Format_EdgeTypeRendered_InDoubleDashes()
    {
        foreach (var et in Enum.GetValues<EdgeType>())
        {
            var step = new PathStep("A", "B", MakeEdge("A", "B", et), null);
            var result = new PathResult("A", "B", null, new List<PathStep> { step });
            var output = PathFormatter.Format(result);

            Assert.Contains($"--{et}-->", output);
        }
    }

    [Fact]
    public void Format_BlankLineAfterStepsCount()
    {
        var result = new PathResult("A", "B", null, new List<PathStep>());
        var output = PathFormatter.Format(result);
        var lines = output.Split('\n');

        // Line 0: header, Line 1: steps, Line 2: blank
        Assert.Equal("", lines[2].TrimEnd());
    }

    [Fact]
    public void Format_FromNodeKindBracket_ExactForAllKinds()
    {
        foreach (var kind in Enum.GetValues<NodeKind>())
        {
            var fromNode = MakeNode("Node", kind, "f.cs", 1);
            var result = new PathResult("Node", "T", fromNode, new List<PathStep>());
            var output = PathFormatter.Format(result);

            Assert.Contains($"[{kind}] Node", output);
        }
    }

    [Fact]
    public void Format_ToNodeKindBracket_ExactForAllKinds()
    {
        foreach (var kind in Enum.GetValues<NodeKind>())
        {
            var toNode = MakeNode("Target", kind, "t.cs", 1);
            var step = new PathStep("A", "Target", MakeEdge("A", "Target", EdgeType.Calls), toNode);
            var result = new PathResult("A", "Target", null, new List<PathStep> { step });
            var output = PathFormatter.Format(result);

            Assert.Contains($"[{kind}] Target", output);
        }
    }

    [Fact]
    public void Format_FileLineUsesColon_NotOtherSeparator()
    {
        var fromNode = MakeNode("A", NodeKind.Method, "path/to/file.cs", 42);
        var result = new PathResult("A", "B", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("File: path/to/file.cs:42", output);
        Assert.DoesNotContain("File: path/to/file.cs 42", output);
    }

    [Fact]
    public void Format_FromId_UsedInHeader_NotFromNodeId()
    {
        // Even if fromNode has a different Id (shouldn't normally happen), header uses result.FromId
        var fromNode = MakeNode("Different.Id", NodeKind.Method);
        var result = new PathResult("Header.From", "Header.To", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("# Path: Header.From → Header.To", output);
    }

    [Fact]
    public void Format_StepCount_ReflectsListCount()
    {
        var steps = new List<PathStep>
        {
            new("A", "B", MakeEdge("A", "B", EdgeType.Calls), null),
            new("B", "C", MakeEdge("B", "C", EdgeType.Calls), null),
            new("C", "D", MakeEdge("C", "D", EdgeType.Calls), null),
            new("D", "E", MakeEdge("D", "E", EdgeType.Calls), null),
            new("E", "F", MakeEdge("E", "F", EdgeType.Calls), null),
        };
        var result = new PathResult("A", "F", null, steps);
        var output = PathFormatter.Format(result);

        Assert.Contains("Steps: 5", output);
    }

    [Fact]
    public void Format_OutputDoesNotEndWithNewline()
    {
        var result = new PathResult("A", "B", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.False(output.EndsWith('\n'));
        Assert.False(output.EndsWith("\r\n"));
    }
}
