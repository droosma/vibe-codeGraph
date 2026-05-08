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
                    }
                },
                ["required"] = new JsonArray("symbol")
            }
        };

        var listTool = new JsonObject
        {
            ["name"] = "codegraph_list",
            ["description"] = "Browse the code graph hierarchy. Use before querying to orient yourself.",
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
                        ["description"] = "Max items to return (default: 20)"
                    }
                }
            }
        };

        var summaryTool = new JsonObject
        {
            ["name"] = "codegraph_summary",
            ["description"] = "Generate a summary report of the code graph: hub types, assembly boundaries, test coverage, and suggested queries. Use to get an overview of the codebase structure.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }
        };

        var pathTool = new JsonObject
        {
            ["name"] = "codegraph_path",
            ["description"] = "Find the shortest dependency path between two symbols in the code graph. Use to understand how two symbols are connected through calls, inheritance, or other relationships.",
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
            ["description"] = "Analyze the impact of changing a symbol by finding all symbols that depend on it (reverse dependency traversal). Use to assess the blast radius of a change.",
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
            ["description"] = "Get a comprehensive view of a single symbol: its type, location, signature, members, all incoming/outgoing edges, and test coverage. Use to fully understand a symbol before making changes.",
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
            ["description"] = "Query multiple symbols in one call. Returns combined results with shared context. More efficient than separate queries.",
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

        var result = new JsonObject
        {
            ["tools"] = new JsonArray(tool, listTool, summaryTool, pathTool, impactTool, explainTool, fileTool, batchTool)
        };
        return CreateResponse(id, result);
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName == "codegraph_list")
            return await HandleListCallAsync(id, parameters);
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
                OutputFormat.Compact => CompactFormatter.Format(result),
                _ => ContextFormatter.Format(result, queryDesc)
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
                OutputFormat.Compact => CompactFormatter.Format(mergedResult),
                _ => ContextFormatter.Format(mergedResult, queryDesc)
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
        var top = arguments?["top"]?.GetValue<int>() ?? 20;

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
                    var types = list.ListTypes(assemblyFilter, top);
                    sb.AppendLine($"{"Type",-50} {"In",4} {"Out",4} {"Assembly",-20}");
                    sb.AppendLine(new string('-', 80));
                    foreach (var t in types)
                        sb.AppendLine($"{t.Name,-50} {t.InDegree,4} {t.OutDegree,4} {t.Assembly,-20}");
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
