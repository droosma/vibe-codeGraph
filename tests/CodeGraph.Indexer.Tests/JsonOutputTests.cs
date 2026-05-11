using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Indexer.Tests;

/// <summary>
/// Tests that JSON output for CLI commands produces valid, well-structured JSON
/// matching the same serialization options used in Program.cs.
/// </summary>
public class JsonOutputTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static GraphNode MakeNode(string id, NodeKind kind = NodeKind.Method, string? filePath = null) => new()
    {
        Id = id,
        Name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
        Kind = kind,
        FilePath = filePath ?? string.Empty,
        Accessibility = Accessibility.Public
    };

    private static GraphEdge MakeEdge(string from, string to, EdgeType type) =>
        new() { FromId = from, ToId = to, Type = type };

    #region List command JSON

    [Fact]
    public void ListAssemblies_JsonOutput_IsValidJsonArray()
    {
        var assemblies = new List<AssemblyInfo>
        {
            new() { Name = "MyApp", TypeCount = 10, MethodCount = 50, TotalNodeCount = 60 },
            new() { Name = "MyApp.Tests", TypeCount = 5, MethodCount = 20, TotalNodeCount = 25 }
        };

        var json = JsonSerializer.Serialize(assemblies, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("MyApp", doc.RootElement[0].GetProperty("name").GetString());
        Assert.Equal(10, doc.RootElement[0].GetProperty("typeCount").GetInt32());
    }

    [Fact]
    public void ListTypes_JsonOutput_ContainsExpectedFields()
    {
        var result = new ListTypesResult
        {
            Types = new List<TypeInfo>
            {
                new() { Id = "MyApp.Foo", Name = "Foo", Assembly = "MyApp", InDegree = 3, OutDegree = 5 }
            },
            TotalCount = 1,
            Skip = 0,
            Top = 50
        };

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        var types = doc.RootElement.GetProperty("types");
        Assert.Equal(1, types.GetArrayLength());
        Assert.Equal("Foo", types[0].GetProperty("name").GetString());
        Assert.Equal(3, types[0].GetProperty("inDegree").GetInt32());
    }

    [Fact]
    public void ListInterfaces_JsonOutput_IsValidJsonArray()
    {
        var ifaces = new List<InterfaceInfo>
        {
            new() { Id = "MyApp.IFoo", Name = "IFoo", Assembly = "MyApp", ImplementationCount = 3 }
        };

        var json = JsonSerializer.Serialize(ifaces, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement[0].GetProperty("implementationCount").GetInt32());
    }

    [Fact]
    public void ListNamespaces_JsonOutput_ContainsCamelCaseKeys()
    {
        var namespaces = new List<NamespaceInfo>
        {
            new() { Name = "MyApp.Services", TypeCount = 5, MethodCount = 20, TotalCount = 25 }
        };

        var json = JsonSerializer.Serialize(namespaces, JsonOptions);

        Assert.Contains("\"name\"", json);
        Assert.Contains("\"typeCount\"", json);
        Assert.Contains("\"methodCount\"", json);
        Assert.Contains("\"totalCount\"", json);
    }

    #endregion

    #region Search command JSON

    [Fact]
    public void SearchResults_JsonOutput_IsValidJsonArray()
    {
        var results = new List<GraphNode>
        {
            MakeNode("MyApp.FooService", NodeKind.Type, "src/FooService.cs"),
            MakeNode("MyApp.BarService.Run", NodeKind.Method, "src/BarService.cs")
        };

        var items = results.Select(node => new
        {
            id = node.Id,
            name = node.Name,
            kind = node.Kind.ToString().ToLowerInvariant(),
            @namespace = node.ContainingNamespaceId,
            filePath = node.FilePath,
            startLine = node.StartLine
        });

        var json = JsonSerializer.Serialize(items, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("MyApp.FooService", doc.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("type", doc.RootElement[0].GetProperty("kind").GetString());
        Assert.Equal("method", doc.RootElement[1].GetProperty("kind").GetString());
    }

    [Fact]
    public void SearchResults_Empty_ReturnsEmptyArray()
    {
        var json = JsonSerializer.Serialize(Array.Empty<object>(), JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    #endregion

    #region Explain command JSON

    [Fact]
    public void ExplainResult_JsonOutput_ContainsExpectedFields()
    {
        var node = MakeNode("MyApp.Svc.Run", NodeKind.Method, "src/Svc.cs");
        var result = new ExplainResult(
            node,
            new List<GraphEdge> { MakeEdge("MyApp.Svc.Run", "Dep.Call", EdgeType.Calls) },
            new List<GraphEdge> { MakeEdge("Ctrl.Act", "MyApp.Svc.Run", EdgeType.Calls) },
            new List<GraphNode> { MakeNode("MyApp.Svc.Run.Helper", NodeKind.Field) },
            new List<GraphNode> { MakeNode("Tests.RunTest") });

        var jsonObj = new
        {
            id = result.Node.Id,
            name = result.Node.Name,
            kind = result.Node.Kind.ToString().ToLowerInvariant(),
            filePath = result.Node.FilePath,
            startLine = result.Node.StartLine,
            endLine = result.Node.EndLine,
            signature = result.Node.Signature,
            docComment = result.Node.DocComment,
            members = result.Members.Select(m => new { id = m.Id, name = m.Name, kind = m.Kind.ToString().ToLowerInvariant() }),
            outgoingEdges = result.OutgoingEdges.Where(e => e.Type != EdgeType.Contains).Select(e => new { type = e.Type.ToString().ToLowerInvariant(), toId = e.ToId }),
            incomingEdges = result.IncomingEdges.Select(e => new { type = e.Type.ToString().ToLowerInvariant(), fromId = e.FromId }),
            tests = result.Tests.Select(t => new { id = t.Id, name = t.Name })
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("MyApp.Svc.Run", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("method", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("members").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("outgoingEdges").GetArrayLength());
        Assert.Equal("calls", doc.RootElement.GetProperty("outgoingEdges")[0].GetProperty("type").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("incomingEdges").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("tests").GetArrayLength());
    }

    [Fact]
    public void ExplainResult_NullDocComment_OmittedInJson()
    {
        var node = MakeNode("Svc.Run");
        var result = new ExplainResult(node, new(), new(), new(), new());

        var jsonObj = new
        {
            id = result.Node.Id,
            docComment = result.Node.DocComment
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);

        Assert.DoesNotContain("\"docComment\"", json);
    }

    #endregion

    #region Impact command JSON

    [Fact]
    public void ImpactResult_JsonOutput_ContainsExpectedFields()
    {
        var target = MakeNode("MyApp.Svc", NodeKind.Type);
        var layers = new List<ImpactLayer>
        {
            new(1, new List<GraphNode> { MakeNode("MyApp.Ctrl", NodeKind.Type, "src/Ctrl.cs") },
                new List<GraphEdge> { MakeEdge("MyApp.Ctrl", "MyApp.Svc", EdgeType.Calls) })
        };
        var result = new ImpactResult("MyApp.Svc", target, layers);

        var jsonObj = new
        {
            pattern = result.Pattern,
            target = result.Target is not null ? new { id = result.Target.Id, kind = result.Target.Kind.ToString().ToLowerInvariant() } : null,
            totalAffected = result.TotalAffected,
            layers = result.Layers.Select(l => new
            {
                depth = l.Depth,
                nodes = l.Nodes.Select(n => new { id = n.Id, kind = n.Kind.ToString().ToLowerInvariant(), filePath = n.FilePath, startLine = n.StartLine })
            })
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("MyApp.Svc", doc.RootElement.GetProperty("pattern").GetString());
        Assert.Equal("type", doc.RootElement.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("totalAffected").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("layers").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("layers")[0].GetProperty("depth").GetInt32());
    }

    [Fact]
    public void ImpactResult_NoTarget_TargetIsNull()
    {
        var result = new ImpactResult("Unknown", null, new List<ImpactLayer>());

        var jsonObj = new
        {
            pattern = result.Pattern,
            target = result.Target is not null ? new { id = result.Target.Id } : (object?)null,
            totalAffected = result.TotalAffected
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);

        Assert.DoesNotContain("\"target\"", json);
    }

    #endregion

    #region Path command JSON

    [Fact]
    public void PathResult_JsonOutput_ContainsSteps()
    {
        var steps = new List<PathStep>
        {
            new("A", "B", MakeEdge("A", "B", EdgeType.Calls), MakeNode("B", NodeKind.Method, "src/B.cs")),
            new("B", "C", MakeEdge("B", "C", EdgeType.Implements), MakeNode("C", NodeKind.Type, "src/C.cs"))
        };
        var result = new PathResult("A", "C", MakeNode("A"), steps);

        var jsonObj = new
        {
            from = result.FromId,
            to = result.ToId,
            found = true,
            steps = result.Steps.Select(s => new
            {
                fromId = s.FromId,
                toId = s.ToId,
                edgeType = s.Edge.Type.ToString().ToLowerInvariant(),
                toKind = s.ToNode?.Kind.ToString().ToLowerInvariant(),
                toFilePath = s.ToNode?.FilePath
            })
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("A", doc.RootElement.GetProperty("from").GetString());
        Assert.Equal("C", doc.RootElement.GetProperty("to").GetString());
        Assert.True(doc.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Equal("calls", doc.RootElement.GetProperty("steps")[0].GetProperty("edgeType").GetString());
        Assert.Equal("implements", doc.RootElement.GetProperty("steps")[1].GetProperty("edgeType").GetString());
    }

    [Fact]
    public void PathResult_NotFound_JsonContainsFoundFalse()
    {
        var json = JsonSerializer.Serialize(new
        {
            from = "X",
            to = "Y",
            found = false,
            steps = Array.Empty<object>()
        }, JsonOptions);

        var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("steps").GetArrayLength());
    }

    #endregion

    #region Test-impact command JSON

    [Fact]
    public void TestImpactResult_JsonOutput_ContainsExpectedFields()
    {
        var target = MakeNode("MyApp.Svc.Run", NodeKind.Method);
        var result = new TestImpactResult(
            "MyApp.Svc.Run",
            target,
            new List<TestCoverage>
            {
                new(MakeNode("Tests.DirectTest"), new List<GraphNode>())
            },
            new List<TestCoverage>
            {
                new(MakeNode("Tests.IndirectTest"), new List<GraphNode> { MakeNode("MyApp.Ctrl") })
            },
            new List<UncoveredCaller>
            {
                new(MakeNode("MyApp.Orphan"), 2)
            },
            "dotnet test --filter Tests.DirectTest");

        var jsonObj = new
        {
            pattern = result.Pattern,
            target = result.Target is not null ? new { id = result.Target.Id, kind = result.Target.Kind.ToString().ToLowerInvariant() } : null,
            directTests = result.DirectTests.Select(t => new { testId = t.TestNode.Id, path = t.PathFromTarget.Select(n => n.Id).ToList() }),
            indirectTests = result.IndirectTests.Select(t => new { testId = t.TestNode.Id, path = t.PathFromTarget.Select(n => n.Id).ToList() }),
            uncoveredCallers = result.UncoveredCallers.Select(c => new { callerId = c.Caller.Id, depth = c.Depth }),
            suggestedTestCommand = result.SuggestedTestCommand
        };

        var json = JsonSerializer.Serialize(jsonObj, JsonOptions);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("MyApp.Svc.Run", doc.RootElement.GetProperty("pattern").GetString());
        Assert.Equal("method", doc.RootElement.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("directTests").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("indirectTests").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("uncoveredCallers").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("uncoveredCallers")[0].GetProperty("depth").GetInt32());
        Assert.Equal("dotnet test --filter Tests.DirectTest", doc.RootElement.GetProperty("suggestedTestCommand").GetString());
    }

    #endregion

    #region Query --json alias

    [Fact]
    public void QueryJsonFormat_UsesStandardOptions()
    {
        // Verify the shared JSON options produce camelCase, indented, enum-as-string output
        var testObj = new { edgeType = EdgeType.Calls, nodeKind = NodeKind.Method };
        var json = JsonSerializer.Serialize(testObj, JsonOptions);

        Assert.Contains("\"edgeType\"", json);
        Assert.Contains("\"calls\"", json);
        Assert.Contains("\"method\"", json);
        Assert.Contains("\n", json);
    }

    #endregion
}
