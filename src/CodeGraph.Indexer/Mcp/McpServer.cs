using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Query;

namespace CodeGraph.Indexer.Mcp;

/// <summary>
/// Minimal MCP (Model Context Protocol) stdio server exposing codegraph_query as a tool.
/// Implements JSON-RPC 2.0 over stdin/stdout with Content-Length framing.
/// </summary>
internal sealed partial class McpServer
{
    private const string ArgumentsPropertyName = "arguments";
    private const string ContentLengthHeader = "Content-Length:";
    private const string DefaultAllKind = "all";
    private const string DefaultAssembliesScope = "assemblies";
    private const string DefaultCompactFormat = "compact";
    private const string DefaultFocusedMode = "focused";
    private const string DefaultGraphSnapshotPath = ".codegraph";
    private const string DefaultProtocolVersion = "2024-11-05";
    private const string DefaultTypeKind = "type";
    private const string GraphIndexHint = "Run 'codegraph index' to generate the graph first.";
    private const int DefaultImpactDepth = 3;
    private const int DefaultListSkip = 0;
    private const int DefaultListTop = 50;
    private const int DefaultMaxNodes = 50;
    private const int DefaultPathDepth = 10;
    private const int DefaultQueryDepth = 1;
    private const int DefaultSearchTop = 20;
    private const int InvalidParamsErrorCode = -32602;
    private const string JsonRpcVersion = "2.0";
    private const int MethodNotFoundErrorCode = -32601;
    private const string MethodInitialize = "initialize";
    private const string MethodInitializedNotification = "notifications/initialized";
    private const string MethodPing = "ping";
    private const string MethodToolsCall = "tools/call";
    private const string MethodToolsList = "tools/list";
    private const string ServerName = "codegraph";
    private const string ServerVersion = "0.1.0";
    private const string SnapshotHint = "Use 'codegraph snapshot save <name>' to create a snapshot first.";
    private const string TextContentType = "text";
    private const string ToolBatch = "codegraph_batch";
    private const string ToolCompare = "codegraph_compare";
    private const string ToolDiff = "codegraph_diff";
    private const string ToolExplain = "codegraph_explain";
    private const string ToolFile = "codegraph_file";
    private const string ToolImpact = "codegraph_impact";
    private const string ToolList = "codegraph_list";
    private const string ToolPackages = "codegraph_packages";
    private const string ToolPath = "codegraph_path";
    private const string ToolQuery = "codegraph_query";
    private const string ToolSearch = "codegraph_search";
    private const string ToolSummary = "codegraph_summary";
    private const string ToolTestImpact = "codegraph_test_impact";

    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    private readonly string _graphDir;
    private readonly QuerySessionTracker _sessionTracker = new();
    private QueryEngine? _engine;
    private string? _lastSolutionFilter;

    internal McpServer(string graphDir)
    {
        _graphDir = graphDir;
    }

    #region Message loop

    internal async Task<int> RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        using var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        var firstLine = await reader.ReadLineAsync();
        if (firstLine is null)
        {
            return 0;
        }

