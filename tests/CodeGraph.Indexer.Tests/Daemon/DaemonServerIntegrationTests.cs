using System.Reflection;
using System.Text.Json.Nodes;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Daemon;
using CodeGraph.Query;

namespace CodeGraph.Indexer.Tests.Daemon;

[Collection("DaemonIntegration")]
public sealed class DaemonServerIntegrationTests : IDisposable
{
    private readonly string _graphDir;

    public DaemonServerIntegrationTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(DaemonServerIntegrationTests).Assembly.Location)!,
            "DaemonIntegration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    [Fact]
    public async Task StartAsync_LoadFailure_DeletesPidFileBeforeRethrowing()
    {
        using var server = new DaemonServer(_graphDir);

        await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync());

        Assert.False(File.Exists(PidFile.GetPath(_graphDir)));
    }

    [Fact]
    public void TryParseRequest_InvalidJson_ReturnsFalse_AndWhitespaceJsonParsesSuccessfully()
    {
        var invalidArgs = new object?[] { "{not-json", null };
        var invalidResult = (bool)typeof(DaemonServer)
            .GetMethod("TryParseRequest", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, invalidArgs)!;

        Assert.False(invalidResult);
        Assert.Null(invalidArgs[1]);

        var validArgs = new object?[] { "  {\"id\":9,\"command\":\"ping\",\"args\":[]}  ", null };
        var validResult = (bool)typeof(DaemonServer)
            .GetMethod("TryParseRequest", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, validArgs)!;

        Assert.True(validResult);
        var request = Assert.IsAssignableFrom<JsonNode>(validArgs[1]);
        Assert.Equal(9, request["id"]?.GetValue<int>());
        Assert.Equal("ping", request["command"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleRequestAsync_Ping_ReturnsPongResponse()
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["id"] = 1,
            ["command"] = "ping",
            ["args"] = new JsonArray()
        });

        Assert.Equal("2.0", response["jsonrpc"]?.GetValue<string>());
        Assert.Equal(1, response["id"]?.GetValue<int>());
        Assert.Equal("pong", response["result"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleRequestAsync_Query_ReturnsCompactFormatterOutput()
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["id"] = 2,
            ["command"] = "query",
            ["args"] = new JsonArray("Demo.Service")
        });

        Assert.Equal(
            "# Service\n\n## Service [type, D:\\repo\\Service.cs:12-20]\n  → contains: Service.Run\n\n## Related\n- Service.Run [method]",
            NormalizeNewLines(response["result"]?.GetValue<string>() ?? string.Empty));
    }

    [Fact]
    public async Task HandleRequestAsync_SearchListAndStats_ReturnExactStrings()
    {
        using var server = CreateServerWithEngine();

        var search = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "search",
            ["args"] = new JsonArray("serv")
        });
        Assert.Equal("Type: Demo.Service\nMethod: Demo.Service.Run", NormalizeNewLines(search["result"]?.GetValue<string>() ?? string.Empty));

        var list = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "list",
            ["args"] = new JsonArray()
        });
        Assert.Equal(
            "Assembly                                  Types  Methods  Total\n--------------------------------------------------------------\nDemo.Assembly                                 1        1      2",
            NormalizeNewLines(list["result"]?.GetValue<string>() ?? string.Empty));

        var stats = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "stats",
            ["args"] = new JsonArray()
        });
        Assert.Equal("Nodes: 2\nEdges: 1", NormalizeNewLines(stats["result"]?.GetValue<string>() ?? string.Empty));
    }

    [Fact]
    public async Task HandleRequestAsync_UnknownCommand_ReturnsJsonError()
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["id"] = 7,
            ["command"] = "unknown",
            ["args"] = new JsonArray()
        });

        Assert.Equal(7, response["id"]?.GetValue<int>());
        Assert.Equal(-32603, response["error"]?["code"]?.GetValue<int>());
        Assert.Equal("Unknown command: unknown", response["error"]?["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleRequestAsync_MissingCommand_ReturnsJsonError()
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["id"] = 8,
            ["args"] = new JsonArray()
        });

        Assert.Equal(8, response["id"]?.GetValue<int>());
        Assert.Equal(-32600, response["error"]?["code"]?.GetValue<int>());
        Assert.Equal("Missing 'command' field", response["error"]?["message"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("query", "Error: symbol pattern required")]
    [InlineData("search", "Error: search query required")]
    public async Task HandleRequestAsync_MissingRequiredArguments_ReturnsExactErrorResult(string command, string expectedResult)
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = command,
            ["args"] = new JsonArray()
        });

        Assert.Equal(expectedResult, response["result"]?.GetValue<string>());
        Assert.Null(response["id"]);
    }

    [Fact]
    public async Task HandleRequestAsync_ListUnknownScope_ReturnsSupportedScopeMessage()
    {
        using var server = CreateServerWithEngine();

        var response = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "list",
            ["args"] = new JsonArray("types")
        });

        Assert.Equal("Unknown scope: types. Supported: assemblies", response["result"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleRequestAsync_InvalidNumericOptions_FallBackToDefaultQueryOutput()
    {
        using var server = CreateServerWithEngine();

        var defaultResponse = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "query",
            ["args"] = new JsonArray("Demo.Service")
        });
        var invalidResponse = await InvokeHandleRequestAsync(server, new JsonObject
        {
            ["command"] = "query",
            ["args"] = new JsonArray("Demo.Service", "--depth", "oops", "--max-nodes", "NaN")
        });

        Assert.Equal(defaultResponse["result"]?.GetValue<string>(), invalidResponse["result"]?.GetValue<string>());
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n");

    private static async Task<JsonNode> InvokeHandleRequestAsync(DaemonServer server, JsonNode request)
    {
        var method = typeof(DaemonServer).GetMethod("HandleRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<JsonNode>)method.Invoke(server, new object[] { request })!;
        return await task;
    }

    private DaemonServer CreateServerWithEngine()
    {
        var metadata = new GraphMetadata { Solution = "Daemon.sln", SolutionName = "Daemon" };
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Demo.Service"] = new GraphNode
            {
                Id = "Demo.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                AssemblyName = "Demo.Assembly",
                Accessibility = Accessibility.Public,
                FilePath = @"D:\repo\Service.cs",
                StartLine = 12,
                EndLine = 20,
                ContainingNamespaceId = "Demo"
            },
            ["Demo.Service.Run"] = new GraphNode
            {
                Id = "Demo.Service.Run",
                Name = "Run",
                Kind = NodeKind.Method,
                AssemblyName = "Demo.Assembly",
                Accessibility = Accessibility.Internal,
                FilePath = @"D:\repo\Service.cs",
                StartLine = 14,
                EndLine = 16,
                ContainingNamespaceId = "Demo"
            }
        };
        var edges = new List<GraphEdge>
        {
            new GraphEdge { FromId = "Demo.Service", ToId = "Demo.Service.Run", Type = EdgeType.Contains }
        };

        var server = new DaemonServer(_graphDir);
        typeof(DaemonServer).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, new QueryEngine(nodes, edges, metadata));
        return server;
    }
}
