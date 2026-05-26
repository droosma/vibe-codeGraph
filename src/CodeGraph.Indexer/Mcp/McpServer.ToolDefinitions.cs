using System.Text.Json.Nodes;

namespace CodeGraph.Indexer.Mcp;

internal sealed partial class McpServer
{
    #region Tool definitions

    private static JsonNode HandleInitialize(JsonNode? id, JsonNode? parameters)
    {
        var clientVersion = GetStringValue(parameters, "protocolVersion") ?? DefaultProtocolVersion;

        var result = new JsonObject
        {
            ["protocolVersion"] = clientVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        };

        return CreateResponse(id, result);
    }

    private static JsonNode HandleToolsList(JsonNode? id)
    {
        var result = new JsonObject
        {
            ["tools"] = new JsonArray(
                CreateSummaryToolDefinition(),
                CreateQueryToolDefinition(),
                CreateListToolDefinition(),
                CreateSearchToolDefinition(),
                CreatePathToolDefinition(),
                CreateImpactToolDefinition(),
                CreateExplainToolDefinition(),
                CreateFileToolDefinition(),
                CreateBatchToolDefinition(),
                CreateCompareToolDefinition(),
                CreateTestImpactToolDefinition(),
                CreateDiffToolDefinition(),
                CreatePackagesToolDefinition())
        };

        return CreateResponse(id, result);
    }

    private static JsonObject CreateSummaryToolDefinition()
        => CreateToolDefinition(
            ToolSummary,
            "Get architectural overview of the codebase (hub types, domain clusters, suggested queries). READ THIS FIRST before other queries — it provides free orientation that saves multiple discovery calls.",
            new JsonObject());

    private static JsonObject CreateQueryToolDefinition()
        => CreateToolDefinition(
            ToolQuery,
            "Query the code graph for structural relationships (calls, implements, DI wiring, inheritance). TIP: Start with codegraph_summary for orientation, then use this for specific symbols. BEST FOR: 'What calls X?', 'What implements Y?', 'How is Z wired in DI?' Use include_source=true only AFTER narrowing to a specific method/type when you need implementation details — snippets are capped at 20 lines for token safety. NOT FOR: Broad text search or reading entire files — use file reading/grep for those.",
            new JsonObject
            {
                ["symbol"] = CreateStringProperty("Symbol name or pattern to search for. Supports wildcards (*). Examples: 'OrderService', 'IOrder*', 'type:OrderService'"),
                ["depth"] = CreateIntegerProperty("Traversal depth from matched nodes (0 = node only, 1 = direct neighbors)", DefaultQueryDepth),
                ["kind"] = CreateEnumStringProperty(
                    "Edge type filter",
                    null,
                    "calls-to",
                    "calls-from",
                    "inherits",
                    "implements",
                    "depends-on",
                    "resolves-to",
                    "covers",
                    "covered-by",
                    "references",
                    "overrides",
                    "contains",
                    "handles-route",
                    "binds-configuration",
                    "uses-middleware",
                    "maps-to-table",
                    "navigates-to",
                    "configured-by",
                    DefaultAllKind),
                ["namespace"] = CreateStringProperty("Namespace filter (wildcards OK)"),
                ["project"] = CreateStringProperty("Project/assembly filter"),
                ["format"] = CreateEnumStringProperty(
                    "Output format. compact=minimal tokens (recommended), context=rich detail, json=structured, text=plain",
                    DefaultCompactFormat,
                    "compact",
                    "context",
                    "json",
                    "text"),
                ["max_nodes"] = CreateIntegerProperty("Maximum nodes to return", DefaultMaxNodes),
                ["include_external"] = CreateBooleanProperty("Include external dependency nodes", false),
                ["solution"] = CreateStringProperty("Scope query to a specific solution (multi-solution support). Uses the solution name without extension."),
                ["mode"] = CreateEnumStringProperty(
                    "Query traversal mode. focused=high-signal edges only (default, recommended), structural=includes containment, all=everything",
                    DefaultFocusedMode,
                    DefaultFocusedMode,
                    "structural",
                    DefaultAllKind),
                ["confidence"] = CreateEnumStringProperty(
                    "Minimum confidence level for edges. verified=only verified, inferred=verified+inferred, unresolved=all (default: all)",
                    null,
                    "verified",
                    "inferred",
                    "unresolved"),
                ["budget"] = CreateIntegerProperty("Maximum token budget for output. Output is truncated with a hint when exceeded."),
                ["include_source"] = CreateBooleanProperty("Embed source code snippets (up to 20 lines) for queried nodes. Set true only AFTER narrowing to a specific symbol when you need implementation details. Snippets include file path and line range. If truncated, output shows how to read the full source.", false)
            },
            "symbol");

