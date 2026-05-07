using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class ExplainFormatterTests
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
    public void Format_BasicNode_ContainsIdAndKindHeader()
    {
        var node = MakeNode("MyApp.Svc.Foo", NodeKind.Method);
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("# MyApp.Svc.Foo", output);
        Assert.Contains("Kind: Method", output);
    }

    [Fact]
    public void Format_NodeWithFile_ContainsFileLineRange()
    {
        var node = MakeNode("Svc.Run", filePath: "src/Svc.cs", startLine: 10, endLine: 25);
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("File: src/Svc.cs:10-25", output);
    }

    [Fact]
    public void Format_NodeWithoutFile_OmitsFileLine()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("File:", output);
    }

    [Fact]
    public void Format_NodeWithSignature_ContainsSignature()
    {
        var node = MakeNode("Svc.Run", signature: "public void Run()");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("Signature: public void Run()", output);
    }

    [Fact]
    public void Format_NodeWithoutSignature_OmitsSignature()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("Signature:", output);
    }

    [Fact]
    public void Format_NodeWithDocComment_ContainsDoc()
    {
        var node = MakeNode("Svc.Run", docComment: "Runs the service.");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("Doc: Runs the service.", output);
    }

    [Fact]
    public void Format_NodeWithoutDocComment_OmitsDoc()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("Doc:", output);
    }

    [Fact]
    public void Format_WithMembers_ShowsMemberSection()
    {
        var node = MakeNode("MyApp.MyClass", NodeKind.Type);
        var members = new List<GraphNode>
        {
            MakeNode("MyApp.MyClass.MethodA", NodeKind.Method),
            MakeNode("MyApp.MyClass.PropB", NodeKind.Property)
        };
        var result = new ExplainResult(node, new(), new(), members, new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Members (2)", output);
        Assert.Contains("- [Method] MethodA", output);
        Assert.Contains("- [Property] PropB", output);
    }

    [Fact]
    public void Format_NoMembers_OmitsMemberSection()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("## Members", output);
    }

    [Fact]
    public void Format_OutgoingEdges_GroupedByType_ExcludesContains()
    {
        var node = MakeNode("Svc.Run");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("Svc.Run", "Svc.Helper", EdgeType.Calls),
            MakeEdge("Svc.Run", "Svc.Member", EdgeType.Contains),
            MakeEdge("Svc.Run", "IFoo", EdgeType.Implements)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Calls (outgoing, 1)", output);
        Assert.Contains("- → Svc.Helper", output);
        Assert.Contains("## Implements (outgoing, 1)", output);
        Assert.Contains("- → IFoo", output);
        Assert.DoesNotContain("## Contains", output);
    }

    [Fact]
    public void Format_IncomingEdges_GroupedByType()
    {
        var node = MakeNode("Svc.Run");
        var incoming = new List<GraphEdge>
        {
            MakeEdge("Controller.Action", "Svc.Run", EdgeType.Calls),
            MakeEdge("OtherSvc.Do", "Svc.Run", EdgeType.Calls)
        };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Calls (incoming, 2)", output);
        Assert.Contains("- ← Controller.Action", output);
        Assert.Contains("- ← OtherSvc.Do", output);
    }

    [Fact]
    public void Format_WithTests_ShowsTestCoverageSection()
    {
        var node = MakeNode("Svc.Run");
        var tests = new List<GraphNode>
        {
            MakeNode("Tests.RunTest1"),
            MakeNode("Tests.RunTest2")
        };
        var result = new ExplainResult(node, new(), new(), new(), tests);
        var output = ExplainFormatter.Format(result);

        Assert.Contains("## Test coverage (2)", output);
        Assert.Contains("- Tests.RunTest1", output);
        Assert.Contains("- Tests.RunTest2", output);
    }

    [Fact]
    public void Format_NoTests_OmitsTestSection()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.DoesNotContain("## Test coverage", output);
    }

    [Fact]
    public void Format_FullNode_AllSectionsPresent()
    {
        var node = MakeNode("MyApp.Svc.Run", NodeKind.Method,
            filePath: "src/Svc.cs", startLine: 10, endLine: 50,
            signature: "public void Run()", docComment: "Runs it.");

        var members = new List<GraphNode> { MakeNode("MyApp.Svc.Run.local", NodeKind.Field) };
        var outgoing = new List<GraphEdge> { MakeEdge("MyApp.Svc.Run", "Dep.Call", EdgeType.Calls) };
        var incoming = new List<GraphEdge> { MakeEdge("Ctrl.Act", "MyApp.Svc.Run", EdgeType.Calls) };
        var tests = new List<GraphNode> { MakeNode("Tests.RunTest") };

        var result = new ExplainResult(node, outgoing, incoming, members, tests);
        var output = ExplainFormatter.Format(result);

        Assert.Contains("# MyApp.Svc.Run", output);
        Assert.Contains("Kind: Method", output);
        Assert.Contains("File: src/Svc.cs:10-50", output);
        Assert.Contains("Signature: public void Run()", output);
        Assert.Contains("Doc: Runs it.", output);
        Assert.Contains("## Members (1)", output);
        Assert.Contains("## Calls (outgoing, 1)", output);
        Assert.Contains("- → Dep.Call", output);
        Assert.Contains("## Calls (incoming, 1)", output);
        Assert.Contains("- ← Ctrl.Act", output);
        Assert.Contains("## Test coverage (1)", output);
        Assert.Contains("- Tests.RunTest", output);
    }

    [Fact]
    public void Format_OutputIsTrimmed()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());
        var output = ExplainFormatter.Format(result);

        Assert.Equal(output, output.TrimEnd());
    }

    [Fact]
    public void Format_MultipleOutgoingEdgeTypes_SortedByKey()
    {
        var node = MakeNode("Svc.Run");
        var outgoing = new List<GraphEdge>
        {
            MakeEdge("Svc.Run", "T1", EdgeType.Inherits),
            MakeEdge("Svc.Run", "T2", EdgeType.Calls)
        };
        var result = new ExplainResult(node, outgoing, new(), new(), new());
        var output = ExplainFormatter.Format(result);

        var callsIdx = output.IndexOf("## Calls (outgoing", StringComparison.Ordinal);
        var inheritsIdx = output.IndexOf("## Inherits (outgoing", StringComparison.Ordinal);
        Assert.True(callsIdx < inheritsIdx, "Calls should come before Inherits (sorted by EdgeType key)");
    }

    [Fact]
    public void Format_MultipleIncomingEdgeTypes_SortedByKey()
    {
        var node = MakeNode("Svc.Run");
        var incoming = new List<GraphEdge>
        {
            MakeEdge("T1", "Svc.Run", EdgeType.References),
            MakeEdge("T2", "Svc.Run", EdgeType.Calls)
        };
        var result = new ExplainResult(node, new(), incoming, new(), new());
        var output = ExplainFormatter.Format(result);

        var callsIdx = output.IndexOf("## Calls (incoming", StringComparison.Ordinal);
        var refsIdx = output.IndexOf("## References (incoming", StringComparison.Ordinal);
        Assert.True(callsIdx < refsIdx, "Calls should come before References (sorted by EdgeType key)");
    }
}