        var useFraming = firstLine.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase);
        var firstMessage = await ReadFirstMessageAsync(reader, firstLine, useFraming);
        if (firstMessage is null)
        {
            return 0;
        }

        await WriteResponseAsync(writer, useFraming, await HandleMessageAsync(firstMessage));

        while (await ReadNextMessageAsync(reader, useFraming) is { } message)
        {
            await WriteResponseAsync(writer, useFraming, await HandleMessageAsync(message));
        }

        return 0;
    }

    private static async Task<JsonNode?> ReadFirstMessageAsync(StreamReader reader, string firstLine, bool useFraming)
    {
        if (!useFraming)
        {
            return JsonNode.Parse(firstLine);
        }

        var contentLength = int.Parse(firstLine[ContentLengthHeader.Length..].Trim());
        await reader.ReadLineAsync();
        return await ReadMessageBodyAsync(reader, contentLength);
    }

    private static Task<JsonNode?> ReadNextMessageAsync(StreamReader reader, bool useFraming)
        => useFraming ? ReadFramedMessageAsync(reader) : ReadJsonLineMessageAsync(reader);

    private static Task WriteResponseAsync(StreamWriter writer, bool useFraming, JsonNode? response)
    {
        if (response is null)
        {
            return Task.CompletedTask;
        }

        return useFraming ? WriteFramedMessageAsync(writer, response) : WriteJsonMessageAsync(writer, response);
    }

    #endregion

    #region Protocol helpers

    /// <summary>
    /// Reads a single JSON-RPC message as one line (newline-delimited JSON).
    /// </summary>
    private static async Task<JsonNode?> ReadJsonLineMessageAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                return null;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                return JsonNode.Parse(line);
            }
            catch
            {
                continue;
            }
        }
    }

    private static async Task WriteJsonMessageAsync(StreamWriter writer, JsonNode message)
    {
        var json = message.ToJsonString(CompactJsonOptions);
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();
    }

    /// <summary>
    /// Reads a Content-Length framed message (LSP-style).
    /// </summary>
    private static async Task<JsonNode?> ReadFramedMessageAsync(StreamReader reader)
    {
        var contentLength = -1;

        while (true)
        {
            var headerLine = await reader.ReadLineAsync();
            if (headerLine is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(headerLine))
            {
                if (contentLength <= 0)
                {
                    continue;
                }

                break;
            }

            if (headerLine.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(headerLine[ContentLengthHeader.Length..].Trim());
            }
        }

        return await ReadMessageBodyAsync(reader, contentLength);
    }

    private static async Task<JsonNode?> ReadMessageBodyAsync(StreamReader reader, int contentLength)
    {
        var buffer = new char[contentLength];
        var totalRead = 0;
        while (totalRead < contentLength)
        {
            var read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
            if (read == 0)
            {
                return null;
            }

            totalRead += read;
        }

        return JsonNode.Parse(new string(buffer));
    }

    private static async Task WriteFramedMessageAsync(StreamWriter writer, JsonNode message)
    {
        var json = message.ToJsonString(CompactJsonOptions);
        var byteCount = Encoding.UTF8.GetByteCount(json);
        await writer.WriteAsync($"{ContentLengthHeader} {byteCount}\r\n\r\n");
        await writer.WriteAsync(json);
        await writer.FlushAsync();
    }

    #endregion

    #region Message routing

    internal async Task<JsonNode?> HandleMessageAsync(JsonNode message)
    {
        var method = GetStringValue(message, "method");
        var id = message["id"];

        return method switch
        {
            MethodInitialize => HandleInitialize(id, message["params"]),
            MethodInitializedNotification => null,
            MethodToolsList => HandleToolsList(id),
            MethodToolsCall => await HandleToolsCallAsync(id, message["params"]),
            MethodPing => CreateResponse(id, new JsonObject()),
            _ => id is not null ? CreateError(id, MethodNotFoundErrorCode, $"Method not found: {method}") : null
        };
    }

    private async Task<JsonNode> ExecuteToolAsync(JsonNode? id, Func<Task<JsonNode>> handler, string missingDataHint)
    {
        try
        {
            return await handler();
        }
        catch (FileNotFoundException ex)
        {
            return CreateToolError(id, $"{ex.Message}\n{missingDataHint}");
        }
        catch (Exception ex)
        {
            return CreateToolError(id, ex.Message);
        }
    }

    private Task<JsonNode> ExecuteToolAsync(JsonNode? id, Func<Task<JsonNode>> handler)
        => ExecuteToolAsync(id, handler, GraphIndexHint);

    private static JsonNode? GetArguments(JsonNode? parameters)
        => parameters?[ArgumentsPropertyName];

    private static string? GetStringArgument(JsonNode? parameters, string name)
        => GetArguments(parameters)?[name]?.GetValue<string>();

    private static int GetIntArgument(JsonNode? parameters, string name, int defaultValue)
        => GetArguments(parameters)?[name]?.GetValue<int>() ?? defaultValue;

    private static int? GetNullableIntArgument(JsonNode? parameters, string name)
        => GetArguments(parameters)?[name]?.GetValue<int>();

    private static bool GetBoolArgument(JsonNode? parameters, string name, bool defaultValue)
        => GetArguments(parameters)?[name]?.GetValue<bool>() ?? defaultValue;

    private static JsonArray? GetArrayArgument(JsonNode? parameters, string name)
        => GetArguments(parameters)?[name]?.AsArray();

    private static string? GetStringValue(JsonNode? node, string propertyName)
        => node?[propertyName]?.GetValue<string>();

    #endregion

    #region Response builders

    private static JsonNode CreateResponse(JsonNode? id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["result"] = result
        };

        if (id is not null)
        {
            response["id"] = id.DeepClone();
        }

        return response;
    }

    private static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (id is not null)
        {
            response["id"] = id.DeepClone();
        }

        return response;
    }

    private static JsonNode CreateToolResult(JsonNode? id, string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = TextContentType,
                ["text"] = text
            }),
            ["isError"] = isError
        };

        return CreateResponse(id, result);
    }

    private static JsonNode CreateToolError(JsonNode? id, string message)
        => CreateToolResult(id, message, true);

    #endregion
}