    private static JsonObject CreateListToolDefinition()
        => CreateToolDefinition(
            ToolList,
            "Browse the code graph hierarchy. Use to discover assemblies, types, interfaces, or namespaces. BEST FOR: 'What projects exist?', 'What types are in module X?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
            new JsonObject
            {
                ["scope"] = CreateEnumStringProperty("What to list (default: assemblies)", null, DefaultAssembliesScope, "types", "interfaces", "namespaces"),
                ["assembly"] = CreateStringProperty("Filter by assembly name"),
                ["top"] = CreateIntegerProperty("Max results (default: 50)", DefaultListTop),
                ["skip"] = CreateIntegerProperty("Skip N results for pagination", DefaultListSkip),
                ["filter"] = CreateStringProperty("Filter by name substring (case-insensitive)")
            });

    private static JsonObject CreateSearchToolDefinition()
        => CreateToolDefinition(
            ToolSearch,
            "Search for symbols by name, namespace, or file path. Use for discovery when you don't know exact names. BEST FOR: 'Find things related to payments', 'What types exist in the auth module?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
            new JsonObject
            {
                ["query"] = CreateStringProperty("Search term (matches name, namespace, file path)"),
                ["top"] = CreateIntegerProperty("Max results (default: 20)", DefaultSearchTop),
                ["kind"] = CreateEnumStringProperty("Filter by node kind (default: all)", DefaultAllKind, DefaultTypeKind, "method", "namespace", DefaultAllKind)
            },
            "query");

    private static JsonObject CreatePathToolDefinition()
        => CreateToolDefinition(
            ToolPath,
            "Find the shortest dependency path between two symbols in the code graph. BEST FOR: 'How are A and B connected?', 'What's the call chain from X to Y?'",
            new JsonObject
            {
                ["from"] = CreateStringProperty("Source symbol name or pattern"),
                ["to"] = CreateStringProperty("Target symbol name or pattern"),
                ["maxDepth"] = CreateIntegerProperty("Maximum search depth (default: 10)", DefaultPathDepth)
            },
            "from",
            "to");

    private static JsonObject CreateImpactToolDefinition()
        => CreateToolDefinition(
            ToolImpact,
            "Analyze the impact of changing a symbol by finding all dependents (reverse traversal). BEST FOR: 'What breaks if I change X?', 'What's the blast radius of this change?'",
            new JsonObject
            {
                ["symbol"] = CreateStringProperty("Symbol name or pattern to analyze impact for"),
                ["depth"] = CreateIntegerProperty("Reverse traversal depth (default: 3)", DefaultImpactDepth)
            },
            "symbol");

    private static JsonObject CreateExplainToolDefinition()
        => CreateToolDefinition(
            ToolExplain,
            "Get a comprehensive view of a single symbol: type, location, signature, members, all edges, and test coverage. BEST FOR: 'Tell me everything about X', 'What does X look like structurally?'",
            new JsonObject
            {
                ["symbol"] = CreateStringProperty("Symbol name or pattern to explain")
            },
            "symbol");

    private static JsonObject CreateFileToolDefinition()
        => CreateToolDefinition(
            ToolFile,
            "Find all symbols defined in a file path. Use when you know the file but not the symbol names.",
            new JsonObject
            {
                ["path"] = CreateStringProperty("File path (partial match OK, e.g., 'OrderService.cs')"),
                ["kind"] = CreateEnumStringProperty("Filter by node kind (default: type)", null, DefaultTypeKind, "method", DefaultAllKind)
            },
            "path");

