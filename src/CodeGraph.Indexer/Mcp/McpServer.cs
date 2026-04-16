using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Filters;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Indexer.Mcp;

/// <summary>
/// Minimal MCP (Model Context Protocol) stdio server exposing codegraph_query as a tool.
/// Implements JSON-RPC 2.0 over stdin/stdout with Content-Length framing.
/// </summary>
internal sealed class McpServer
{
    private readonly string _graphDir;
    private QueryEngine? _engine;

    internal McpServer(string graphDir)
    {
        _graphDir = graphDir;
    }

    internal async Task<int> RunAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        using var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        // Detect framing by reading the first line
        var firstLine = await reader.ReadLineAsync();
        if (firstLine is null)
            return 0;

        var useFraming = firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase);

        // Process first message
        JsonNode? firstMessage;
        if (useFraming)
        {
            var contentLength = int.Parse(firstLine["Content-Length:".Length..].Trim());
            await reader.ReadLineAsync(); // blank separator
            var buffer = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                if (read == 0) return 0;
                totalRead += read;
            }
            firstMessage = JsonNode.Parse(new string(buffer));
        }
        else
        {
            firstMessage = JsonNode.Parse(firstLine);
        }

        if (firstMessage is not null)
        {
            var response = await HandleMessageAsync(firstMessage);
            if (response is not null)
            {
                if (useFraming)
                    await WriteFramedMessageAsync(writer, response);
                else
                    await WriteJsonMessageAsync(writer, response);
            }
        }

        // Continue reading messages
        while (true)
        {
            JsonNode? message;
            if (useFraming)
                message = await ReadFramedMessageAsync(reader);
            else
                message = await ReadJsonLineMessageAsync(reader);

            if (message is null)
                break;

            var response = await HandleMessageAsync(message);
            if (response is not null)
            {
                if (useFraming)
                    await WriteFramedMessageAsync(writer, response);
                else
                    await WriteJsonMessageAsync(writer, response);
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads a single JSON-RPC message as one line (newline-delimited JSON).
    /// </summary>
    private static async Task<JsonNode?> ReadJsonLineMessageAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) return null;
            line = line.Trim();
            if (line.Length == 0) continue;
            try { return JsonNode.Parse(line); }
            catch { continue; }
        }
    }

    private static async Task WriteJsonMessageAsync(StreamWriter writer, JsonNode message)
    {
        var json = message.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();
    }

    /// <summary>
    /// Reads a Content-Length framed message (LSP-style).
    /// </summary>
    private static async Task<JsonNode?> ReadFramedMessageAsync(StreamReader reader)
    {
        int contentLength = -1;

        while (true)
        {
            var headerLine = await reader.ReadLineAsync();
            if (headerLine is null)
                return null;

            if (string.IsNullOrEmpty(headerLine))
            {
                if (contentLength <= 0)
                    continue;
                break;
            }

            if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = headerLine["Content-Length:".Length..].Trim();
                contentLength = int.Parse(value);
            }
        }

        var buffer = new char[contentLength];
        var totalRead = 0;
        while (totalRead < contentLength)
        {
            var read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
            if (read == 0)
                return null;
            totalRead += read;
        }

        return JsonNode.Parse(new string(buffer));
    }

    private static async Task WriteFramedMessageAsync(StreamWriter writer, JsonNode message)
    {
        var json = message.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetByteCount(json);
        await writer.WriteAsync($"Content-Length: {bytes}\r\n\r\n");
        await writer.WriteAsync(json);
        await writer.FlushAsync();
    }

    private async Task<JsonNode?> HandleMessageAsync(JsonNode message)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];

        return method switch
        {
            "initialize" => HandleInitialize(id, message["params"]),
            "notifications/initialized" => null,
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolsCallAsync(id, message["params"]),
            "ping" => CreateResponse(id, new JsonObject()),
            _ => id is not null ? CreateError(id, -32601, $"Method not found: {method}") : null
        };
    }

    private static JsonNode HandleInitialize(JsonNode? id, JsonNode? parameters)
    {
        // Echo back the client's protocol version for compatibility
        var clientVersion = parameters?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05";

        var result = new JsonObject
        {
            ["protocolVersion"] = clientVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "codegraph",
                ["version"] = "0.1.0"
            }
        };
        return CreateResponse(id, result);
    }

    private static JsonNode HandleToolsList(JsonNode? id)
    {
        var tool = new JsonObject
        {
            ["name"] = "codegraph_query",
            ["description"] = "Query the semantic code graph for symbol relationships, call chains, dependencies, implementations, DI wiring, and test coverage. Use this instead of grep/search for structural code questions.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Symbol name or pattern to search for. Supports wildcards (*). Examples: 'OrderService', 'IOrder*', 'type:OrderService'"
                    },
                    ["depth"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Traversal depth from matched nodes (0 = node only, 1 = direct neighbors)",
                        ["default"] = 1
                    },
                    ["kind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Edge type filter",
                        ["enum"] = new JsonArray("calls-to", "calls-from", "inherits", "implements", "depends-on", "resolves-to", "covers", "all")
                    },
                    ["namespace"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Namespace filter (wildcards OK)"
                    },
                    ["project"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Project/assembly filter"
                    },
                    ["format"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Output format",
                        ["enum"] = new JsonArray("context", "json", "text"),
                        ["default"] = "context"
                    },
                    ["max_nodes"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum nodes to return",
                        ["default"] = 50
                    },
                    ["include_external"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Include external dependency nodes",
                        ["default"] = false
                    }
                },
                ["required"] = new JsonArray("symbol")
            }
        };

        var result = new JsonObject
        {
            ["tools"] = new JsonArray(tool)
        };
        return CreateResponse(id, result);
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName != "codegraph_query")
            return CreateError(id, -32602, $"Unknown tool: {toolName}");

        var arguments = parameters?["arguments"];
        var symbol = arguments?["symbol"]?.GetValue<string>();
        if (string.IsNullOrEmpty(symbol))
            return CreateError(id, -32602, "Missing required parameter: symbol");

        try
        {
            _engine ??= await QueryEngine.LoadAsync(_graphDir);

            var depth = arguments?["depth"]?.GetValue<int>() ?? 1;
            var kind = arguments?["kind"]?.GetValue<string>();
            var ns = arguments?["namespace"]?.GetValue<string>();
            var project = arguments?["project"]?.GetValue<string>();
            var format = arguments?["format"]?.GetValue<string>() ?? "context";
            var maxNodes = arguments?["max_nodes"]?.GetValue<int>() ?? 50;
            var includeExternal = arguments?["include_external"]?.GetValue<bool>() ?? false;

            EdgeType? edgeTypeFilter;
            try
            {
                edgeTypeFilter = EdgeTypeFilter.Parse(kind);
            }
            catch (ArgumentException ex)
            {
                return CreateToolError(id, ex.Message);
            }

            var outputFormat = format.ToLowerInvariant() switch
            {
                "json" => OutputFormat.Json,
                "text" => OutputFormat.Text,
                _ => OutputFormat.Context
            };

            var options = new QueryOptions
            {
                Pattern = symbol,
                Depth = depth,
                EdgeTypeFilter = edgeTypeFilter,
                NamespaceFilter = ns,
                ProjectFilter = project,
                MaxNodes = maxNodes,
                IncludeExternal = includeExternal,
                Rank = true,
                Format = outputFormat
            };

            var result = _engine.Query(options);

            if (result.MatchedNodes.Count == 0)
                return CreateToolResult(id, $"No nodes found matching '{symbol}'.", true);

            var queryDesc = $"{symbol} --depth {depth} --kind {kind ?? "all"}";
            var output = outputFormat switch
            {
                OutputFormat.Json => JsonFormatter.Format(result),
                OutputFormat.Text => TextFormatter.Format(result),
                _ => ContextFormatter.Format(result, queryDesc)
            };

            return CreateToolResult(id, output, false);
        }
        catch (FileNotFoundException ex)
        {
            return CreateToolError(id, $"{ex.Message}\nRun 'codegraph index' to generate the graph first.");
        }
        catch (Exception ex)
        {
            return CreateToolError(id, ex.Message);
        }
    }

    private static JsonNode CreateResponse(JsonNode? id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (id is not null)
            response["id"] = id.DeepClone();
        return response;
    }

    private static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        if (id is not null)
            response["id"] = id.DeepClone();
        return response;
    }

    private static JsonNode CreateToolResult(JsonNode? id, string text, bool isError)
    {
        var content = new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = text
        });

        var result = new JsonObject
        {
            ["content"] = content,
            ["isError"] = isError
        };

        return CreateResponse(id, result);
    }

    private static JsonNode CreateToolError(JsonNode? id, string message)
    {
        return CreateToolResult(id, message, true);
    }
}
