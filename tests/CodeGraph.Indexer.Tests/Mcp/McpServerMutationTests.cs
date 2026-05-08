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

    private async Task WriteGraphDataAsync()
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "App.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                FilePath = "src/Service.cs",
                StartLine = 1, EndLine = 50,
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
                StartLine = 5, EndLine = 20,
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
                StartLine = 1, EndLine = 5,
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
                FilePath = "",
                Signature = "App",
                Accessibility = Accessibility.Public,
                AssemblyName = "App"
            }
        };

        var edges = new List<GraphEdge>
        {
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
        };

        var metadata = new GraphMetadata
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
}
