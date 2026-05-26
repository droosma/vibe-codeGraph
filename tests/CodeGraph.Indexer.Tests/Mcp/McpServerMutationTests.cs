using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Mcp;

namespace CodeGraph.Indexer.Tests.Mcp;

/// <summary>
/// Additional mutation-killing tests for McpServer.
/// Targets: tool dispatch routing, error message specifics, 
/// CreateToolResult isError values, HandleListCallAsync scope routing,
/// query format dispatch, mode parsing, confidence parsing.
/// </summary>
public class McpServerMutationTests : IDisposable
{
    private readonly string _graphDir;

    public McpServerMutationTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerMutationTests).Assembly.Location)!,
            "McpMutTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    private McpServer CreateServer() => new(_graphDir);

    private static JsonNode MakeRequest(string method, JsonNode? id = null, JsonNode? @params = null)
    {
        var msg = new JsonObject { ["method"] = method };
        if (id is not null)
            msg["id"] = id.DeepClone();
        if (@params is not null)
            msg["params"] = @params.DeepClone();
        return msg;
    }

    private async Task WriteGraphDataAsync(
        List<GraphNode>? nodes = null,
        List<GraphEdge>? edges = null,
        GraphMetadata? metadata = null)
    {
        nodes ??=
        [
            new()
            {
                Id = "App.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                FilePath = "src/Service.cs",
                StartLine = 1,
                EndLine = 50,
                Signature = "App.Service",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "App.Service.Execute()",
                Name = "Execute",
                Kind = NodeKind.Method,
                FilePath = "src/Service.cs",
                StartLine = 5,
                EndLine = 20,
                Signature = "App.Service.Execute()",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingTypeId = "App.Service",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "App.IRepo",
                Name = "IRepo",
                Kind = NodeKind.Type,
                FilePath = "src/IRepo.cs",
                StartLine = 1,
                EndLine = 5,
                Signature = "App.IRepo",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Interface" }
            },
            new()
            {
                Id = "App",
                Name = "App",
                Kind = NodeKind.Namespace,
                FilePath = string.Empty,
                Signature = "App",
                Accessibility = Accessibility.Public,
                AssemblyName = "App"
            }
        ];

        edges ??=
        [
            new()
            {
                FromId = "App.Service.Execute()",
                ToId = "App.IRepo",
                Type = EdgeType.DependsOn,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "App.Service",
                ToId = "App.Service.Execute()",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "App",
                ToId = "App.Service",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "App",
                ToId = "App.IRepo",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            }
        ];

        metadata ??= new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "def456",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "0.1.0",
            Solution = "app.sln",
            SolutionName = "app"
        };

        var writer = new GraphWriter();
        await writer.WriteAsync(_graphDir, nodes, edges, metadata);
    }

    private static (List<GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Metadata) CreateRichGraph(string commitHash = "def456")
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "App",
                Name = "App",
                Kind = NodeKind.Namespace,
                FilePath = string.Empty,
                Signature = "App",
                Accessibility = Accessibility.Public,
                AssemblyName = "App"
            },
            new()
            {
                Id = "Tests",
                Name = "Tests",
                Kind = NodeKind.Namespace,
                FilePath = string.Empty,
                Signature = "Tests",
                Accessibility = Accessibility.Public,
                AssemblyName = "Tests"
            },
            new()
            {
                Id = "App.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                FilePath = "src/Service.cs",
                StartLine = 1,
                EndLine = 50,
                Signature = "App.Service",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "App.Service.Execute()",
                Name = "Execute",
                Kind = NodeKind.Method,
                FilePath = "src/Service.cs",
                StartLine = 5,
                EndLine = 20,
                Signature = "App.Service.Execute()",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingTypeId = "App.Service",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "App.Controller",
                Name = "Controller",
                Kind = NodeKind.Type,
                FilePath = "src/Controller.cs",
                StartLine = 1,
                EndLine = 40,
                Signature = "App.Controller",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "App.Controller.Handle()",
                Name = "Handle",
                Kind = NodeKind.Method,
                FilePath = "src/Controller.cs",
                StartLine = 10,
                EndLine = 20,
                Signature = "App.Controller.Handle()",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingTypeId = "App.Controller",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "App.IRepo",
                Name = "IRepo",
                Kind = NodeKind.Type,
                FilePath = "src/IRepo.cs",
                StartLine = 1,
                EndLine = 5,
                Signature = "App.IRepo",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Interface" }
            },
            new()
            {
                Id = "App.SqlRepo",
                Name = "SqlRepo",
                Kind = NodeKind.Type,
                FilePath = "src/SqlRepo.cs",
                StartLine = 1,
                EndLine = 25,
                Signature = "App.SqlRepo",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "App.UnknownDep",
                Name = "UnknownDep",
                Kind = NodeKind.Type,
                FilePath = "src/UnknownDep.cs",
                StartLine = 1,
                EndLine = 5,
                Signature = "App.UnknownDep",
                Accessibility = Accessibility.Public,
                AssemblyName = "App",
                ContainingNamespaceId = "App",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "Tests.ServiceTests",
                Name = "ServiceTests",
                Kind = NodeKind.Type,
                FilePath = "tests/ServiceTests.cs",
                StartLine = 1,
                EndLine = 20,
                Signature = "Tests.ServiceTests",
                Accessibility = Accessibility.Public,
                AssemblyName = "Tests",
                ContainingNamespaceId = "Tests",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "Tests.ServiceTests.Execute_is_covered()",
                Name = "Execute_is_covered",
                Kind = NodeKind.Method,
                FilePath = "tests/ServiceTests.cs",
                StartLine = 5,
                EndLine = 10,
                Signature = "Tests.ServiceTests.Execute_is_covered()",
                Accessibility = Accessibility.Public,
                AssemblyName = "Tests",
                ContainingTypeId = "Tests.ServiceTests",
                ContainingNamespaceId = "Tests",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "Tests.ControllerTests",
                Name = "ControllerTests",
                Kind = NodeKind.Type,
                FilePath = "tests/ControllerTests.cs",
                StartLine = 1,
                EndLine = 20,
                Signature = "Tests.ControllerTests",
                Accessibility = Accessibility.Public,
                AssemblyName = "Tests",
                ContainingNamespaceId = "Tests",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "Tests.ControllerTests.Handle_is_covered()",
                Name = "Handle_is_covered",
                Kind = NodeKind.Method,
                FilePath = "tests/ControllerTests.cs",
                StartLine = 5,
                EndLine = 10,
                Signature = "Tests.ControllerTests.Handle_is_covered()",
                Accessibility = Accessibility.Public,
                AssemblyName = "Tests",
                ContainingTypeId = "Tests.ControllerTests",
                ContainingNamespaceId = "Tests",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "OtherApp.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                FilePath = "other/Service.cs",
                StartLine = 1,
                EndLine = 12,
                Signature = "OtherApp.Service",
                Accessibility = Accessibility.Public,
                AssemblyName = "OtherApp",
                ContainingNamespaceId = "OtherApp",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "App", ToId = "App.Service", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.Controller", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.IRepo", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.SqlRepo", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.UnknownDep", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service", ToId = "App.Service.Execute()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Controller", ToId = "App.Controller.Handle()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "Tests", ToId = "Tests.ServiceTests", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "Tests", ToId = "Tests.ControllerTests", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "Tests.ServiceTests", ToId = "Tests.ServiceTests.Execute_is_covered()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "Tests.ControllerTests", ToId = "Tests.ControllerTests.Handle_is_covered()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service.Execute()", ToId = "App.IRepo", Type = EdgeType.DependsOn, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service.Execute()", ToId = "App.UnknownDep", Type = EdgeType.DependsOn, Confidence = EdgeConfidence.Unresolved },
            new() { FromId = "App.SqlRepo", ToId = "App.IRepo", Type = EdgeType.Implements, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Controller.Handle()", ToId = "App.Service.Execute()", Type = EdgeType.Calls, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service.Execute()", ToId = "Tests.ServiceTests.Execute_is_covered()", Type = EdgeType.CoveredBy, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Controller.Handle()", ToId = "Tests.ControllerTests.Handle_is_covered()", Type = EdgeType.CoveredBy, Confidence = EdgeConfidence.Verified },
            new()
            {
                FromId = "App.Service.Execute()",
                ToId = "Newtonsoft.Json.JsonConvert",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "Newtonsoft.Json/13.0.1",
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "App.SqlRepo",
                ToId = "Newtonsoft.Json.Linq.JObject",
                Type = EdgeType.DependsOn,
                IsExternal = true,
                PackageSource = "Newtonsoft.Json/13.0.1",
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "OtherApp.Service",
                ToId = "Newtonsoft.Json.Linq.JObject",
                Type = EdgeType.DependsOn,
                IsExternal = true,
                PackageSource = "Newtonsoft.Json/12.0.3",
                Confidence = EdgeConfidence.Verified
            }
        };

        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "0.1.0",
            Solution = "app.sln",
            SolutionName = "app"
        };

        return (nodes, edges, metadata);
    }

    // ── Unknown tool returns error with correct message ──

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsErrorWithToolName()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "nonexistent_tool",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var error = response!["error"];
        Assert.NotNull(error);
        Assert.Equal(-32602, error!["code"]!.GetValue<int>());
        Assert.Contains("nonexistent_tool", error["message"]!.GetValue<string>());
    }

    // ── Unknown method with id returns method-not-found error ──

    [Fact]
    public async Task UnknownMethod_WithId_ReturnsMethodNotFoundError()
    {
        var server = CreateServer();
        var request = MakeRequest("unknown/method", id: JsonValue.Create(99));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var error = response!["error"]!;
        Assert.Equal(-32601, error["code"]!.GetValue<int>());
        Assert.Contains("unknown/method", error["message"]!.GetValue<string>());
    }

    // ── Unknown method without id returns null (notification) ──

    [Fact]
    public async Task UnknownMethod_WithoutId_ReturnsNull()
    {
        var server = CreateServer();
        var request = MakeRequest("some/notification");

        var response = await server.HandleMessageAsync(request);

        Assert.Null(response);
    }

    // ── Query: no nodes matching returns isError true with message ──

    [Fact]
    public async Task ToolsCall_Query_NoMatch_ReturnsIsErrorTrue()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "ZzzNoMatch999" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No nodes found", text);
        Assert.Contains("ZzzNoMatch999", text);
    }

    // ── Query: valid match returns isError false ──

    [Fact]
    public async Task ToolsCall_Query_ValidMatch_ReturnsIsErrorFalse()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "Service" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    // ── Query with format="json" ──

    [Fact]
    public async Task ToolsCall_Query_JsonFormat_ReturnsJsonOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["format"] = "json"
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        // JSON format output contains braces
        Assert.Contains("{", text);
    }

    // ── Query with format="text" ──

    [Fact]
    public async Task ToolsCall_Query_TextFormat_ReturnsTextOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["format"] = "text"
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    // ── Query with format="context" ──

    [Fact]
    public async Task ToolsCall_Query_ContextFormat_ReturnsOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["format"] = "context"
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    // ── Query mode parsing ──

    [Theory]
    [InlineData("focused")]
    [InlineData("structural")]
    [InlineData("all")]
    public async Task ToolsCall_Query_AllModes_ReturnSuccess(string mode)
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["mode"] = mode
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    // ── Query confidence parsing ──

    [Theory]
    [InlineData("verified")]
    [InlineData("inferred")]
    [InlineData("unresolved")]
    public async Task ToolsCall_Query_ConfidenceFilter_ReturnSuccess(string confidence)
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["confidence"] = confidence
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        // Should not crash - may or may not have results depending on filter
    }

    // ── List: namespaces scope ──

    [Fact]
    public async Task ToolsCall_List_Namespaces_ReturnsOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject { ["scope"] = "namespaces" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Namespace", text);
    }

    // ── List: interfaces scope ──

    [Fact]
    public async Task ToolsCall_List_Interfaces_ReturnsOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject { ["scope"] = "interfaces" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Interface", text);
    }

    // ── List: unknown scope returns error ──

    [Fact]
    public async Task ToolsCall_List_UnknownScope_ReturnsError()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject { ["scope"] = "invalid_scope" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("invalid_scope", text);
    }

    // ── List: default scope is assemblies ──

    [Fact]
    public async Task ToolsCall_List_NoScope_DefaultsToAssemblies()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Assembly", text);
    }

    // ── Path: missing "from" returns error mentioning "from" ──

    [Fact]
    public async Task ToolsCall_Path_EmptyFrom_ReturnsErrorMentioningParams()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject
                {
                    ["from"] = "",
                    ["to"] = "IRepo"
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("from", text.ToLower());
    }

    // ── Impact: missing symbol returns error ──

    [Fact]
    public async Task ToolsCall_Impact_EmptySymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_impact",
                ["arguments"] = new JsonObject { ["symbol"] = "" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("symbol", result["content"]![0]!["text"]!.GetValue<string>().ToLower());
    }

    // ── Explain: missing symbol returns error ──

    [Fact]
    public async Task ToolsCall_Explain_EmptySymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_explain",
                ["arguments"] = new JsonObject { ["symbol"] = "" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("symbol", result["content"]![0]!["text"]!.GetValue<string>().ToLower());
    }

    // ── Query: no graph data returns error with "codegraph index" hint ──

    [Fact]
    public async Task ToolsCall_Query_NoGraphData_ReturnsFileNotFoundHint()
    {
        // Use fresh server with empty dir (no graph data)
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "Anything" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("codegraph index", text);
    }

    // ── Path: no graph data returns error with "codegraph index" hint ──

    [Fact]
    public async Task ToolsCall_Path_NoGraphData_ReturnsFileNotFoundHint()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject
                {
                    ["from"] = "A",
                    ["to"] = "B"
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("codegraph index", text);
    }

    // ── Impact: no graph data returns error ──

    [Fact]
    public async Task ToolsCall_Impact_NoGraphData_ReturnsFileNotFoundHint()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_impact",
                ["arguments"] = new JsonObject { ["symbol"] = "Anything" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("codegraph index", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ── Explain: no graph data returns error ──

    [Fact]
    public async Task ToolsCall_Explain_NoGraphData_ReturnsFileNotFoundHint()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_explain",
                ["arguments"] = new JsonObject { ["symbol"] = "Anything" }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("codegraph index", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ── Summary: no graph data returns error ──

    [Fact]
    public async Task ToolsCall_Summary_NoGraphData_ReturnsFileNotFoundHint()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_summary",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("codegraph index", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ── List: no graph data returns error ──

    [Fact]
    public async Task ToolsCall_List_NoGraphData_ReturnsFileNotFoundHint()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(1),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("codegraph index", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ── Ping returns empty result ──

    [Fact]
    public async Task Ping_ReturnsEmptyJsonObject()
    {
        var server = CreateServer();
        var request = MakeRequest("ping", id: JsonValue.Create(42));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal(42, response!["id"]!.GetValue<int>());
        Assert.NotNull(response["result"]);
    }

    // ── tools/list returns exactly 6 tools ──

    [Fact]
    public async Task ToolsList_ReturnsExactlySixTools()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(1));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.Equal(13, tools.Count);
    }

    // ── tools/list tool names are exact ──

    [Fact]
    public async Task ToolsList_ContainsExactToolNames()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(1));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("codegraph_query", names);
        Assert.Contains("codegraph_list", names);
        Assert.Contains("codegraph_summary", names);
        Assert.Contains("codegraph_path", names);
        Assert.Contains("codegraph_impact", names);
        Assert.Contains("codegraph_explain", names);
        Assert.Contains("codegraph_test_impact", names);
        Assert.Contains("codegraph_diff", names);
        Assert.Contains("codegraph_packages", names);
    }

    // ── Query with budget parameter ──

    [Fact]
    public async Task ToolsCall_Query_WithBudget_ReturnsTruncatedOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(5),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "Service",
                    ["budget"] = 100
                }
            });

        var response = await server.HandleMessageAsync(request);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    // ── CreateResponse includes jsonrpc = "2.0" ──

    [Fact]
    public async Task AllResponses_HaveJsonRpc20()
    {
        var server = CreateServer();
        var request = MakeRequest("initialize", id: JsonValue.Create(1),
            @params: new JsonObject { ["protocolVersion"] = "2024-11-05" });

        var response = await server.HandleMessageAsync(request);

        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsList_QuerySearchAndPackagesSchemas_HaveExactDefaults()
    {
        var server = CreateServer();
        var response = await server.HandleMessageAsync(MakeRequest("tools/list", id: JsonValue.Create(50)));

        var tools = response!["result"]!["tools"]!.AsArray();
        var queryTool = tools.Single(t => t!["name"]!.GetValue<string>() == "codegraph_query")!;
        var searchTool = tools.Single(t => t!["name"]!.GetValue<string>() == "codegraph_search")!;
        var packagesTool = tools.Single(t => t!["name"]!.GetValue<string>() == "codegraph_packages")!;

        Assert.Contains("Start with codegraph_summary", queryTool["description"]!.GetValue<string>());
        var queryProperties = queryTool["inputSchema"]!["properties"]!.AsObject();
        Assert.Equal("compact", queryProperties["format"]!["default"]!.GetValue<string>());
        Assert.Equal("focused", queryProperties["mode"]!["default"]!.GetValue<string>());
        Assert.False(queryProperties["include_source"]!["default"]!.GetValue<bool>());
        Assert.Equal("symbol", queryTool["inputSchema"]!["required"]![0]!.GetValue<string>());
        var kindEnum = queryProperties["kind"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        Assert.Equal(
            ["calls-to", "calls-from", "inherits", "implements", "depends-on", "resolves-to", "covers", "covered-by", "references", "overrides", "contains", "handles-route", "binds-configuration", "uses-middleware", "maps-to-table", "navigates-to", "configured-by", "all"],
            kindEnum);

        Assert.Contains("Searching inside method bodies or comments", searchTool["description"]!.GetValue<string>());
        var searchProperties = searchTool["inputSchema"]!["properties"]!.AsObject();
        Assert.Equal(20, searchProperties["top"]!["default"]!.GetValue<int>());
        Assert.Equal("all", searchProperties["kind"]!["default"]!.GetValue<string>());
        Assert.Equal("query", searchTool["inputSchema"]!["required"]![0]!.GetValue<string>());

        var packageProperties = packagesTool["inputSchema"]!["properties"]!.AsObject();
        Assert.Equal(2, packageProperties.Count);
        Assert.Equal("Filter to a specific project name", packageProperties["project"]!["description"]!.GetValue<string>());
        Assert.Equal("Filter to a specific package name", packageProperties["package"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Query_InvalidMode_FallsBackToFocusedTraversal()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var structuralResponse = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(51),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["mode"] = "structural",
                        ["format"] = "compact"
                    }
                }));
        var invalidModeResponse = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(52),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["mode"] = "garbage-mode",
                        ["format"] = "compact"
                    }
                }));

        var structuralText = structuralResponse!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var invalidModeText = invalidModeResponse!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.Contains("→ depends-on: App.IRepo, App.UnknownDep [unresolved]", structuralText);
        Assert.DoesNotContain("→ depends-on:", invalidModeText);
        Assert.Contains("## App.Service.Execute() [method, src/Service.cs:5-20]", invalidModeText);
    }

    [Fact]
    public async Task ToolsCall_Query_InvalidFormat_FallsBackToContextFormatter()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(53),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Service",
                        ["mode"] = "structural",
                        ["format"] = "garbage-format"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("# Subgraph for App.Service", text);
        Assert.Contains("## Query: Service --depth 1 --kind all", text);
        Assert.Contains("## Commit: def456 (main, ", text);
    }

    [Fact]
    public async Task ToolsCall_Query_InvalidConfidence_UsesAllEdgesButVerifiedDoesNot()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var verifiedResponse = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(54),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["mode"] = "structural",
                        ["format"] = "compact",
                        ["confidence"] = "verified"
                    }
                }));
        var invalidResponse = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(55),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["mode"] = "structural",
                        ["format"] = "compact",
                        ["confidence"] = "not-a-confidence"
                    }
                }));

        var verifiedText = verifiedResponse!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var invalidText = invalidResponse!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.DoesNotContain("UnknownDep [unresolved]", verifiedText);
        Assert.Contains("UnknownDep [unresolved]", invalidText);
    }

    [Fact]
    public async Task ToolsCall_Query_ThirdQuery_AppendsReportSuggestion()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        for (var index = 0; index < 2; index++)
        {
            await server.HandleMessageAsync(
                MakeRequest("tools/call", id: JsonValue.Create(index + 60),
                    @params: new JsonObject
                    {
                        ["name"] = "codegraph_query",
                        ["arguments"] = new JsonObject { ["symbol"] = "Service" }
                    }));
        }

        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(62),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject { ["symbol"] = "Execute" }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("💡 Suggested next queries:", text);
        Assert.Contains("codegraph report --format compact", text);
    }

    [Fact]
    public async Task ToolsCall_Search_MissingQuery_ReturnsExactError()
    {
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(63),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_search",
                    ["arguments"] = new JsonObject()
                }));

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Equal("Missing required parameter: query", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Search_KindFilter_FormatsExactResultLine()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(64),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_search",
                    ["arguments"] = new JsonObject
                    {
                        ["query"] = "Handle",
                        ["top"] = 1,
                        ["kind"] = "method"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Equal("[Method] Handle  ns=App (src/Controller.cs)\n\n1 result(s) for 'Handle'.", text.Replace("\r\n", "\n").TrimEnd());
    }

    [Fact]
    public async Task ToolsCall_Compare_MissingSymbols_ReturnsExactError()
    {
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(65),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_compare",
                    ["arguments"] = new JsonObject { ["symbolA"] = "SqlRepo" }
                }));

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Equal("Missing required parameters: symbolA and symbolB", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Compare_ReturnsSharedDependencySections()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(66),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_compare",
                    ["arguments"] = new JsonObject
                    {
                        ["symbolA"] = "SqlRepo",
                        ["symbolB"] = "IRepo"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>().Replace("\r\n", "\n");
        Assert.Contains("## Compare: SqlRepo vs IRepo", text);
        Assert.Contains("### Shared dependencies (1)\n  App.IRepo", text);
        Assert.Contains("### Unique to SqlRepo (0)", text);
        Assert.Contains("### Unique to IRepo (0)", text);
    }

    [Fact]
    public async Task ToolsCall_TestImpact_MissingSymbol_ReturnsExactError()
    {
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(67),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_test_impact",
                    ["arguments"] = new JsonObject()
                }));

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Equal("Missing required parameter: symbol", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_TestImpact_ReportsDirectAndTransitiveCoverage()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(68),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_test_impact",
                    ["arguments"] = new JsonObject { ["symbol"] = "Execute" }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>().Replace("\r\n", "\n");
        Assert.Contains("Affected tests for App.Service.Execute():", text);
        Assert.Contains("Direct coverage (tests that call this method):\n  ServiceTests.Execute_is_covered [tests/ServiceTests.cs]", text);
        Assert.Contains("Transitive (tests reaching through call chain):\n  ControllerTests.Handle_is_covered [tests/ControllerTests.cs] via Controller.Handle → Service.Execute", text);
    }

    [Fact]
    public async Task ToolsCall_Packages_NoFilter_IncludesUsageAndConflictSection()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(69),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_packages",
                    ["arguments"] = new JsonObject()
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>().Replace("\r\n", "\n");
        Assert.Contains("# Package usage (2 entries)", text);
        Assert.Contains("## App\n- Newtonsoft.Json v13.0.1\n  Types: 2, Usages: 2", text);
        Assert.Contains("## OtherApp\n- Newtonsoft.Json v12.0.3\n  Types: 1, Usages: 1", text);
        Assert.Contains("# Version conflicts (1 packages)", text);
        Assert.Contains("- App: 13.0.1", text);
        Assert.Contains("- OtherApp: 12.0.3", text);
    }

    [Fact]
    public async Task ToolsCall_Packages_PackageFilter_SkipsConflictSection()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(70),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_packages",
                    ["arguments"] = new JsonObject { ["package"] = "Newtonsoft.Json" }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("# Package usage (2 entries)", text);
        Assert.DoesNotContain("Version conflicts", text);
    }

    [Fact]
    public async Task ToolsCall_Diff_DefaultFormat_UsesGroupedReviewOutput()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);

        var baseNodes = graph.Nodes.Where(n => n.Id != "OtherApp.Service").ToList();
        var baseEdges = graph.Edges.Where(e => e.FromId != "OtherApp.Service").ToList();
        var baseMetadata = graph.Metadata with { CommitHash = "base1234" };
        var baseDir = Path.Combine(_graphDir, "grouped-base");
        Directory.CreateDirectory(baseDir);
        var writer = new GraphWriter();
        await writer.WriteAsync(baseDir, baseNodes, baseEdges, baseMetadata);

        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(71),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_diff",
                    ["arguments"] = new JsonObject { ["base"] = baseDir }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>().Replace("\r\n", "\n");
        Assert.StartsWith("# PR Review: base123..def456", text);
        Assert.Contains("OtherApp.Service", text);
        Assert.Contains("Newtonsoft.Json.Linq.JObject", text);
        Assert.DoesNotContain("# Graph Diff:", text);
    }

    [Fact]
    public async Task ToolsCall_Diff_JsonFormat_ReturnsCamelCasePayload()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);

        var baseNodes = graph.Nodes.Where(n => n.Id != "OtherApp.Service").ToList();
        var baseEdges = graph.Edges.Where(e => e.FromId != "OtherApp.Service").ToList();
        var baseMetadata = graph.Metadata with { CommitHash = "basejson" };
        var baseDir = Path.Combine(_graphDir, "json-base");
        Directory.CreateDirectory(baseDir);
        var writer = new GraphWriter();
        await writer.WriteAsync(baseDir, baseNodes, baseEdges, baseMetadata);

        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(72),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_diff",
                    ["arguments"] = new JsonObject
                    {
                        ["base"] = baseDir,
                        ["format"] = "json"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        using var payload = JsonDocument.Parse(text);
        Assert.True(payload.RootElement.TryGetProperty("addedNodes", out var addedNodes));
        Assert.Equal("OtherApp.Service", addedNodes[0].GetProperty("id").GetString());
        Assert.True(payload.RootElement.TryGetProperty("headMetadata", out _));
    }

    [Fact]
    public async Task ToolsCall_Query_FilePathWithMultipleMatches_ReturnsWildcardMatches()
    {
        var graph = CreateRichGraph();
        await WriteGraphDataAsync(graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest("tools/call", id: JsonValue.Create(73),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Service.cs",
                        ["mode"] = "all",
                        ["format"] = "context"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("App.Service", text);
        Assert.Contains("OtherApp.Service", text);
    }
}
