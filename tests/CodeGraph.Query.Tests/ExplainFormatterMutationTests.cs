using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

/// <summary>
/// Mutation-killing tests for ExplainFormatter.
/// Verifies exact output strings, line ordering, and all code paths.
/// </summary>
public class ExplainFormatterMutationTests
{
    private static GraphNode MakeNode(
        string id,
        NodeKind kind = NodeKind.Method,
        string? filePath = null,
        int startLine = 0,
        int endLine = 0,
        string? signature = null,
        string? docComment = null) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        FilePath = filePath ?? string.Empty,
        StartLine = startLine,
        EndLine = endLine,
        Signature = signature ?? string.Empty,
        DocComment = docComment,
        Accessibility = Accessibility.Public
    };

    private static GraphEdge MakeEdge(string from, string to, EdgeType type) =>
        new() { FromId = from, ToId = to, Type = type };

    [Fact]
    public void Format_ExactHeaderLine_StartsWithHashSpace()
    {
        var node = MakeNode("X.Y");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);
        var lines = output.Split('\n');

        Assert.Equal("# X.Y", lines[0].TrimEnd());
    }

    [Fact]
    public void Format_ExactKindLine_SecondLine()
    {
        var node = MakeNode("Foo", NodeKind.Property);
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);
        var lines = output.Split('\n');

        Assert.Equal("Kind: Property", lines[1].TrimEnd());
    }

    [Fact]
    public void Format_FileLineFormat_ExactColonDashFormat()
    {
        var node = MakeNode("A", filePath: "dir/file.cs", startLine: 5, endLine: 20);
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("File: dir/file.cs:5-20", output);
        // Verify it uses colon between path and startLine, dash between start and end
        Assert.DoesNotContain("File: dir/file.cs:5:20", output);
        Assert.DoesNotContain("File: dir/file.cs 5-20", output);
    }

    [Fact]
    public void Format_SignatureLine_ExactPrefix()
    {
        var node = MakeNode("M", signature: "void M()");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        var lines = output.Split('\n');
        var sigLine = lines.First(l => l.Contains("Signature:"));
        Assert.Equal("Signature: void M()", sigLine.TrimEnd());
    }

    [Fact]
    public void Format_DocCommentLine_ExactPrefix()
    {
        var node = MakeNode("M", docComment: "Hello world");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        var lines = output.Split('\n');
        var docLine = lines.First(l => l.Contains("Doc:"));
        Assert.Equal("Doc: Hello world", docLine.TrimEnd());
    }

    [Fact]
    public void Format_MemberLine_ExactFormat_BracketKindSpaceName()
    {
        var node = MakeNode("MyClass", NodeKind.Type);
        var member = MakeNode("MyClass.DoWork", NodeKind.Method);
        var result = new ExplainResult(node, new(), new(), new List<GraphNode> { member }, new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("- [Method] DoWork", output);
        // Verify bracket format exactly
        Assert.DoesNotContain("- (Method) DoWork", output);
        Assert.DoesNotContain("-[Method]", output);
    }

    [Fact]
    public void Format_MembersHeader_ExactCountInParentheses()
    {
        var node = MakeNode("T", NodeKind.Type);
        var members = new List<GraphNode>
        {
            MakeNode("T.A", NodeKind.Method),
            MakeNode("T.B", NodeKind.Property),
            MakeNode("T.C", NodeKind.Field)
        };
        var result = new ExplainResult(node, new(), new(), members, new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Members (3)", output);
        Assert.DoesNotContain("## Members (2)", output);
        Assert.DoesNotContain("## Members (4)", output);
    }

    [Fact]
    public void Format_OutgoingEdge_ExactArrowFormat()
    {
        var node = MakeNode("A");
        var outgoing = new List<GraphEdge> { MakeEdge("A", "B.Target", EdgeType.Calls) };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        // Exact format: "- → <toId>"
        Assert.Contains("- → B.Target", output);
        Assert.DoesNotContain("- -> B.Target", output);
        Assert.DoesNotContain("→ B.Target", output.Replace("- → ", ""));
    }

    [Fact]
    public void Format_IncomingEdge_ExactArrowFormat()
    {
        var node = MakeNode("A");
        var incoming = new List<GraphEdge> { MakeEdge("B.Caller", "A", EdgeType.Calls) };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        // Exact format: "- ← <fromId>"
        Assert.Contains("- ← B.Caller", output);
        Assert.DoesNotContain("- <- B.Caller", output);
    }

    [Fact]
    public void Format_OutgoingHeader_ExactFormat_WithCount()
    {
        var node = MakeNode("A");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("A", "B", EdgeType.References),
            MakeEdge("A", "C", EdgeType.References)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## References (outgoing, 2)", output);
    }

    [Fact]
    public void Format_IncomingHeader_ExactFormat_WithCount()
    {
        var node = MakeNode("A");
        var incoming = new List<GraphEdge>
        {
            MakeEdge("X", "A", EdgeType.Inherits)
        };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Inherits (incoming, 1)", output);
    }

    [Fact]
    public void Format_TestCoverageHeader_ExactFormat()
    {
        var node = MakeNode("A");
        var tests = new List<GraphNode>
        {
            MakeNode("T1"),
            MakeNode("T2"),
            MakeNode("T3")
        };
        var result = new ExplainResult(node, new(), new(), new(), tests);
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Test coverage (3)", output);
    }

    [Fact]
    public void Format_TestEntry_UsesId()
    {
        var node = MakeNode("A");
        var tests = new List<GraphNode> { MakeNode("Tests.MyTest") };
        var result = new ExplainResult(node, new(), new(), new(), tests);
        var output = ExplainFormatter.Format(result);

        Assert.Contains("- Tests.MyTest", output);
    }

    [Fact]
    public void Format_ContainsEdge_ExcludedFromOutgoing()
    {
        var node = MakeNode("Parent");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("Parent", "Child", EdgeType.Contains)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        // No outgoing section should appear since Contains is excluded
        Assert.DoesNotContain("(outgoing,", output);
        Assert.DoesNotContain("→", output);
    }

    [Fact]
    public void Format_ContainsEdgeMixedWithOthers_OnlyNonContainsShown()
    {
        var node = MakeNode("Parent");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("Parent", "Child", EdgeType.Contains),
            MakeEdge("Parent", "Dep", EdgeType.DependsOn)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## DependsOn (outgoing, 1)", output);
        Assert.Contains("- → Dep", output);
        Assert.DoesNotContain("## Contains", output);
    }

    [Fact]
    public void Format_EmptyOutgoingAndIncoming_NoEdgeSections()
    {
        var node = MakeNode("Lonely");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("outgoing", output);
        Assert.DoesNotContain("incoming", output);
        Assert.DoesNotContain("→", output);
        Assert.DoesNotContain("←", output);
    }

    [Fact]
    public void Format_MultipleOutgoingEdgesInSameGroup_AllRendered()
    {
        var node = MakeNode("A");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("A", "X", EdgeType.Calls),
            MakeEdge("A", "Y", EdgeType.Calls),
            MakeEdge("A", "Z", EdgeType.Calls)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Calls (outgoing, 3)", output);
        Assert.Contains("- → X", output);
        Assert.Contains("- → Y", output);
        Assert.Contains("- → Z", output);
    }

    [Fact]
    public void Format_MultipleIncomingEdgesInSameGroup_AllRendered()
    {
        var node = MakeNode("A");
        var incoming = new List<GraphEdge>
        {
            MakeEdge("P", "A", EdgeType.DependsOn),
            MakeEdge("Q", "A", EdgeType.DependsOn)
        };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## DependsOn (incoming, 2)", output);
        Assert.Contains("- ← P", output);
        Assert.Contains("- ← Q", output);
    }

    [Fact]
    public void Format_AllNodeKinds_RenderedInKindLine()
    {
        foreach (var kind in Enum.GetValues<NodeKind>())
        {
            var node = MakeNode("Test", kind);
            var result = new ExplainResult(node, new(), new(), new(), new());
            var output = ExplainFormatter.Format(result);

            Assert.Contains($"Kind: {kind}", output);
        }
    }

    [Fact]
    public void Format_EmptySignature_OmitsSignatureLine()
    {
        var node = MakeNode("A");
        node = node with { Signature = "" };
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("Signature:", output);
    }

    [Fact]
    public void Format_NullDocComment_OmitsDocLine()
    {
        var node = MakeNode("A");
        node = node with { DocComment = null };
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("Doc:", output);
    }

    [Fact]
    public void Format_EmptyDocComment_OmitsDocLine()
    {
        var node = MakeNode("A");
        node = node with { DocComment = "" };
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("Doc:", output);
    }

    [Fact]
    public void Format_EmptyFilePath_OmitsFileLine()
    {
        var node = MakeNode("A");
        node = node with { FilePath = "" };
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("File:", output);
    }

    [Fact]
    public void Format_BlankSectionsHaveTrailingBlankLine()
    {
        var node = MakeNode("A");
        var members = new List<GraphNode> { MakeNode("A.M", NodeKind.Field) };
        var outgoing = new List<GraphEdge> { MakeEdge("A", "B", EdgeType.Calls) };
        var result = new ExplainResult(node, outgoing, new(), members, new());
        var output = ExplainFormatter.Format(result);

        // After members section there should be a blank line
        var lines = output.Split('\n');
        var memberHeaderIdx = Array.FindIndex(lines, l => l.StartsWith("## Members"));
        var memberItemIdx = memberHeaderIdx + 1;
        // Next line after member item(s) should be blank
        var afterMembers = memberItemIdx + 1;
        Assert.Equal("", lines[afterMembers].TrimEnd());
    }

    [Fact]
    public void Format_IncomingContainsEdge_NotFiltered()
    {
        // Contains edges are only filtered from outgoing, not incoming
        var node = MakeNode("Child");
        var incoming = new List<GraphEdge>
        {
            MakeEdge("Parent", "Child", EdgeType.Contains)
        };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Contains (incoming, 1)", output);
        Assert.Contains("- ← Parent", output);
    }

    [Fact]
    public void Format_MemberName_NotFullId()
    {
        var node = MakeNode("Ns.Class", NodeKind.Type);
        var member = MakeNode("Ns.Class.Method1", NodeKind.Method);
        var result = new ExplainResult(node, new(), new(), new List<GraphNode> { member }, new());
        var output = ExplainFormatter.Format(result);

        // Member display uses Name property, not Id
        Assert.Contains("- [Method] Method1", output);
        Assert.DoesNotContain("- [Method] Ns.Class.Method1", output);
    }

    [Fact]
    public void Format_OrderOfSections_HeaderThenMembersThenOutgoingThenIncomingThenTests()
    {
        var node = MakeNode("A", NodeKind.Type, "f.cs", 1, 10, "sig", "doc");
        var members = new List<GraphNode> { MakeNode("A.M", NodeKind.Method) };
        var outgoing = new List<GraphEdge> { MakeEdge("A", "Out", EdgeType.Calls) };
        var incoming = new List<GraphEdge> { MakeEdge("In", "A", EdgeType.References) };
        var tests = new List<GraphNode> { MakeNode("T") };
        var result = new ExplainResult(node, outgoing, incoming, members, tests);
        var output = ExplainFormatter.Format(result);

        var membersIdx = output.IndexOf("## Members");
        var outIdx = output.IndexOf("(outgoing,");
        var inIdx = output.IndexOf("(incoming,");
        var testIdx = output.IndexOf("## Test coverage");

        Assert.True(membersIdx < outIdx, "Members before outgoing");
        Assert.True(outIdx < inIdx, "Outgoing before incoming");
        Assert.True(inIdx < testIdx, "Incoming before tests");
    }

    [Fact]
    public void Format_AllEdgeTypes_InOutgoing_UseCorrectLabel()
    {
        var node = MakeNode("A");
        var allTypes = new[]
        {
            EdgeType.Calls, EdgeType.Inherits, EdgeType.Implements,
            EdgeType.DependsOn, EdgeType.ResolvesTo, EdgeType.Covers,
            EdgeType.CoveredBy, EdgeType.References, EdgeType.Overrides
        };

        foreach (var et in allTypes)
        {
            var outgoing = new List<GraphEdge> { MakeEdge("A", "B", et) };
            var result = new ExplainResult(node, outgoing, new(), new(), new());
            var output = ExplainFormatter.Format(result);

            Assert.Contains($"## {et} (outgoing, 1)", output);
        }
    }
}