    private static JsonObject CreateBatchToolDefinition()
        => CreateToolDefinition(
            ToolBatch,
            "Query multiple symbols in one call. Returns combined results with shared context. More efficient than separate queries. BEST FOR: 'Compare these 3 services', 'Show all related types together'.",
            new JsonObject
            {
                ["symbols"] = CreateStringArrayProperty("List of symbol names/patterns to query"),
                ["depth"] = CreateIntegerProperty("Traversal depth (default: 1)", DefaultQueryDepth),
                ["format"] = CreateEnumStringProperty("Output format (default: compact)", DefaultCompactFormat, "compact", "context", "json", "text"),
                ["mode"] = CreateEnumStringProperty("Query traversal mode (default: focused)", DefaultFocusedMode, DefaultFocusedMode, "structural", DefaultAllKind)
            },
            "symbols");

    private static JsonObject CreateCompareToolDefinition()
        => CreateToolDefinition(
            ToolCompare,
            "Compare two symbols structurally. BEST FOR: 'What's different between ServiceA and ServiceB?', 'Compare implementations of interface X'. Shows shared interfaces/bases, unique dependencies, and structural differences.",
            new JsonObject
            {
                ["symbolA"] = CreateStringProperty("First symbol to compare"),
                ["symbolB"] = CreateStringProperty("Second symbol to compare"),
                ["depth"] = CreateIntegerProperty("Traversal depth for each (default: 1)", DefaultQueryDepth)
            },
            "symbolA",
            "symbolB");

    private static JsonObject CreateTestImpactToolDefinition()
        => CreateToolDefinition(
            ToolTestImpact,
            "Analyze test coverage for a symbol. Shows direct tests (CoveredBy edges), indirect tests (via call chains), uncovered callers, and suggests a 'dotnet test --filter' command. BEST FOR: 'What tests cover X?', 'Is this method tested?', 'What tests should I run after changing X?'",
            new JsonObject
            {
                ["symbol"] = CreateStringProperty("Symbol name or pattern to analyze test coverage for"),
                ["depth"] = CreateIntegerProperty("Backward traversal depth for indirect coverage (default: 3)", DefaultImpactDepth)
            },
            "symbol");

    private static JsonObject CreateDiffToolDefinition()
        => CreateToolDefinition(
            ToolDiff,
            "Compare two graph snapshots to find structural changes. BEST FOR: 'What changed between branches?', 'PR review of structural changes', 'What types/edges were added or removed?'",
            new JsonObject
            {
                ["base"] = CreateStringProperty("Path to the base graph directory (e.g., '.codegraph\\snapshots\\main')"),
                ["head"] = CreateStringProperty("Path to the head graph directory (default: '.codegraph')", DefaultGraphSnapshotPath),
                ["format"] = CreateStringProperty("Diff output format: compact, context, text, or json", DefaultCompactFormat)
            },
            "base");

    private static JsonObject CreatePackagesToolDefinition()
        => CreateToolDefinition(
            ToolPackages,
            "Analyze NuGet package usage across the solution. Shows which packages each project uses, how many internal types reference them, and detects version conflicts. BEST FOR: 'What packages does this project use?', 'Are there version conflicts?', 'Why is this package referenced?'",
            new JsonObject
            {
                ["project"] = CreateStringProperty("Filter to a specific project name"),
                ["package"] = CreateStringProperty("Filter to a specific package name")
            });

    private static JsonObject CreateToolDefinition(string name, string description, JsonObject properties, params string[] required)
    {
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Length > 0)
        {
            inputSchema["required"] = CreateStringArray(required);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonObject CreateStringProperty(string description, string? defaultValue = null)
    {
        var property = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description
        };

        if (defaultValue is not null)
        {
            property["default"] = defaultValue;
        }

        return property;
    }

    private static JsonObject CreateIntegerProperty(string description, int? defaultValue = null)
    {
        var property = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description
        };

        if (defaultValue is not null)
        {
            property["default"] = defaultValue.Value;
        }

        return property;
    }

    private static JsonObject CreateBooleanProperty(string description, bool? defaultValue = null)
    {
        var property = new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = description
        };

        if (defaultValue is not null)
        {
            property["default"] = defaultValue.Value;
        }

        return property;
    }

    private static JsonObject CreateEnumStringProperty(string description, string? defaultValue, params string[] values)
    {
        var property = CreateStringProperty(description, defaultValue);
        property["enum"] = CreateStringArray(values);
        return property;
    }

    private static JsonObject CreateStringArrayProperty(string description)
        => new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "string"
            },
            ["description"] = description
        };

    private static JsonArray CreateStringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    #endregion
}
