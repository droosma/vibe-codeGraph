using System.Text.Json.Nodes;
using CodeGraph.Indexer.Mcp;

namespace CodeGraph.Indexer.Tests.Mcp;

public sealed class McpServerToolDefinitionExactTests : IDisposable
{
    private readonly string _graphDir;

    public McpServerToolDefinitionExactTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerToolDefinitionExactTests).Assembly.Location)!,
            "McpToolDefs_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
        {
            Directory.Delete(_graphDir, recursive: true);
        }
    }

    [Fact]
    public async Task HandleToolsList_ReturnsToolsInExpectedOrder()
    {
        var tools = await GetToolsAsync();

        Assert.Equal(
            [
                "codegraph_summary",
                "codegraph_query",
                "codegraph_list",
                "codegraph_search",
                "codegraph_path",
                "codegraph_impact",
                "codegraph_explain",
                "codegraph_file",
                "codegraph_batch",
                "codegraph_compare",
                "codegraph_test_impact",
                "codegraph_diff",
                "codegraph_packages"
            ],
            tools.Select(tool => tool!["name"]!.GetValue<string>()).ToArray());
    }

    [Theory]
    [MemberData(nameof(ExpectedToolDefinitions))]
    public async Task HandleToolsList_ReturnsExactToolDefinition(string toolName, JsonObject expectedDefinition)
    {
        var tools = await GetToolsAsync();
        var actualDefinition = tools.Single(tool => tool!["name"]!.GetValue<string>() == toolName);

        Assert.Equal(expectedDefinition.ToJsonString(), actualDefinition!.ToJsonString());
    }

    public static TheoryData<string, JsonObject> ExpectedToolDefinitions =>
        new()
        {
            {
                "codegraph_summary",
                Tool(
                    "codegraph_summary",
                    "Get architectural overview of the codebase (hub types, domain clusters, suggested queries). READ THIS FIRST before other queries — it provides free orientation that saves multiple discovery calls.",
                    Props())
            },
            {
                "codegraph_query",
                Tool(
                    "codegraph_query",
                    "Query the code graph for structural relationships (calls, implements, DI wiring, inheritance). TIP: Start with codegraph_summary for orientation, then use this for specific symbols. BEST FOR: 'What calls X?', 'What implements Y?', 'How is Z wired in DI?' Use include_source=true only AFTER narrowing to a specific method/type when you need implementation details — snippets are capped at 20 lines for token safety. NOT FOR: Broad text search or reading entire files — use file reading/grep for those.",
                    Props(
                        ("symbol", StringProp("Symbol name or pattern to search for. Supports wildcards (*). Examples: 'OrderService', 'IOrder*', 'type:OrderService'")),
                        ("depth", IntegerProp("Traversal depth from matched nodes (0 = node only, 1 = direct neighbors)", 1)),
                        ("kind", EnumStringProp(
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
                            "all")),
                        ("namespace", StringProp("Namespace filter (wildcards OK)")),
                        ("project", StringProp("Project/assembly filter")),
                        ("format", EnumStringProp(
                            "Output format. compact=minimal tokens (recommended), context=rich detail, json=structured, text=plain",
                            "compact",
                            "compact",
                            "context",
                            "json",
                            "text")),
                        ("max_nodes", IntegerProp("Maximum nodes to return", 50)),
                        ("include_external", BooleanProp("Include external dependency nodes", false)),
                        ("solution", StringProp("Scope query to a specific solution (multi-solution support). Uses the solution name without extension.")),
                        ("mode", EnumStringProp(
                            "Query traversal mode. focused=high-signal edges only (default, recommended), structural=includes containment, all=everything",
                            "focused",
                            "focused",
                            "structural",
                            "all")),
                        ("confidence", EnumStringProp(
                            "Minimum confidence level for edges. verified=only verified, inferred=verified+inferred, unresolved=all (default: all)",
                            null,
                            "verified",
                            "inferred",
                            "unresolved")),
                        ("budget", IntegerProp("Maximum token budget for output. Output is truncated with a hint when exceeded.")),
                        ("include_source", BooleanProp("Embed source code snippets (up to 20 lines) for queried nodes. Set true only AFTER narrowing to a specific symbol when you need implementation details. Snippets include file path and line range. If truncated, output shows how to read the full source.", false))),
                    "symbol")
            },
            {
                "codegraph_list",
                Tool(
                    "codegraph_list",
                    "Browse the code graph hierarchy. Use to discover assemblies, types, interfaces, or namespaces. BEST FOR: 'What projects exist?', 'What types are in module X?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
                    Props(
                        ("scope", EnumStringProp("What to list (default: assemblies)", null, "assemblies", "types", "interfaces", "namespaces")),
                        ("assembly", StringProp("Filter by assembly name")),
                        ("top", IntegerProp("Max results (default: 50)", 50)),
                        ("skip", IntegerProp("Skip N results for pagination", 0)),
                        ("filter", StringProp("Filter by name substring (case-insensitive)"))))
            },
            {
                "codegraph_search",
                Tool(
                    "codegraph_search",
                    "Search for symbols by name, namespace, or file path. Use for discovery when you don't know exact names. BEST FOR: 'Find things related to payments', 'What types exist in the auth module?' NOT FOR: Searching inside method bodies or comments — use grep for those.",
                    Props(
                        ("query", StringProp("Search term (matches name, namespace, file path)")),
                        ("top", IntegerProp("Max results (default: 20)", 20)),
                        ("kind", EnumStringProp("Filter by node kind (default: all)", "all", "type", "method", "namespace", "all"))),
                    "query")
            },
            {
                "codegraph_path",
                Tool(
                    "codegraph_path",
                    "Find the shortest dependency path between two symbols in the code graph. BEST FOR: 'How are A and B connected?', 'What's the call chain from X to Y?'",
                    Props(
                        ("from", StringProp("Source symbol name or pattern")),
                        ("to", StringProp("Target symbol name or pattern")),
                        ("maxDepth", IntegerProp("Maximum search depth (default: 10)", 10))),
                    "from",
                    "to")
            },
            {
                "codegraph_impact",
                Tool(
                    "codegraph_impact",
                    "Analyze the impact of changing a symbol by finding all dependents (reverse traversal). BEST FOR: 'What breaks if I change X?', 'What's the blast radius of this change?'",
                    Props(
                        ("symbol", StringProp("Symbol name or pattern to analyze impact for")),
                        ("depth", IntegerProp("Reverse traversal depth (default: 3)", 3))),
                    "symbol")
            },
            {
                "codegraph_explain",
                Tool(
                    "codegraph_explain",
                    "Get a comprehensive view of a single symbol: type, location, signature, members, all edges, and test coverage. BEST FOR: 'Tell me everything about X', 'What does X look like structurally?'",
                    Props(("symbol", StringProp("Symbol name or pattern to explain"))),
                    "symbol")
            },
            {
                "codegraph_file",
                Tool(
                    "codegraph_file",
                    "Find all symbols defined in a file path. Use when you know the file but not the symbol names.",
                    Props(
                        ("path", StringProp("File path (partial match OK, e.g., 'OrderService.cs')")),
                        ("kind", EnumStringProp("Filter by node kind (default: type)", null, "type", "method", "all"))),
                    "path")
            },
            {
                "codegraph_batch",
                Tool(
                    "codegraph_batch",
                    "Query multiple symbols in one call. Returns combined results with shared context. More efficient than separate queries. BEST FOR: 'Compare these 3 services', 'Show all related types together'.",
                    Props(
                        ("symbols", StringArrayProp("List of symbol names/patterns to query")),
                        ("depth", IntegerProp("Traversal depth (default: 1)", 1)),
                        ("format", EnumStringProp("Output format (default: compact)", "compact", "compact", "context", "json", "text")),
                        ("mode", EnumStringProp("Query traversal mode (default: focused)", "focused", "focused", "structural", "all"))),
                    "symbols")
            },
            {
                "codegraph_compare",
                Tool(
                    "codegraph_compare",
                    "Compare two symbols structurally. BEST FOR: 'What's different between ServiceA and ServiceB?', 'Compare implementations of interface X'. Shows shared interfaces/bases, unique dependencies, and structural differences.",
                    Props(
                        ("symbolA", StringProp("First symbol to compare")),
                        ("symbolB", StringProp("Second symbol to compare")),
                        ("depth", IntegerProp("Traversal depth for each (default: 1)", 1))),
                    "symbolA",
                    "symbolB")
            },
            {
                "codegraph_test_impact",
                Tool(
                    "codegraph_test_impact",
                    "Analyze test coverage for a symbol. Shows direct tests (CoveredBy edges), indirect tests (via call chains), uncovered callers, and suggests a 'dotnet test --filter' command. BEST FOR: 'What tests cover X?', 'Is this method tested?', 'What tests should I run after changing X?'",
                    Props(
                        ("symbol", StringProp("Symbol name or pattern to analyze test coverage for")),
                        ("depth", IntegerProp("Backward traversal depth for indirect coverage (default: 3)", 3))),
                    "symbol")
            },
            {
                "codegraph_diff",
                Tool(
                    "codegraph_diff",
                    "Compare two graph snapshots to find structural changes. BEST FOR: 'What changed between branches?', 'PR review of structural changes', 'What types/edges were added or removed?'",
                    Props(
                        ("base", StringProp("Path to the base graph directory (e.g., '.codegraph\\snapshots\\main')")),
                        ("head", StringProp("Path to the head graph directory (default: '.codegraph')", ".codegraph")),
                        ("format", StringProp("Diff output format: compact, context, text, or json", "compact"))),
                    "base")
            },
            {
                "codegraph_packages",
                Tool(
                    "codegraph_packages",
                    "Analyze NuGet package usage across the solution. Shows which packages each project uses, how many internal types reference them, and detects version conflicts. BEST FOR: 'What packages does this project use?', 'Are there version conflicts?', 'Why is this package referenced?'",
                    Props(
                        ("project", StringProp("Filter to a specific project name")),
                        ("package", StringProp("Filter to a specific package name"))))
            }
        };

    private async Task<JsonArray> GetToolsAsync()
    {
        var server = new McpServer(_graphDir);
        var response = await server.HandleMessageAsync(MakeRequest("tools/list", id: JsonValue.Create(1)));
        return response!["result"]!["tools"]!.AsArray();
    }

    private static JsonNode MakeRequest(string method, JsonNode? id = null, JsonNode? @params = null)
    {
        var message = new JsonObject { ["method"] = method };
        if (id is not null)
        {
            message["id"] = id.DeepClone();
        }

        if (@params is not null)
        {
            message["params"] = @params.DeepClone();
        }

        return message;
    }

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required)
    {
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Length > 0)
        {
            inputSchema["required"] = StringArray(required);
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonObject Props(params (string Name, JsonObject Property)[] properties)
    {
        var result = new JsonObject();
        foreach (var (name, property) in properties)
        {
            result[name] = property;
        }

        return result;
    }

    private static JsonObject StringProp(string description, string? defaultValue = null)
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

    private static JsonObject IntegerProp(string description, int? defaultValue = null)
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

    private static JsonObject BooleanProp(string description, bool? defaultValue = null)
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

    private static JsonObject EnumStringProp(string description, string? defaultValue, params string[] values)
    {
        var property = StringProp(description, defaultValue);
        property["enum"] = StringArray(values);
        return property;
    }

    private static JsonObject StringArrayProp(string description) =>
        new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "string"
            },
            ["description"] = description
        };

    private static JsonArray StringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
