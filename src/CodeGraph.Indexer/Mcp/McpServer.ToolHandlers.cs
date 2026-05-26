using System.Text;
using System.Text.Json.Nodes;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Filters;
using CodeGraph.Query.Metrics;
using CodeGraph.Query.OutputFormatters;
using CodeGraph.Query.Report;

namespace CodeGraph.Indexer.Mcp;

internal sealed partial class McpServer
{
    #region Tool dispatch

    private Task<JsonNode> HandleToolsCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var toolName = GetStringValue(parameters, "name");

        return toolName switch
        {
            ToolList => HandleListCallAsync(id, parameters),
            ToolSearch => HandleSearchCallAsync(id, parameters),
            ToolSummary => HandleSummaryCallAsync(id),
            ToolPath => HandlePathCallAsync(id, parameters),
            ToolImpact => HandleImpactCallAsync(id, parameters),
            ToolExplain => HandleExplainCallAsync(id, parameters),
            ToolFile => HandleFileCallAsync(id, parameters),
            ToolBatch => HandleBatchCallAsync(id, parameters),
            ToolCompare => HandleCompareCallAsync(id, parameters),
            ToolTestImpact => HandleTestImpactCallAsync(id, parameters),
            ToolDiff => HandleDiffCallAsync(id, parameters),
            ToolPackages => HandlePackagesCallAsync(id, parameters),
            ToolQuery => HandleQueryCallAsync(id, parameters),
            _ => Task.FromResult(CreateError(id, InvalidParamsErrorCode, $"Unknown tool: {toolName}"))
        };
    }

    #endregion

    #region Tool handlers

    private async Task<JsonNode> HandleQueryCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbol = GetStringArgument(parameters, "symbol");
        if (string.IsNullOrEmpty(symbol))
        {
            return CreateError(id, InvalidParamsErrorCode, "Missing required parameter: symbol");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync(GetStringArgument(parameters, "solution"));
            if (!TryResolveQueryPattern(engine, id, symbol, out var pattern, out var earlyResult))
            {
                return earlyResult!;
            }

            var kind = GetStringArgument(parameters, "kind");
            EdgeType? edgeTypeFilter;
            try
            {
                edgeTypeFilter = EdgeTypeFilter.Parse(kind);
            }
            catch (ArgumentException ex)
            {
                return CreateToolError(id, ex.Message);
            }

            var depth = GetIntArgument(parameters, "depth", DefaultQueryDepth);
            var options = CreateQueryOptions(
                pattern,
                depth,
                edgeTypeFilter,
                GetStringArgument(parameters, "namespace"),
                GetStringArgument(parameters, "project"),
                GetIntArgument(parameters, "max_nodes", DefaultMaxNodes),
                GetBoolArgument(parameters, "include_external", false),
                ParseOutputFormat(GetStringArgument(parameters, "format") ?? DefaultCompactFormat),
                ParseQueryMode(GetStringArgument(parameters, "mode")),
                GetNullableIntArgument(parameters, "budget"),
                ParseMinimumConfidence(GetStringArgument(parameters, "confidence")));

            var result = engine.Query(options);
            _sessionTracker.Record(symbol, depth, result.MatchedNodes.Count);

            if (result.MatchedNodes.Count == 0)
            {
                return CreateToolResult(id, BuildMissingQueryMessage(symbol, result.Suggestions), true);
            }

            var queryDescription = $"{symbol} --depth {depth} --kind {kind ?? DefaultAllKind}";
            var output = FormatQueryOutput(
                result,
                options,
                queryDescription,
                GetBoolArgument(parameters, "include_source", false));

            return CreateToolResult(id, output, false);
        });
    }

    private async Task<JsonNode> HandlePathCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var from = GetStringArgument(parameters, "from");
        var to = GetStringArgument(parameters, "to");
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return CreateToolError(id, "Missing required parameters: from and to");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var finder = new PathFinder(engine.Nodes, engine.Edges);
            var result = finder.FindPath(from, to, GetIntArgument(parameters, "maxDepth", DefaultPathDepth));

            return result is null
                ? CreateToolResult(id, $"No path found from '{from}' to '{to}'.", true)
                : CreateToolResult(id, PathFormatter.Format(result), false);
        });
    }

    private async Task<JsonNode> HandleImpactCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbol = GetStringArgument(parameters, "symbol");
        if (string.IsNullOrEmpty(symbol))
        {
            return CreateToolError(id, "Missing required parameter: symbol");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var analyzer = new ImpactAnalyzer(engine.Nodes, engine.Edges);
            var result = analyzer.Analyze(symbol, GetIntArgument(parameters, "depth", DefaultImpactDepth));
            return CreateToolResult(id, ImpactFormatter.Format(result), false);
        });
    }

    private async Task<JsonNode> HandleExplainCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbol = GetStringArgument(parameters, "symbol");
        if (string.IsNullOrEmpty(symbol))
        {
            return CreateToolError(id, "Missing required parameter: symbol");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var explainer = new SymbolExplainer(engine.Nodes, engine.Edges);
            var result = explainer.Explain(symbol);

            return result is null
                ? CreateToolResult(id, $"No node found matching '{symbol}'.", true)
                : CreateToolResult(id, ExplainFormatter.Format(result), false);
        });
    }

    private async Task<JsonNode> HandleFileCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var path = GetStringArgument(parameters, "path");
        if (string.IsNullOrEmpty(path))
        {
            return CreateToolError(id, "Missing required parameter: path");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var nodes = engine.FindByFilePath(path, ParseFileNodeKind(GetStringArgument(parameters, "kind") ?? DefaultTypeKind));

            return nodes.Count == 0
                ? CreateToolResult(id, $"No symbols found in file '{path}'.", true)
                : CreateToolResult(id, FormatFileResults(path, nodes), false);
        });
    }

    private async Task<JsonNode> HandleBatchCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbols = GetArrayArgument(parameters, "symbols");
        if (symbols is null || symbols.Count == 0)
        {
            return CreateToolError(id, "Missing required parameter: symbols (must be a non-empty array)");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var depth = GetIntArgument(parameters, "depth", DefaultQueryDepth);
            var outputFormat = ParseOutputFormat(GetStringArgument(parameters, "format") ?? DefaultCompactFormat);
            var queryMode = ParseQueryMode(GetStringArgument(parameters, "mode"));
            var includeSource = GetBoolArgument(parameters, "include_source", false);

            var mergedNodes = new Dictionary<string, GraphNode>();
            var mergedEdges = new List<GraphEdge>();
            var matchedNodes = new List<GraphNode>();
            var edgeSet = new HashSet<(string FromId, string ToId, EdgeType Type)>();

            foreach (var symbolNode in symbols)
            {
                var currentSymbol = symbolNode?.GetValue<string>();
                if (string.IsNullOrEmpty(currentSymbol))
                {
                    continue;
                }

                var result = engine.Query(new QueryOptions
                {
                    Pattern = currentSymbol,
                    Depth = depth,
                    MaxNodes = DefaultMaxNodes,
                    Rank = true,
                    Mode = queryMode,
                    Format = outputFormat
                });

                matchedNodes.AddRange(result.MatchedNodes);

                foreach (var entry in result.Nodes)
                {
                    mergedNodes.TryAdd(entry.Key, entry.Value);
                }

                foreach (var edge in result.Edges)
                {
                    if (edgeSet.Add((edge.FromId, edge.ToId, edge.Type)))
                    {
                        mergedEdges.Add(edge);
                    }
                }
            }

            if (matchedNodes.Count == 0)
            {
                var requestedSymbols = string.Join(", ", symbols.Select(node => node?.GetValue<string>()));
                return CreateToolResult(id, $"No nodes found matching any of: {requestedSymbols}", true);
            }

            var mergedResult = new QueryResult
            {
                MatchedNodes = matchedNodes.DistinctBy(node => node.Id).ToList(),
                Nodes = mergedNodes,
                Edges = mergedEdges,
                Metadata = engine.Metadata,
                TotalMatchCount = matchedNodes.Count
            };

            var symbolsList = string.Join(", ", symbols.Select(node => node?.GetValue<string>()));
            var queryDescription = $"batch [{symbolsList}] --depth {depth}";
            var output = FormatResult(mergedResult, outputFormat, queryDescription, includeSource);

            return CreateToolResult(id, output, false);
        });
    }

    private async Task<JsonNode> HandleCompareCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbolA = GetStringArgument(parameters, "symbolA");
        var symbolB = GetStringArgument(parameters, "symbolB");
        if (string.IsNullOrEmpty(symbolA) || string.IsNullOrEmpty(symbolB))
        {
            return CreateToolError(id, "Missing required parameters: symbolA and symbolB");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var depth = GetIntArgument(parameters, "depth", DefaultQueryDepth);
            var resultA = engine.Query(CreateFocusedComparisonOptions(symbolA, depth));
            var resultB = engine.Query(CreateFocusedComparisonOptions(symbolB, depth));

            if (resultA.MatchedNodes.Count == 0 && resultB.MatchedNodes.Count == 0)
            {
                return CreateToolResult(id, $"No nodes found matching '{symbolA}' or '{symbolB}'.", true);
            }

            return CreateToolResult(id, FormatComparison(symbolA, resultA, symbolB, resultB), false);
        });
    }

    private async Task<JsonNode> HandleTestImpactCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var symbol = GetStringArgument(parameters, "symbol");
        if (string.IsNullOrEmpty(symbol))
        {
            return CreateToolError(id, "Missing required parameter: symbol");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var analyzer = new TestImpactAnalyzer(engine.Nodes, engine.Edges);
            var result = analyzer.Analyze(symbol, GetIntArgument(parameters, "depth", DefaultImpactDepth));
            return CreateToolResult(id, TestImpactFormatter.Format(result), false);
        });
    }

    private async Task<JsonNode> HandleDiffCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var baseDir = GetStringArgument(parameters, "base");
        if (string.IsNullOrEmpty(baseDir))
        {
            return CreateToolError(id, "Missing required parameter: base");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var headDir = GetStringArgument(parameters, "head") ?? _graphDir;
            var format = GetStringArgument(parameters, "format") ?? DefaultCompactFormat;
            var (baseMeta, baseNodes, baseEdges) = await GraphReader.ReadAsync(baseDir);
            var (headMeta, headNodes, headEdges) = await GraphReader.ReadAsync(headDir);
            var diff = GraphDiffEngine.Compare(baseMeta, baseNodes, baseEdges, headMeta, headNodes, headEdges);

            var output = format.ToLowerInvariant() switch
            {
                "json" => GraphDiffJsonFormatter.Format(diff),
                "text" => GraphDiffTextFormatter.Format(diff),
                "context" => GraphDiffContextFormatter.Format(diff),
                _ => GraphDiffGroupFormatter.Format(diff)
            };

            return CreateToolResult(id, output, false);
        }, SnapshotHint);
    }

    private async Task<JsonNode> HandlePackagesCallAsync(JsonNode? id, JsonNode? parameters)
    {
        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var analyzer = new PackageAnalyzer(engine.Nodes, engine.Edges);
            var packageFilter = GetStringArgument(parameters, "package");
            var output = BuildPackageOutput(analyzer, GetStringArgument(parameters, "project"), packageFilter);
            return CreateToolResult(id, output, false);
        });
    }

    private async Task<JsonNode> HandleListCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var scope = GetStringArgument(parameters, "scope") ?? DefaultAssembliesScope;
        var assemblyFilter = GetStringArgument(parameters, "assembly");
        var top = GetIntArgument(parameters, "top", DefaultListTop);
        var skip = GetIntArgument(parameters, "skip", DefaultListSkip);
        var filter = GetStringArgument(parameters, "filter");

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var list = new ListEngine(engine.Nodes, engine.Edges);
            var output = FormatListOutput(list, scope, assemblyFilter, top, skip, filter);

            return output is null
                ? CreateToolError(id, $"Unknown list scope: {scope}. Use: assemblies, types, interfaces, namespaces")
                : CreateToolResult(id, output, false);
        });
    }

    private async Task<JsonNode> HandleSearchCallAsync(JsonNode? id, JsonNode? parameters)
    {
        var query = GetStringArgument(parameters, "query");
        if (string.IsNullOrEmpty(query))
        {
            return CreateToolError(id, "Missing required parameter: query");
        }

        return await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var results = engine.Search(query, GetIntArgument(parameters, "top", DefaultSearchTop), ParseSearchNodeKind(GetStringArgument(parameters, "kind")));

            return results.Count == 0
                ? CreateToolResult(id, $"No results for '{query}'.", false)
                : CreateToolResult(id, FormatSearchResults(query, results), false);
        });
    }

    private async Task<JsonNode> HandleSummaryCallAsync(JsonNode? id)
        => await ExecuteToolAsync(id, async () =>
        {
            var engine = await GetOrLoadEngineAsync();
            var report = ReportGenerator.Generate(engine.Nodes, engine.Edges, engine.Metadata);
            return CreateToolResult(id, report, false);
        });

    #endregion

    #region Handler helpers

    private static QueryOptions CreateFocusedComparisonOptions(string pattern, int depth)
        => new()
        {
            Pattern = pattern,
            Depth = depth,
            MaxNodes = DefaultMaxNodes,
            Mode = QueryMode.Focused,
            IgnoreCase = true
        };

    private static QueryOptions CreateQueryOptions(
        string pattern,
        int depth,
        EdgeType? edgeTypeFilter,
        string? namespaceFilter,
        string? projectFilter,
        int maxNodes,
        bool includeExternal,
        OutputFormat outputFormat,
        QueryMode queryMode,
        int? budget,
        EdgeConfidence? minimumConfidence)
        => new()
        {
            Pattern = pattern,
            Depth = depth,
            EdgeTypeFilter = edgeTypeFilter,
            NamespaceFilter = namespaceFilter,
            ProjectFilter = projectFilter,
            MaxNodes = maxNodes,
            IncludeExternal = includeExternal,
            Rank = true,
            Format = outputFormat,
            Mode = queryMode,
            Budget = budget,
            ConfidenceThreshold = minimumConfidence,
            IgnoreCase = true
        };

    private string FormatQueryOutput(QueryResult result, QueryOptions options, string queryDescription, bool includeSource)
    {
        var output = FormatResult(result, options.Format, queryDescription, includeSource);
        output = BudgetTruncator.Apply(output, options.Budget);

        var metrics = CompressionCalculator.Calculate(result, output);
        output = MetricsFormatter.AppendMetrics(output, metrics);
        output += $"\n[Cost: {metrics.NodeCount} nodes, {metrics.EdgeCount} edges, ~{metrics.OutputTokens} tokens]";

        var suggestions = QuerySuggestionGenerator.Generate(result, options, session: _sessionTracker);
        var hints = QuerySuggestionGenerator.FormatHints(suggestions);
        if (!string.IsNullOrEmpty(hints))
        {
            output += hints;
        }

        return output;
    }

    private static string FormatResult(QueryResult result, OutputFormat outputFormat, string queryDescription, bool includeSource)
        => outputFormat switch
        {
            OutputFormat.Json => JsonFormatter.Format(result),
            OutputFormat.Text => TextFormatter.Format(result),
            OutputFormat.Compact => CompactFormatter.Format(result, includeSource),
            _ => ContextFormatter.Format(result, queryDescription, includeSource)
        };

    private static string? FormatListOutput(ListEngine list, string scope, string? assemblyFilter, int top, int skip, string? filter)
        => scope switch
        {
            DefaultAssembliesScope => FormatAssemblies(list.ListAssemblies()),
            "types" => FormatTypes(list.ListTypes(assemblyFilter, top, skip, filter), top, skip),
            "interfaces" => FormatInterfaces(list.ListInterfaces(assemblyFilter)),
            "namespaces" => FormatNamespaces(list.ListNamespaces(assemblyFilter)),
            _ => null
        };

    private static string FormatAssemblies(IReadOnlyCollection<AssemblyInfo> assemblies)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
        sb.AppendLine(new string('-', 62));
        foreach (var assembly in assemblies)
        {
            sb.AppendLine($"{assembly.Name,-40} {assembly.TypeCount,6} {assembly.MethodCount,8} {assembly.TotalNodeCount,6}");
        }

        return sb.ToString();
    }

    private static string FormatTypes(ListTypesResult result, int top, int skip)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Type",-50} {"In",4} {"Out",4} {"Assembly",-20}");
        sb.AppendLine(new string('-', 80));
        foreach (var type in result.Types)
        {
            sb.AppendLine($"{type.Name,-50} {type.InDegree,4} {type.OutDegree,4} {type.Assembly,-20}");
        }

        var endIndex = Math.Min(skip + top, result.TotalCount);
        sb.AppendLine($"\nShowing {skip + 1}-{endIndex} of {result.TotalCount:N0} types. Use --skip {endIndex} for next page.");
        return sb.ToString();
    }

    private static string FormatInterfaces(IReadOnlyCollection<InterfaceInfo> interfaces)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Interface",-50} {"Impls",6} {"Assembly",-20}");
        sb.AppendLine(new string('-', 78));
        foreach (var iface in interfaces)
        {
            sb.AppendLine($"{iface.Name,-50} {iface.ImplementationCount,6} {iface.Assembly,-20}");
        }

        return sb.ToString();
    }

    private static string FormatNamespaces(IReadOnlyCollection<NamespaceInfo> namespaces)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Namespace",-50} {"Types",6} {"Methods",8}");
        sb.AppendLine(new string('-', 66));
        foreach (var namespaceSummary in namespaces)
        {
            sb.AppendLine($"{namespaceSummary.Name,-50} {namespaceSummary.TypeCount,6} {namespaceSummary.MethodCount,8}");
        }

        return sb.ToString();
    }

    private static string FormatFileResults(string path, IReadOnlyCollection<GraphNode> nodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Symbols in '{path}' ({nodes.Count} found):");
        sb.AppendLine();
        foreach (var node in nodes.OrderBy(n => n.StartLine))
        {
            sb.AppendLine($"  {node.Kind}: {node.Id}");
            if (!string.IsNullOrEmpty(node.Signature))
            {
                sb.AppendLine($"    sig: {node.Signature}");
            }

            sb.AppendLine($"    lines: {node.StartLine}-{node.EndLine}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSearchResults(string query, IReadOnlyCollection<GraphNode> results)
    {
        var sb = new StringBuilder();
        foreach (var node in results)
        {
            var containingNamespace = node.ContainingNamespaceId ?? string.Empty;
            var filePart = !string.IsNullOrEmpty(node.FilePath) ? $" ({node.FilePath})" : string.Empty;
            sb.AppendLine($"[{node.Kind}] {node.Name}  ns={containingNamespace}{filePart}");
        }

        sb.AppendLine($"\n{results.Count} result(s) for '{query}'.");
        return sb.ToString();
    }

    private static string FormatComparison(string symbolA, QueryResult resultA, string symbolB, QueryResult resultB)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Compare: {symbolA} vs {symbolB}");
        sb.AppendLine();

        AppendEdgeSection(sb, symbolA, resultA);
        sb.AppendLine();
        AppendEdgeSection(sb, symbolB, resultB);
        sb.AppendLine();

        var dependenciesA = new HashSet<string>(GetDependencyTargets(resultA));
        var dependenciesB = new HashSet<string>(GetDependencyTargets(resultB));
        AppendComparisonSection(sb, $"### Shared dependencies ({dependenciesA.Intersect(dependenciesB).Count()})", dependenciesA.Intersect(dependenciesB));
        AppendComparisonSection(sb, $"### Unique to {symbolA} ({dependenciesA.Except(dependenciesB).Count()})", dependenciesA.Except(dependenciesB));
        AppendComparisonSection(sb, $"### Unique to {symbolB} ({dependenciesB.Except(dependenciesA).Count()})", dependenciesB.Except(dependenciesA), addTrailingBlankLine: false);

        return sb.ToString().TrimEnd();
    }

    private static void AppendEdgeSection(StringBuilder sb, string symbol, QueryResult result)
    {
        sb.AppendLine($"### {symbol} ({result.MatchedNodes.Count} matched, {result.Edges.Count} edges)");
        foreach (var edgeDescription in result.Edges
                     .Where(edge => edge.Type != EdgeType.Contains)
                     .Select(edge => $"  {edge.Type}: {edge.FromId} → {edge.ToId}")
                     .Take(20))
        {
            sb.AppendLine(edgeDescription);
        }
    }

    private static IEnumerable<string> GetDependencyTargets(QueryResult result)
        => result.Edges
            .Where(edge => edge.Type != EdgeType.Contains)
            .Select(edge => edge.ToId);

    private static void AppendComparisonSection(StringBuilder sb, string heading, IEnumerable<string> items, bool addTrailingBlankLine = true)
    {
        sb.AppendLine(heading);
        foreach (var item in items.Take(10))
        {
            sb.AppendLine($"  {item}");
        }

        if (addTrailingBlankLine)
        {
            sb.AppendLine();
        }
    }

    private static string BuildMissingQueryMessage(string symbol, IReadOnlyList<string> suggestions)
    {
        var message = $"No nodes found matching '{symbol}'.";
        if (suggestions.Count > 0)
        {
            message += $"\nDid you mean: {string.Join(", ", suggestions)}";
        }

        return message;
    }

    private static string BuildPackageOutput(PackageAnalyzer analyzer, string? projectFilter, string? packageFilter)
    {
        if (!string.IsNullOrEmpty(packageFilter))
        {
            return PackageFormatter.FormatUsage(analyzer.AnalyzeByPackage(packageFilter));
        }

        var output = PackageFormatter.FormatUsage(analyzer.AnalyzeByProject(projectFilter));
        var conflicts = analyzer.FindConflicts();
        if (conflicts.Count > 0)
        {
            output += "\n" + PackageFormatter.FormatConflicts(conflicts);
        }

        return output;
    }

    private static QueryMode ParseQueryMode(string? mode)
        => mode?.ToLowerInvariant() switch
        {
            DefaultFocusedMode => QueryMode.Focused,
            "structural" => QueryMode.Structural,
            DefaultAllKind => QueryMode.All,
            _ => QueryMode.Focused
        };

    private static OutputFormat ParseOutputFormat(string? format)
        => format?.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "text" => OutputFormat.Text,
            DefaultCompactFormat => OutputFormat.Compact,
            _ => OutputFormat.Context
        };

    private static EdgeConfidence? ParseMinimumConfidence(string? confidence)
        => confidence?.ToLowerInvariant() switch
        {
            "verified" => EdgeConfidence.Verified,
            "inferred" => EdgeConfidence.Inferred,
            "unresolved" => EdgeConfidence.Unresolved,
            _ => null
        };

    private static NodeKind? ParseFileNodeKind(string kind)
        => kind.ToLowerInvariant() switch
        {
            DefaultTypeKind => NodeKind.Type,
            "method" => NodeKind.Method,
            DefaultAllKind => null,
            _ => NodeKind.Type
        };

    private static NodeKind? ParseSearchNodeKind(string? kind)
        => kind?.ToLowerInvariant() switch
        {
            DefaultTypeKind => NodeKind.Type,
            "method" => NodeKind.Method,
            "namespace" => NodeKind.Namespace,
            _ => null
        };

    private static bool TryResolveQueryPattern(QueryEngine engine, JsonNode? id, string symbol, out string pattern, out JsonNode? earlyResult)
    {
        pattern = symbol;
        earlyResult = null;

        if (!QueryEngine.LooksLikeFilePath(symbol))
        {
            return true;
        }

        var fileNodes = engine.FindByFilePath(symbol);
        if (fileNodes.Count == 0)
        {
            earlyResult = CreateToolResult(id, $"No symbols found in file '{symbol}'.", true);
            return false;
        }

        pattern = fileNodes.Count == 1
            ? fileNodes[0].Id
            : $"*{Path.GetFileNameWithoutExtension(symbol)}*";

        return true;
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

    #endregion
}
