using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Filters;
using CodeGraph.Query.Metrics;
using CodeGraph.Query.OutputFormatters;
using CodeGraph.Query.Report;

namespace CodeGraph.Indexer.Mcp;

/// <summary>
/// Minimal MCP (Model Context Protocol) stdio server exposing codegraph_query as a tool.
/// Implements JSON-RPC 2.0 over stdin/stdout with Content-Length framing.
/// </summary>
internal sealed class McpServer
{
    private readonly string _graphDir;
    private QueryEngine? _engine;
    private string? _lastSolutionFilter;
    private readonly QuerySessionTracker _sessionTracker = new();

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

    internal async Task<JsonNode?> HandleMessageAsync(JsonNode message)
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
            ["description"] = "Query the code graph for structural relationships (calls, implements, DI wiring, inheritance). TIP: Start with codegraph_summary for orientation, then use this for specific symbols. BEST FOR: 'What calls X?', 'What implements Y?', 'How is Z wired in DI?' NOT FOR: Reading method bodies, finding string literals, broad text search — use file reading/grep for those.",
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
                        ["enum"] = new JsonArray("calls-to", "calls-from", "inherits", "implements", "depends-on", "resolves-to", "covers", "covered-by", "references", "overrides", "contains", "all")
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
                        ["description"] = "Output format. compact=minimal tokens (recommended), context=rich detail, json=structured, text=plain",
                        ["enum"] = new JsonArray("compact", "context", "json", "text"),
                        ["default"] = "compact"
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
                    },
                    ["solution"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Scope query to a specific solution (multi-solution support). Uses the solution name without extension."
                    },
                    ["mode"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("focused", "structural", "all"),
                        ["description"] = "Query traversal mode. focused=high-signal edges only (default, recommended), structural=includes containment, all=everything",
                        ["default"] = "focused"
                    },
                    ["confidence"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("verified", "inferred", "unresolved"),
                        ["description"] = "Minimum confidence level for edges. verified=only verified, inferred=verified+inferred, unresolved=all (default: all)"
                    },
                    ["budget"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum token budget for output. Output is truncated with a hint when exceeded."
                    },
                    ["include_source"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Embed source code snippets (up to 20 lines) for each node in the output",
                        ["default"] = false
                    }
                },
                ["required"] = new JsonArray("symbol")
            }
        };

        var listTool = new JsonObject
        {
            ["name"] = "codegraph_list",
            ["description"] = "Browse the code graph hierarchy. Use to discover assemblies, types, interfaces, or namespaces. BEST FOR: 'What projects exist?', 'What types are in module X?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["scope"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("assemblies", "types", "interfaces", "namespaces"),
                        ["description"] = "What to list (default: assemblies)"
                    },
                    ["assembly"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by assembly name"
                    },
                    ["top"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Max results (default: 50)",
                        ["default"] = 50
                    },
                    ["skip"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Skip N results for pagination",
                        ["default"] = 0
                    },
                    ["filter"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by name substring (case-insensitive)"
                    }
                }
            }
        };

        var summaryTool = new JsonObject
        {
            ["name"] = "codegraph_summary",
            ["description"] = "Get architectural overview of the codebase (hub types, domain clusters, suggested queries). READ THIS FIRST before other queries — it provides free orientation that saves multiple discovery calls.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }
        };

        var pathTool = new JsonObject
        {
            ["name"] = "codegraph_path",
            ["description"] = "Find the shortest dependency path between two symbols in the code graph. BEST FOR: 'How are A and B connected?', 'What's the call chain from X to Y?'",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["from"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Source symbol name or pattern"
                    },
                    ["to"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Target symbol name or pattern"
                    },
                    ["maxDepth"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum search depth (default: 10)",
                        ["default"] = 10
                    }
                },
                ["required"] = new JsonArray("from", "to")
            }
        };

        var impactTool = new JsonObject
        {
            ["name"] = "codegraph_impact",
            ["description"] = "Analyze the impact of changing a symbol by finding all dependents (reverse traversal). BEST FOR: 'What breaks if I change X?', 'What's the blast radius of this change?'",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Symbol name or pattern to analyze impact for"
                    },
                    ["depth"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Reverse traversal depth (default: 3)",
                        ["default"] = 3
                    }
                },
                ["required"] = new JsonArray("symbol")
            }
        };

        var explainTool = new JsonObject
        {
            ["name"] = "codegraph_explain",
            ["description"] = "Get a comprehensive view of a single symbol: type, location, signature, members, all edges, and test coverage. BEST FOR: 'Tell me everything about X', 'What does X look like structurally?'",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Symbol name or pattern to explain"
                    }
                },
                ["required"] = new JsonArray("symbol")
            }
        };

        var fileTool = new JsonObject
        {
            ["name"] = "codegraph_file",
            ["description"] = "Find all symbols defined in a file path. Use when you know the file but not the symbol names.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["path"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "File path (partial match OK, e.g., 'OrderService.cs')"
                    },
                    ["kind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("type", "method", "all"),
                        ["description"] = "Filter by node kind (default: type)"
                    }
                },
                ["required"] = new JsonArray("path")
            }
        };

        var batchTool = new JsonObject
        {
            ["name"] = "codegraph_batch",
            ["description"] = "Query multiple symbols in one call. Returns combined results with shared context. More efficient than separate queries. BEST FOR: 'Compare these 3 services', 'Show all related types together'.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbols"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "List of symbol names/patterns to query"
                    },
                    ["depth"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Traversal depth (default: 1)",
                        ["default"] = 1
                    },
                    ["format"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("compact", "context", "json", "text"),
                        ["description"] = "Output format (default: compact)",
                        ["default"] = "compact"
                    },
                    ["mode"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("focused", "structural", "all"),
                        ["description"] = "Query traversal mode (default: focused)",
                        ["default"] = "focused"
                    }
                },
                ["required"] = new JsonArray("symbols")
            }
        };

        var searchTool = new JsonObject
        {
            ["name"] = "codegraph_search",
            ["description"] = "Search for symbols by name, namespace, or file path. Use for discovery when you don't know exact names. BEST FOR: 'Find things related to payments', 'What types exist in the auth module?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Search term (matches name, namespace, file path)"
                    },
                    ["top"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Max results (default: 20)",
                        ["default"] = 20
                    },
                    ["kind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("type", "method", "namespace", "all"),
                        ["description"] = "Filter by node kind (default: all)",
                        ["default"] = "all"
                    }
                },
                ["required"] = new JsonArray("query")
            }
        };

        var compareTool = new JsonObject
        {
            ["name"] = "codegraph_compare",
            ["description"] = "Compare two symbols structurally. BEST FOR: 'What's different between ServiceA and ServiceB?', 'Compare implementations of interface X'. Shows shared interfaces/bases, unique dependencies, and structural differences.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbolA"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "First symbol to compare"
                    },
                    ["symbolB"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Second symbol to compare"
                    },
                    ["depth"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Traversal depth for each (default: 1)",
                        ["default"] = 1
                    }
                },
                ["required"] = new JsonArray("symbolA", "symbolB")
            }
        };

        var result = new JsonObject
        {
            ["tools"] = new JsonArray(summaryTool, tool, listTool, searchTool, pathTool, impactTool, explainTool, fileTool, batchTool, compareTool)
        };
        return CreateResponse(id, result);
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName == "codegraph_list")
            return await HandleListCallAsync(id, parameters);
        if (toolName == "codegraph_search")
            return await HandleSearchCallAsync(id, parameters);
        if (toolName == "codegraph_summary")
            return await HandleSummaryCallAsync(id);
        if (toolName == "codegraph_path")
            return await HandlePathCallAsync(id, parameters);
        if (toolName == "codegraph_impact")
            return await HandleImpactCallAsync(id, parameters);
        if (toolName == "codegraph_explain")
            return await HandleExplainCallAsync(id, parameters);
        if (toolName == "codegraph_file")
            return await HandleFileCallAsync(id, parameters);
        if (toolName == "codegraph_batch")
            return await HandleBatchCallAsync(id, parameters);
        if (toolName == "codegraph_compare")
            return await HandleCompareCallAsync(id, parameters);
        if (toolName != "codegraph_query")
            return CreateError(id, -32602, $"Unknown tool: {toolName}");

        var arguments = parameters?["arguments"];
        var symbol = arguments?["symbol"]?.GetValue<string>();
        if (string.IsNullOrEmpty(symbol))
            return CreateError(id, -32602, "Missing required parameter: symbol");

        try
        {
            var solutionFilter = arguments?["solution"]?.GetValue<string>();
            var engine = await GetOrLoadEngineAsync(solutionFilter);

            // If symbol looks like a file path, resolve to symbols first
            string pattern;
            if (QueryEngine.LooksLikeFilePath(symbol))
            {
                var fileNodes = engine.FindByFilePath(symbol);
                if (fileNodes.Count == 0)
                    return CreateToolResult(id, $"No symbols found in file '{symbol}'.", true);
                if (fileNodes.Count == 1)
                    pattern = fileNodes[0].Id;
                else
                    pattern = "*" + Path.GetFileNameWithoutExtension(symbol) + "*";
            }
            else
            {
                pattern = symbol;
            }

            var depth = arguments?["depth"]?.GetValue<int>() ?? 1;
            var kind = arguments?["kind"]?.GetValue<string>();
            var ns = arguments?["namespace"]?.GetValue<string>();
            var project = arguments?["project"]?.GetValue<string>();
            var format = arguments?["format"]?.GetValue<string>() ?? "compact";
            var maxNodes = arguments?["max_nodes"]?.GetValue<int>() ?? 50;
            var includeExternal = arguments?["include_external"]?.GetValue<bool>() ?? false;
            var modeStr = arguments?["mode"]?.GetValue<string>();
            var budget = arguments?["budget"]?.GetValue<int>();
            var confidenceStr = arguments?["confidence"]?.GetValue<string>();
            var includeSource = arguments?["include_source"]?.GetValue<bool>() ?? false;

            var queryMode = modeStr?.ToLowerInvariant() switch
            {
                "focused" => QueryMode.Focused,
                "structural" => QueryMode.Structural,
                "all" => QueryMode.All,
                _ => QueryMode.Focused
            };

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
                "compact" => OutputFormat.Compact,
                _ => OutputFormat.Context
            };

            EdgeConfidence? minConfidence = confidenceStr?.ToLowerInvariant() switch
            {
                "verified" => EdgeConfidence.Verified,
                "inferred" => EdgeConfidence.Inferred,
                "unresolved" => EdgeConfidence.Unresolved,
                _ => null
            };

            var options = new QueryOptions
            {
                Pattern = pattern,
                Depth = depth,
                EdgeTypeFilter = edgeTypeFilter,
                NamespaceFilter = ns,
                ProjectFilter = project,
                MaxNodes = maxNodes,
                IncludeExternal = includeExternal,
                Rank = true,
                Format = outputFormat,
                Mode = queryMode,
                Budget = budget,
                ConfidenceThreshold = minConfidence,
                IgnoreCase = true
            };

            var result = engine.Query(options);

            // Record query in session tracker
            _sessionTracker.Record(symbol, depth, result.MatchedNodes.Count);

            if (result.MatchedNodes.Count == 0)
            {
                var msg = $"No nodes found matching '{symbol}'.";
                if (result.Suggestions.Count > 0)
                    msg += $"\nDid you mean: {string.Join(", ", result.Suggestions)}";
                return CreateToolResult(id, msg, true);
            }

            var queryDesc = $"{symbol} --depth {depth} --kind {kind ?? "all"}";
            var output = outputFormat switch
            {
                OutputFormat.Json => JsonFormatter.Format(result),
                OutputFormat.Text => TextFormatter.Format(result),
                OutputFormat.Compact => CompactFormatter.Format(result, includeSource),
                _ => ContextFormatter.Format(result, queryDesc, includeSource)
            };

            output = BudgetTruncator.Apply(output, budget);

            var metrics = CompressionCalculator.Calculate(result, output);
            output = MetricsFormatter.AppendMetrics(output, metrics);

            // Append cost metadata
            output += $"\n[Cost: {metrics.NodeCount} nodes, {metrics.EdgeCount} edges, ~{metrics.OutputTokens} tokens]";

            // Append session-aware suggestions
            var suggestions = QuerySuggestionGenerator.Generate(result, options, session: _sessionTracker);
            var hints = QuerySuggestionGenerator.FormatHints(suggestions);
            if (!string.IsNullOrEmpty(hints))
                output += hints;

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

    private async Task<JsonNode> HandlePathCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var from = arguments?["from"]?.GetValue<string>();
        var to = arguments?["to"]?.GetValue<string>();

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            return CreateToolError(id, "Missing required parameters: from and to");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var finder = new PathFinder(engine.Nodes, engine.Edges);
            var maxDepth = arguments?["maxDepth"]?.GetValue<int>() ?? 10;
            var result = finder.FindPath(from, to, maxDepth);

            if (result is null)
                return CreateToolResult(id, $"No path found from '{from}' to '{to}'.", true);

            return CreateToolResult(id, PathFormatter.Format(result), false);
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

    private async Task<JsonNode> HandleImpactCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var symbol = arguments?["symbol"]?.GetValue<string>();

        if (string.IsNullOrEmpty(symbol))
            return CreateToolError(id, "Missing required parameter: symbol");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var analyzer = new ImpactAnalyzer(engine.Nodes, engine.Edges);
            var depth = arguments?["depth"]?.GetValue<int>() ?? 3;
            var result = analyzer.Analyze(symbol, depth);

            return CreateToolResult(id, ImpactFormatter.Format(result), false);
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

    private async Task<JsonNode> HandleExplainCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var symbol = arguments?["symbol"]?.GetValue<string>();

        if (string.IsNullOrEmpty(symbol))
            return CreateToolError(id, "Missing required parameter: symbol");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var explainer = new SymbolExplainer(engine.Nodes, engine.Edges);
            var result = explainer.Explain(symbol);

            if (result is null)
                return CreateToolResult(id, $"No node found matching '{symbol}'.", true);

            return CreateToolResult(id, ExplainFormatter.Format(result), false);
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

    private async Task<JsonNode> HandleFileCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var path = arguments?["path"]?.GetValue<string>();

        if (string.IsNullOrEmpty(path))
            return CreateToolError(id, "Missing required parameter: path");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var kindStr = arguments?["kind"]?.GetValue<string>() ?? "type";

            NodeKind? kindFilter = kindStr.ToLowerInvariant() switch
            {
                "type" => NodeKind.Type,
                "method" => NodeKind.Method,
                "all" => null,
                _ => NodeKind.Type
            };

            var nodes = engine.FindByFilePath(path, kindFilter);

            if (nodes.Count == 0)
                return CreateToolResult(id, $"No symbols found in file '{path}'.", true);

            var sb = new StringBuilder();
            sb.AppendLine($"Symbols in '{path}' ({nodes.Count} found):");
            sb.AppendLine();
            foreach (var node in nodes.OrderBy(n => n.StartLine))
            {
                sb.AppendLine($"  {node.Kind}: {node.Id}");
                if (!string.IsNullOrEmpty(node.Signature))
                    sb.AppendLine($"    sig: {node.Signature}");
                sb.AppendLine($"    lines: {node.StartLine}-{node.EndLine}");
            }

            return CreateToolResult(id, sb.ToString().TrimEnd(), false);
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

    private async Task<JsonNode> HandleBatchCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var symbolsNode = arguments?["symbols"]?.AsArray();

        if (symbolsNode is null || symbolsNode.Count == 0)
            return CreateToolError(id, "Missing required parameter: symbols (must be a non-empty array)");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var depth = arguments?["depth"]?.GetValue<int>() ?? 1;
            var formatStr = arguments?["format"]?.GetValue<string>() ?? "compact";
            var modeStr = arguments?["mode"]?.GetValue<string>();
            var batchIncludeSource = arguments?["include_source"]?.GetValue<bool>() ?? false;

            var queryMode = modeStr?.ToLowerInvariant() switch
            {
                "focused" => QueryMode.Focused,
                "structural" => QueryMode.Structural,
                "all" => QueryMode.All,
                _ => QueryMode.Focused
            };

            var outputFormat = formatStr.ToLowerInvariant() switch
            {
                "json" => OutputFormat.Json,
                "text" => OutputFormat.Text,
                "compact" => OutputFormat.Compact,
                _ => OutputFormat.Context
            };

            // Run each symbol query against the same loaded engine, merge results
            var mergedNodes = new Dictionary<string, GraphNode>();
            var mergedEdges = new List<GraphEdge>();
            var matchedNodes = new List<GraphNode>();
            var edgeSet = new HashSet<(string, string, EdgeType)>();

            foreach (var symbolNode in symbolsNode)
            {
                var sym = symbolNode?.GetValue<string>();
                if (string.IsNullOrEmpty(sym)) continue;

                var options = new QueryOptions
                {
                    Pattern = sym,
                    Depth = depth,
                    MaxNodes = 50,
                    Rank = true,
                    Mode = queryMode,
                    Format = outputFormat
                };

                var result = engine.Query(options);
                matchedNodes.AddRange(result.MatchedNodes);

                foreach (var kvp in result.Nodes)
                    mergedNodes.TryAdd(kvp.Key, kvp.Value);

                foreach (var edge in result.Edges)
                {
                    var key = (edge.FromId, edge.ToId, edge.Type);
                    if (edgeSet.Add(key))
                        mergedEdges.Add(edge);
                }
            }

            if (matchedNodes.Count == 0)
            {
                var symbols = string.Join(", ", symbolsNode.Select(s => s?.GetValue<string>()));
                return CreateToolResult(id, $"No nodes found matching any of: {symbols}", true);
            }

            var mergedResult = new QueryResult
            {
                MatchedNodes = matchedNodes.DistinctBy(n => n.Id).ToList(),
                Nodes = mergedNodes,
                Edges = mergedEdges,
                Metadata = engine.Metadata,
                TotalMatchCount = matchedNodes.Count
            };

            var symbolsList = string.Join(", ", symbolsNode.Select(s => s?.GetValue<string>()));
            var queryDesc = $"batch [{symbolsList}] --depth {depth}";
            var output = outputFormat switch
            {
                OutputFormat.Json => JsonFormatter.Format(mergedResult),
                OutputFormat.Text => TextFormatter.Format(mergedResult),
                OutputFormat.Compact => CompactFormatter.Format(mergedResult, batchIncludeSource),
                _ => ContextFormatter.Format(mergedResult, queryDesc, batchIncludeSource)
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

    private async Task<JsonNode> HandleCompareCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var symbolA = arguments?["symbolA"]?.GetValue<string>();
        var symbolB = arguments?["symbolB"]?.GetValue<string>();

        if (string.IsNullOrEmpty(symbolA) || string.IsNullOrEmpty(symbolB))
            return CreateToolError(id, "Missing required parameters: symbolA and symbolB");

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var depth = arguments?["depth"]?.GetValue<int>() ?? 1;

            var optionsA = new QueryOptions { Pattern = symbolA, Depth = depth, MaxNodes = 50, Mode = QueryMode.Focused, IgnoreCase = true };
            var optionsB = new QueryOptions { Pattern = symbolB, Depth = depth, MaxNodes = 50, Mode = QueryMode.Focused, IgnoreCase = true };

            var resultA = engine.Query(optionsA);
            var resultB = engine.Query(optionsB);

            if (resultA.MatchedNodes.Count == 0 && resultB.MatchedNodes.Count == 0)
                return CreateToolResult(id, $"No nodes found matching '{symbolA}' or '{symbolB}'.", true);

            var sb = new StringBuilder();
            sb.AppendLine($"## Compare: {symbolA} vs {symbolB}");
            sb.AppendLine();

            // Show edges for A
            sb.AppendLine($"### {symbolA} ({resultA.MatchedNodes.Count} matched, {resultA.Edges.Count} edges)");
            var edgesA = resultA.Edges.Where(e => e.Type != EdgeType.Contains)
                .Select(e => $"  {e.Type}: {e.FromId} → {e.ToId}").Take(20);
            foreach (var e in edgesA) sb.AppendLine(e);
            sb.AppendLine();

            // Show edges for B
            sb.AppendLine($"### {symbolB} ({resultB.MatchedNodes.Count} matched, {resultB.Edges.Count} edges)");
            var edgesB = resultB.Edges.Where(e => e.Type != EdgeType.Contains)
                .Select(e => $"  {e.Type}: {e.FromId} → {e.ToId}").Take(20);
            foreach (var e in edgesB) sb.AppendLine(e);
            sb.AppendLine();

            // Shared dependencies
            var depsA = new HashSet<string>(resultA.Edges.Where(e => e.Type != EdgeType.Contains).Select(e => e.ToId));
            var depsB = new HashSet<string>(resultB.Edges.Where(e => e.Type != EdgeType.Contains).Select(e => e.ToId));
            var shared = depsA.Intersect(depsB).ToList();
            var uniqueA = depsA.Except(depsB).ToList();
            var uniqueB = depsB.Except(depsA).ToList();

            sb.AppendLine($"### Shared dependencies ({shared.Count})");
            foreach (var s in shared.Take(10)) sb.AppendLine($"  {s}");
            sb.AppendLine();
            sb.AppendLine($"### Unique to {symbolA} ({uniqueA.Count})");
            foreach (var s in uniqueA.Take(10)) sb.AppendLine($"  {s}");
            sb.AppendLine();
            sb.AppendLine($"### Unique to {symbolB} ({uniqueB.Count})");
            foreach (var s in uniqueB.Take(10)) sb.AppendLine($"  {s}");

            return CreateToolResult(id, sb.ToString().TrimEnd(), false);
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

    private async Task<QueryEngine> GetOrLoadEngineAsync(string? solutionFilter = null)
    {
        if (_engine is null || solutionFilter != _lastSolutionFilter)
        {
            _engine = await QueryEngine.LoadAsync(_graphDir, solutionFilter);
            _lastSolutionFilter = solutionFilter;
        }
        return _engine;
    }

    private async Task<JsonNode> HandleListCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var scope = arguments?["scope"]?.GetValue<string>() ?? "assemblies";
        var assemblyFilter = arguments?["assembly"]?.GetValue<string>();
        var top = arguments?["top"]?.GetValue<int>() ?? 50;
        var skip = arguments?["skip"]?.GetValue<int>() ?? 0;
        var filter = arguments?["filter"]?.GetValue<string>();

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var list = new ListEngine(engine.Nodes, engine.Edges);
            var sb = new StringBuilder();

            switch (scope)
            {
                case "assemblies":
                    var assemblies = list.ListAssemblies();
                    sb.AppendLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
                    sb.AppendLine(new string('-', 62));
                    foreach (var a in assemblies)
                        sb.AppendLine($"{a.Name,-40} {a.TypeCount,6} {a.MethodCount,8} {a.TotalNodeCount,6}");
                    break;

                case "types":
                    var result = list.ListTypes(assemblyFilter, top, skip, filter);
                    sb.AppendLine($"{"Type",-50} {"In",4} {"Out",4} {"Assembly",-20}");
                    sb.AppendLine(new string('-', 80));
                    foreach (var t in result.Types)
                        sb.AppendLine($"{t.Name,-50} {t.InDegree,4} {t.OutDegree,4} {t.Assembly,-20}");
                    var endIndex = Math.Min(skip + top, result.TotalCount);
                    sb.AppendLine($"\nShowing {skip + 1}-{endIndex} of {result.TotalCount:N0} types. Use --skip {endIndex} for next page.");
                    break;

                case "interfaces":
                    var ifaces = list.ListInterfaces(assemblyFilter);
                    sb.AppendLine($"{"Interface",-50} {"Impls",6} {"Assembly",-20}");
                    sb.AppendLine(new string('-', 78));
                    foreach (var iface in ifaces)
                        sb.AppendLine($"{iface.Name,-50} {iface.ImplementationCount,6} {iface.Assembly,-20}");
                    break;

                case "namespaces":
                    var namespaces = list.ListNamespaces(assemblyFilter);
                    sb.AppendLine($"{"Namespace",-50} {"Types",6} {"Methods",8}");
                    sb.AppendLine(new string('-', 66));
                    foreach (var ns in namespaces)
                        sb.AppendLine($"{ns.Name,-50} {ns.TypeCount,6} {ns.MethodCount,8}");
                    break;

                default:
                    return CreateToolError(id, $"Unknown list scope: {scope}. Use: assemblies, types, interfaces, namespaces");
            }

            return CreateToolResult(id, sb.ToString(), false);
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

    private async Task<JsonNode> HandleSearchCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"];
        var query = arguments?["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query))
            return CreateToolError(id, "Missing required parameter: query");

        var top = arguments?["top"]?.GetValue<int>() ?? 20;
        var kindStr = arguments?["kind"]?.GetValue<string>();

        NodeKind? kindFilter = kindStr?.ToLowerInvariant() switch
        {
            "type" => NodeKind.Type,
            "method" => NodeKind.Method,
            "namespace" => NodeKind.Namespace,
            _ => null
        };

        try
        {
            var engine = await GetOrLoadEngineAsync();
            var results = engine.Search(query, top, kindFilter);

            if (results.Count == 0)
                return CreateToolResult(id, $"No results for '{query}'.", false);

            var sb = new StringBuilder();
            foreach (var node in results)
            {
                var ns = node.ContainingNamespaceId ?? "";
                var filePart = !string.IsNullOrEmpty(node.FilePath) ? $" ({node.FilePath})" : "";
                sb.AppendLine($"[{node.Kind}] {node.Name}  ns={ns}{filePart}");
            }
            sb.AppendLine($"\n{results.Count} result(s) for '{query}'.");

            return CreateToolResult(id, sb.ToString(), false);
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

    private async Task<JsonNode> HandleSummaryCallAsync(JsonNode? id)
    {
        try
        {
            var engine = await GetOrLoadEngineAsync();
            var report = ReportGenerator.Generate(engine.Nodes, engine.Edges, engine.Metadata);
            return CreateToolResult(id, report, false);
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
