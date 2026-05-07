using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Mcp;

namespace CodeGraph.Indexer.Tests.Mcp;

public class McpServerTests : IDisposable
{
    private readonly string _graphDir;

    public McpServerTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerTests).Assembly.Location)!,
            "McpTestGraphData_" + Guid.NewGuid().ToString("N")[..8]);
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
        List<GraphEdge>? edges = null)
    {
        var graphNodes = nodes ?? new List<GraphNode>
        {
            new()
            {
                Id = "MyApp.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                FilePath = "src/OrderService.cs",
                StartLine = 1,
                EndLine = 10,
                Signature = "MyApp.OrderService",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                ContainingNamespaceId = "MyApp",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "MyApp.OrderService.PlaceOrder()",
                Name = "PlaceOrder",
                Kind = NodeKind.Method,
                FilePath = "src/OrderService.cs",
                StartLine = 3,
                EndLine = 8,
                Signature = "MyApp.OrderService.PlaceOrder()",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                ContainingTypeId = "MyApp.OrderService",
                ContainingNamespaceId = "MyApp",
                Metadata = new Dictionary<string, string>
                {
                    ["returnType"] = "void",
                    ["parameterCount"] = "0"
                }
            },
            new()
            {
                Id = "MyApp.IOrderRepository",
                Name = "IOrderRepository",
                Kind = NodeKind.Type,
                FilePath = "src/IOrderRepository.cs",
                StartLine = 1,
                EndLine = 5,
                Signature = "MyApp.IOrderRepository",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                ContainingNamespaceId = "MyApp",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Interface" }
            },
            new()
            {
                Id = "MyApp.SqlOrderRepository",
                Name = "SqlOrderRepository",
                Kind = NodeKind.Type,
                FilePath = "src/SqlOrderRepository.cs",
                StartLine = 1,
                EndLine = 20,
                Signature = "MyApp.SqlOrderRepository",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                ContainingNamespaceId = "MyApp",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            },
            new()
            {
                Id = "MyApp",
                Name = "MyApp",
                Kind = NodeKind.Namespace,
                FilePath = "",
                Signature = "MyApp",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp"
            }
        };

        var graphEdges = edges ?? new List<GraphEdge>
        {
            new()
            {
                FromId = "MyApp.OrderService.PlaceOrder()",
                ToId = "MyApp.IOrderRepository",
                Type = EdgeType.DependsOn,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "MyApp.SqlOrderRepository",
                ToId = "MyApp.IOrderRepository",
                Type = EdgeType.Implements,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "MyApp.OrderService",
                ToId = "MyApp.OrderService.PlaceOrder()",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "MyApp",
                ToId = "MyApp.OrderService",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "MyApp",
                ToId = "MyApp.IOrderRepository",
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            }
        };

        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "0.1.0",
            Solution = "test.sln",
            SolutionName = "test"
        };

        var writer = new GraphWriter();
        await writer.WriteAsync(_graphDir, graphNodes, graphEdges, metadata);
    }

    // ── initialize ──

    [Fact]
    public async Task Initialize_ReturnsProtocolVersion_And_ServerInfo()
    {
        var server = CreateServer();
        var request = MakeRequest("initialize", id: JsonValue.Create(1),
            @params: new JsonObject { ["protocolVersion"] = "2024-11-05" });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, response["id"]!.GetValue<int>());

        var result = response["result"]!;
        Assert.Equal("2024-11-05", result["protocolVersion"]!.GetValue<string>());
        Assert.Equal("codegraph", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("0.1.0", result["serverInfo"]!["version"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task Initialize_WithoutProtocolVersion_DefaultsTo20241105()
    {
        var server = CreateServer();
        var request = MakeRequest("initialize", id: JsonValue.Create(1),
            @params: new JsonObject());

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.Equal("2024-11-05", result["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Initialize_WithCustomProtocolVersion_EchoesItBack()
    {
        var server = CreateServer();
        var request = MakeRequest("initialize", id: JsonValue.Create(42),
            @params: new JsonObject { ["protocolVersion"] = "2025-01-01" });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal(42, response!["id"]!.GetValue<int>());
        Assert.Equal("2025-01-01", response["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Initialize_NullParams_DefaultsProtocolVersion()
    {
        var server = CreateServer();
        var request = MakeRequest("initialize", id: JsonValue.Create(1));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2024-11-05", response!["result"]!["protocolVersion"]!.GetValue<string>());
    }

    // ── notifications/initialized ──

    [Fact]
    public async Task NotificationsInitialized_ReturnsNull()
    {
        var server = CreateServer();
        var request = MakeRequest("notifications/initialized");

        var response = await server.HandleMessageAsync(request);

        Assert.Null(response);
    }

    // ── ping ──

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        var server = CreateServer();
        var request = MakeRequest("ping", id: JsonValue.Create(99));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
        Assert.Equal(99, response["id"]!.GetValue<int>());
        Assert.NotNull(response["result"]);
    }

    // ── unknown method ──

    [Fact]
    public async Task UnknownMethod_WithId_ReturnsMethodNotFoundError()
    {
        var server = CreateServer();
        var request = MakeRequest("nonexistent/method", id: JsonValue.Create(5));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
        Assert.Equal(5, response["id"]!.GetValue<int>());
        var error = response["error"]!;
        Assert.Equal(-32601, error["code"]!.GetValue<int>());
        Assert.Contains("nonexistent/method", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownMethod_WithoutId_ReturnsNull()
    {
        var server = CreateServer();
        var request = MakeRequest("nonexistent/method");

        var response = await server.HandleMessageAsync(request);

        Assert.Null(response);
    }

    // ── tools/list ──

    [Fact]
    public async Task ToolsList_Returns6Tools()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.Equal(6, tools.Count);
    }

    [Fact]
    public async Task ToolsList_ContainsAllExpectedToolNames()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();

        Assert.Contains("codegraph_query", names);
        Assert.Contains("codegraph_list", names);
        Assert.Contains("codegraph_summary", names);
        Assert.Contains("codegraph_path", names);
        Assert.Contains("codegraph_impact", names);
        Assert.Contains("codegraph_explain", names);
    }

    [Fact]
    public async Task ToolsList_QueryTool_HasRequiredSymbolParam()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var queryTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_query");
        var schema = queryTool!["inputSchema"]!;
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        var required = schema["required"]!.AsArray();
        Assert.Contains(required, r => r!.GetValue<string>() == "symbol");
    }

    [Fact]
    public async Task ToolsList_PathTool_HasRequiredFromAndTo()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var pathTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_path");
        var required = pathTool!["inputSchema"]!["required"]!.AsArray();
        Assert.Contains(required, r => r!.GetValue<string>() == "from");
        Assert.Contains(required, r => r!.GetValue<string>() == "to");
    }

    [Fact]
    public async Task ToolsList_ImpactTool_HasRequiredSymbol()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var impactTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_impact");
        var required = impactTool!["inputSchema"]!["required"]!.AsArray();
        Assert.Contains(required, r => r!.GetValue<string>() == "symbol");
    }

    [Fact]
    public async Task ToolsList_ExplainTool_HasRequiredSymbol()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var explainTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_explain");
        var required = explainTool!["inputSchema"]!["required"]!.AsArray();
        Assert.Contains(required, r => r!.GetValue<string>() == "symbol");
    }

    [Fact]
    public async Task ToolsList_EachToolHasDescription()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.All(tools, t =>
        {
            var desc = t!["description"]!.GetValue<string>();
            Assert.False(string.IsNullOrEmpty(desc));
        });
    }

    [Fact]
    public async Task ToolsList_EachToolHasInputSchema()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t!["inputSchema"]);
            Assert.Equal("object", t["inputSchema"]!["type"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task ToolsList_QueryTool_HasAllExpectedProperties()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var queryTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_query");
        var props = queryTool!["inputSchema"]!["properties"]!.AsObject();

        Assert.True(props.ContainsKey("symbol"));
        Assert.True(props.ContainsKey("depth"));
        Assert.True(props.ContainsKey("kind"));
        Assert.True(props.ContainsKey("namespace"));
        Assert.True(props.ContainsKey("project"));
        Assert.True(props.ContainsKey("format"));
        Assert.True(props.ContainsKey("max_nodes"));
        Assert.True(props.ContainsKey("include_external"));
        Assert.True(props.ContainsKey("solution"));
        Assert.True(props.ContainsKey("mode"));
        Assert.True(props.ContainsKey("confidence"));
        Assert.True(props.ContainsKey("budget"));
    }

    [Fact]
    public async Task ToolsList_ListTool_HasScopeEnum()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/list", id: JsonValue.Create(2));

        var response = await server.HandleMessageAsync(request);

        var tools = response!["result"]!["tools"]!.AsArray();
        var listTool = tools.First(t => t!["name"]!.GetValue<string>() == "codegraph_list");
        var scopeProp = listTool!["inputSchema"]!["properties"]!["scope"]!;
        var enumValues = scopeProp["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();

        Assert.Contains("assemblies", enumValues);
        Assert.Contains("types", enumValues);
        Assert.Contains("interfaces", enumValues);
        Assert.Contains("namespaces", enumValues);
    }

    // ── tools/call: error cases ──

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "nonexistent_tool",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var error = response!["error"]!;
        Assert.Equal(-32602, error["code"]!.GetValue<int>());
        var message = error["message"]!.GetValue<string>();
        Assert.Contains("Unknown tool", message);
        Assert.Contains("nonexistent_tool", message);
    }

    [Fact]
    public async Task ToolsCall_Query_MissingSymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var error = response!["error"]!;
        Assert.Equal(-32602, error["code"]!.GetValue<int>());
        Assert.Contains("symbol", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Query_EmptySymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response!["error"]);
    }

    [Fact]
    public async Task ToolsCall_Path_MissingParams_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("from", text);
        Assert.Contains("to", text);
    }

    [Fact]
    public async Task ToolsCall_Impact_MissingSymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_impact",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("symbol", text);
    }

    [Fact]
    public async Task ToolsCall_Explain_MissingSymbol_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_explain",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("symbol", text);
    }

    // ── tools/call: with graph data ──

    [Fact]
    public async Task ToolsCall_Query_ValidSymbol_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "OrderService" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
        var result = response["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("OrderService", text);
    }

    [Fact]
    public async Task ToolsCall_Query_NoMatch_ReturnsNoNodesFound()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "NonExistentSymbol999" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No nodes found", text);
        Assert.Contains("NonExistentSymbol999", text);
    }

    [Fact]
    public async Task ToolsCall_Query_JsonFormat_ReturnsJsonOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["format"] = "json"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Query_TextFormat_ReturnsTextOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["format"] = "text"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Query_CompactFormat_ReturnsCompactOutput()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["format"] = "compact"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Query_WithDepthAndKind_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["depth"] = 2,
                    ["kind"] = "all"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Query_WithNamespaceFilter_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["namespace"] = "MyApp"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Query_WithProjectFilter_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["project"] = "MyApp"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Query_WithMaxNodes_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["max_nodes"] = 5
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Query_WithIncludeExternal_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["include_external"] = true
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Theory]
    [InlineData("focused")]
    [InlineData("structural")]
    [InlineData("all")]
    public async Task ToolsCall_Query_WithMode_ReturnsResult(string mode)
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["mode"] = mode
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("verified")]
    [InlineData("inferred")]
    [InlineData("unresolved")]
    public async Task ToolsCall_Query_WithConfidence_ReturnsResult(string confidence)
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["confidence"] = confidence
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Query_WithBudget_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["budget"] = 500
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Theory]
    [InlineData("calls-to")]
    [InlineData("calls-from")]
    [InlineData("inherits")]
    [InlineData("implements")]
    [InlineData("depends-on")]
    [InlineData("resolves-to")]
    [InlineData("covers")]
    [InlineData("covered-by")]
    [InlineData("references")]
    [InlineData("overrides")]
    [InlineData("contains")]
    [InlineData("all")]
    public async Task ToolsCall_Query_WithEdgeKind_ReturnsResult(string kind)
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["kind"] = kind
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Query_InvalidEdgeKind_ReturnsError()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["kind"] = "invalid-edge-kind"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
    }

    // ── tools/call: codegraph_list ──

    [Fact]
    public async Task ToolsCall_List_Assemblies_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject { ["scope"] = "assemblies" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Assembly", text);
    }

    [Fact]
    public async Task ToolsCall_List_Types_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject { ["scope"] = "types" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_List_Interfaces_ReturnsResult()
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

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_List_Namespaces_ReturnsResult()
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

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_List_DefaultScope_ReturnsAssemblies()
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

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Assembly", text);
    }

    [Fact]
    public async Task ToolsCall_List_InvalidScope_ReturnsError()
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

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("invalid_scope", text);
    }

    [Fact]
    public async Task ToolsCall_List_WithAssemblyFilter_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject
                {
                    ["scope"] = "types",
                    ["assembly"] = "MyApp"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_List_WithTop_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_list",
                ["arguments"] = new JsonObject
                {
                    ["scope"] = "types",
                    ["top"] = 5
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    // ── tools/call: codegraph_path ──

    [Fact]
    public async Task ToolsCall_Path_ValidPath_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject
                {
                    ["from"] = "OrderService",
                    ["to"] = "IOrderRepository"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        // Path may or may not exist; verify response structure
        Assert.NotNull(result["content"]);
    }

    [Fact]
    public async Task ToolsCall_Path_NoPathExists_ReturnsNotFound()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject
                {
                    ["from"] = "NonExistent1",
                    ["to"] = "NonExistent2"
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No path found", text);
    }

    [Fact]
    public async Task ToolsCall_Path_WithMaxDepth_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject
                {
                    ["from"] = "OrderService",
                    ["to"] = "IOrderRepository",
                    ["maxDepth"] = 5
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ToolsCall_Path_MissingFrom_ReturnsError()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject { ["to"] = "Target" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Path_MissingTo_ReturnsError()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_path",
                ["arguments"] = new JsonObject { ["from"] = "Source" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.True(response!["result"]!["isError"]!.GetValue<bool>());
    }

    // ── tools/call: codegraph_impact ──

    [Fact]
    public async Task ToolsCall_Impact_ValidSymbol_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_impact",
                ["arguments"] = new JsonObject { ["symbol"] = "IOrderRepository" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolsCall_Impact_WithDepth_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_impact",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "IOrderRepository",
                    ["depth"] = 5
                }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
    }

    // ── tools/call: codegraph_explain ──

    [Fact]
    public async Task ToolsCall_Explain_ValidSymbol_ReturnsResult()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_explain",
                ["arguments"] = new JsonObject { ["symbol"] = "OrderService" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("OrderService", text);
    }

    [Fact]
    public async Task ToolsCall_Explain_NoMatch_ReturnsNotFound()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_explain",
                ["arguments"] = new JsonObject { ["symbol"] = "ZzzNonExistent999" }
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("No node found", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ── tools/call: codegraph_summary ──

    [Fact]
    public async Task ToolsCall_Summary_ReturnsReport()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_summary",
                ["arguments"] = new JsonObject()
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    // ── tools/call: graph not generated ──

    [Fact]
    public async Task ToolsCall_Query_NoGraphDir_ReturnsRunIndexHint()
    {
        var emptyDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerTests).Assembly.Location)!,
            "McpEmptyDir_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(emptyDir);

        try
        {
            var server = new McpServer(emptyDir);
            var request = MakeRequest("tools/call", id: JsonValue.Create(10),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject { ["symbol"] = "Foo" }
                });

            var response = await server.HandleMessageAsync(request);

            Assert.NotNull(response);
            var result = response!["result"]!;
            Assert.True(result["isError"]!.GetValue<bool>());
            var text = result["content"]![0]!["text"]!.GetValue<string>();
            Assert.Contains("codegraph index", text);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public async Task ToolsCall_List_NoGraphDir_ReturnsRunIndexHint()
    {
        var emptyDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerTests).Assembly.Location)!,
            "McpEmptyDir_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(emptyDir);

        try
        {
            var server = new McpServer(emptyDir);
            var request = MakeRequest("tools/call", id: JsonValue.Create(10),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_list",
                    ["arguments"] = new JsonObject { ["scope"] = "assemblies" }
                });

            var response = await server.HandleMessageAsync(request);

            Assert.NotNull(response);
            var result = response!["result"]!;
            Assert.True(result["isError"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public async Task ToolsCall_Summary_NoGraphDir_ReturnsRunIndexHint()
    {
        var emptyDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerTests).Assembly.Location)!,
            "McpEmptyDir_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(emptyDir);

        try
        {
            var server = new McpServer(emptyDir);
            var request = MakeRequest("tools/call", id: JsonValue.Create(10),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_summary",
                    ["arguments"] = new JsonObject()
                });

            var response = await server.HandleMessageAsync(request);

            Assert.NotNull(response);
            var result = response!["result"]!;
            Assert.True(result["isError"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    // ── JSON-RPC response format ──

    [Fact]
    public async Task Response_HasJsonRpc20()
    {
        var server = CreateServer();
        var request = MakeRequest("ping", id: JsonValue.Create(1));

        var response = await server.HandleMessageAsync(request);

        Assert.Equal("2.0", response!["jsonrpc"]!.GetValue<string>());
    }

    [Fact]
    public async Task Response_HasIdFromRequest()
    {
        var server = CreateServer();
        var request = MakeRequest("ping", id: JsonValue.Create(77));

        var response = await server.HandleMessageAsync(request);

        Assert.Equal(77, response!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task ErrorResponse_HasCodeAndMessage()
    {
        var server = CreateServer();
        var request = MakeRequest("bad/method", id: JsonValue.Create(1));

        var response = await server.HandleMessageAsync(request);

        var error = response!["error"]!;
        Assert.NotNull(error["code"]);
        Assert.NotNull(error["message"]);
    }

    [Fact]
    public async Task ToolResult_HasContentArrayWithTextType()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "OrderService" }
            });

        var response = await server.HandleMessageAsync(request);

        var content = response!["result"]!["content"]!.AsArray();
        Assert.NotEmpty(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(content[0]!["text"]!.GetValue<string>()));
    }

    // ── Solution filter ──

    [Fact]
    public async Task ToolsCall_Query_WithSolutionFilter_ReloadsEngine()
    {
        await WriteGraphDataAsync();
        var server = CreateServer();

        // First query without solution filter
        var request1 = MakeRequest("tools/call", id: JsonValue.Create(10),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject { ["symbol"] = "OrderService" }
            });
        await server.HandleMessageAsync(request1);

        // Second query with solution filter - should reload engine
        var request2 = MakeRequest("tools/call", id: JsonValue.Create(11),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query",
                ["arguments"] = new JsonObject
                {
                    ["symbol"] = "OrderService",
                    ["solution"] = "test"
                }
            });
        var response = await server.HandleMessageAsync(request2);

        Assert.NotNull(response);
    }

    // ── Null arguments edge cases ──

    [Fact]
    public async Task ToolsCall_Query_NullArguments_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject
            {
                ["name"] = "codegraph_query"
            });

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response!["error"]);
    }

    [Fact]
    public async Task ToolsCall_NullToolName_ReturnsError()
    {
        var server = CreateServer();
        var request = MakeRequest("tools/call", id: JsonValue.Create(3),
            @params: new JsonObject());

        var response = await server.HandleMessageAsync(request);

        Assert.NotNull(response);
    }
}
