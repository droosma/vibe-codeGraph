using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class PathFormatterTests
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
    public void Format_Header_ContainsFromAndToIds()
    {
        var result = new PathResult("A", "B", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("# Path: A → B", output);
        Assert.Contains("Steps: 0", output);
    }

    [Fact]
    public void Format_ZeroSteps_ShowsFromIdOnly()
    {
        var result = new PathResult("A.Start", "B.End", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("Steps: 0", output);
        Assert.Contains("A.Start", output);
    }

    [Fact]
    public void Format_FromNodeWithKindAndFile_ShowsAnnotatedFromNode()
    {
        var fromNode = MakeNode("Svc.Run", NodeKind.Method, "src/Svc.cs", 42);
        var result = new PathResult("Svc.Run", "Target", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("[Method] Svc.Run", output);
        Assert.Contains("File: src/Svc.cs:42", output);
    }

    [Fact]
    public void Format_FromNodeNull_ShowsBareFromId()
    {
        var result = new PathResult("Svc.Run", "Target", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("  Svc.Run", output);
        Assert.DoesNotContain("[Method]", output);
    }

    [Fact]
    public void Format_FromNodeWithoutFile_OmitsFileLine()
    {
        var fromNode = MakeNode("Svc.Run", NodeKind.Method);
        var result = new PathResult("Svc.Run", "Target", fromNode, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Contains("[Method] Svc.Run", output);
        Assert.DoesNotContain("File:", output);
    }

    [Fact]
    public void Format_SingleStep_WithToNode_ShowsEdgeAndNodeAnnotation()
    {
        var fromNode = MakeNode("A", NodeKind.Method, "a.cs", 1);
        var toNode = MakeNode("B", NodeKind.Type, "b.cs", 10);
        var edge = MakeEdge("A", "B", EdgeType.Calls);
        var step = new PathStep("A", "B", edge, toNode);

        var result = new PathResult("A", "B", fromNode, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        Assert.Contains("--Calls-->", output);
        Assert.Contains("[Type] B", output);
        Assert.Contains("File: b.cs:10", output);
    }

    [Fact]
    public void Format_SingleStep_WithoutToNode_ShowsBareToId()
    {
        var fromNode = MakeNode("A");
        var edge = MakeEdge("A", "B.Missing", EdgeType.DependsOn);
        var step = new PathStep("A", "B.Missing", edge, null);

        var result = new PathResult("A", "B.Missing", fromNode, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        Assert.Contains("--DependsOn-->", output);
        Assert.Contains("  B.Missing", output);
        Assert.DoesNotContain("[Method] B.Missing", output);
    }

    [Fact]
    public void Format_MultipleSteps_AllEdgeTypesRendered()
    {
        var fromNode = MakeNode("A");
        var midNode = MakeNode("B", NodeKind.Type, "b.cs", 5);
        var endNode = MakeNode("C", NodeKind.Property, "c.cs", 20);

        var steps = new List<PathStep>
        {
            new("A", "B", MakeEdge("A", "B", EdgeType.Calls), midNode),
            new("B", "C", MakeEdge("B", "C", EdgeType.References), endNode)
        };

        var result = new PathResult("A", "C", fromNode, steps);
        var output = PathFormatter.Format(result);

        Assert.Contains("Steps: 2", output);
        Assert.Contains("--Calls-->", output);
        Assert.Contains("--References-->", output);
        Assert.Contains("[Type] B", output);
        Assert.Contains("[Property] C", output);
    }

    [Fact]
    public void Format_StepToNodeWithoutFile_OmitsFileForThatStep()
    {
        var fromNode = MakeNode("A", NodeKind.Method, "a.cs", 1);
        var toNode = MakeNode("B", NodeKind.Type);
        var step = new PathStep("A", "B", MakeEdge("A", "B", EdgeType.Inherits), toNode);

        var result = new PathResult("A", "B", fromNode, new List<PathStep> { step });
        var output = PathFormatter.Format(result);

        Assert.Contains("[Type] B", output);
        var lines = output.Split('\n');
        var bLine = Array.FindIndex(lines, l => l.Contains("[Type] B"));
        // Next line should not be a File: line
        if (bLine + 1 < lines.Length)
            Assert.DoesNotContain("File:", lines[bLine + 1]);
    }

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var result = new PathResult("A", "B", null, new List<PathStep>());
        var output = PathFormatter.Format(result);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void Format_StepsCount_MatchesActualSteps()
    {
        var steps = new List<PathStep>
        {
            new("A", "B", MakeEdge("A", "B", EdgeType.Calls), null),
            new("B", "C", MakeEdge("B", "C", EdgeType.Calls), null),
            new("C", "D", MakeEdge("C", "D", EdgeType.Calls), null)
        };
        var result = new PathResult("A", "D", null, steps);
        var output = PathFormatter.Format(result);

        Assert.Contains("Steps: 3", output);
    }

    [Fact]
    public void Format_AllEdgeTypes_RenderedCorrectly()
    {
        var edgeTypes = new[] { EdgeType.Calls, EdgeType.Inherits, EdgeType.Implements, EdgeType.DependsOn };
        foreach (var edgeType in edgeTypes)
        {
            var step = new PathStep("A", "B", MakeEdge("A", "B", edgeType), null);
            var result = new PathResult("A", "B", null, new List<PathStep> { step });
            var output = PathFormatter.Format(result);

            Assert.Contains($"--{edgeType}-->", output);
        }
    }
}
