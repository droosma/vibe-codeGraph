# Vision: CodeGraph for .NET

## The Problem

AI coding agents are structurally blind. They work with text — grep matches, file contents, regex hits — but have no understanding of how code is actually connected. When an agent needs to understand `OrderService`, it runs `grep "OrderService"` and gets 200 hits across controllers, tests, DI registrations, and unrelated comments. It then reads 10-15 files to build a mental model that a compiler already has.

This is wasteful in two dimensions:

- **Tokens** — every grep result and file read consumes context window. A typical "understand this service" task burns 30,000-40,000 tokens on discovery alone.
- **Time** — the back-and-forth loop of grep → read → grep again → read more takes 50+ tool calls for a moderately complex question.

## The Thesis

.NET has Roslyn — a compiler-as-a-service that understands every type, every call, every interface implementation, every DI registration. The structural information agents need already exists at compile time. CodeGraph extracts it into a queryable graph and serves it in a token-efficient format.

**The bet: if you give agents structural intelligence instead of text search, they use fewer tokens, make fewer mistakes, and need fewer tool calls.**

## Inspiration

[Graphify](https://github.com/safishamsi/graphify) proved the concept for JavaScript/TypeScript — a code graph that LLM agents can query. CodeGraph mirrors that idea but is purpose-built for .NET, leveraging Roslyn's semantic analysis for capabilities that no language-agnostic tool can match:

- Full type hierarchy resolution (not just text matching)
- Interface-to-implementation mapping through DI container analysis
- Test coverage edges (which tests exercise which production code)
- Cross-project dependency tracking within a solution
- Confidence-annotated edges (verified by Roslyn vs. inferred)

## Design Principles

### 1. Tokens are the currency

Every byte of output costs tokens. The tool's primary optimization target is **information density per token**. This drives every formatting decision:

- `compact` format is the MCP default — structural relationships in ~200 tokens vs ~2,000 for raw file content
- `focused` mode filters to high-signal edges only (calls, implements, inherits) — drops noise like `References` and `CoveredBy`
- Cost estimation tells the agent how many tokens a query will consume before executing it

### 2. Meet agents where they think

Agents think in files, not fully-qualified symbol names. They think in questions ("what calls this?", "what tests cover this?"), not graph theory. The tool's query interface must match agent mental models:

- File-path queries: `codegraph query src/Services/OrderService.cs` resolves to symbols automatically
- Fuzzy matching: typos get "did you mean?" suggestions instead of empty results
- Semantic search: broad discovery ("find anything related to payment") without knowing exact names
- Batch queries: ask about 3 related types in one call instead of 3 separate round-trips

### 3. .NET-native intelligence

The tool should answer questions that only a Roslyn-powered tool can answer — things grep will never do reliably:

- "What interface does this implement, and where is it registered in DI?" → `Implements` + `ResolvesTo` edges
- "What tests cover this method?" → `CoveredBy` edges from `TestCoveragePass`
- "If I change this, what breaks?" → `ImpactAnalyzer` forward reachability
- "What's the call chain from controller to database?" → `Calls` edge traversal with depth control

### 4. Graph, not text

The output is structured relationships, not prose. An agent consuming CodeGraph output can programmatically reason about the dependency structure — it doesn't need to parse natural language descriptions of code.

---

## Validated Through Benchmarks

### Methodology

We run A/B benchmarks: two isolated AI agents (sub-agents with separate context windows) are given the **exact same question** about a real production codebase ([Exquise](https://github.com/theNextQse/exquise), ~100K graph nodes). One agent has CodeGraph installed and available as a tool; the other has only grep, glob, and file view. Both run to completion, then an orchestrator compares the results for precision, recall, and token cost.

The benchmark question used: **"Where in this codebase is Office interop used?"** — a structural discovery question that requires tracing through namespaces, types, and dependencies.

### Results Across Versions

| Version | CG Time | CG Tool Calls | CG File Reads | Grep Time | Grep Tool Calls | Token Ratio |
|---------|---------|---------------|---------------|-----------|-----------------|-------------|
| v0.2.0  | 469s    | ~50           | ~30           | 271s      | ~55             | ~4.2× better |
| v0.3.0  | 388s    | ~40           | 16            | 200s      | ~60             | ~2× better |
| v0.4.0  | 399s    | ~35           | 12            | 220s      | ~50             | ~2× better |

### v0.4.0 Detailed Token Analysis

| Metric | With CodeGraph | Without (grep/view) | Improvement |
|--------|---------------|---------------------|-------------|
| Token consumption | ~18,600 | ~39,200 | **2.1× fewer tokens** |
| File reads needed | 12 | 49 | **4× fewer file reads** |
| Tool calls | 35 | 50 | **30% fewer calls** |
| Discovery accuracy | High (structural) | Medium (text-based) | Fewer false positives |

### Key Findings

1. **`search` was the game-changer in v0.4.0** — 20 of 45 tool calls used semantic search. Agents love broad discovery queries ("find anything related to X") and `codegraph_search` handles this natively.

2. **`compare` was used** — the agent used it to compare two related types in one call, replacing what previously took multiple queries.

3. **Zero timeouts in v0.4.0** — v0.3.0 had 4 `list types` timeouts that forced fallback to grep. Pagination (`--top`, `--skip`) fixed this completely.

4. **Wall time is ~1.8× slower** — this is entirely due to **process spawning**. Each CLI invocation starts a new .NET process (~200ms) and reloads the graph from SQLite (~500ms). The MCP server mode (persistent process) eliminates this, but benchmark agents defaulted to CLI. This is the #1 remaining problem ([#98](https://github.com/droosma/vibe-codeGraph/issues/98)).

5. **Token efficiency plateaued at ~2×** — v0.3.0 and v0.4.0 both show ~2× token savings. The next big jump will come from .NET-specific intelligence (routes, entities, etc.) that eliminates entire categories of discovery queries.

6. **`--include-source` was never used by the agent** — despite being available. This suggests tool descriptions need better prompting, or agents don't naturally reach for "give me the source code inline."

### How to Run Benchmarks

The benchmark is not automated — it's run manually using sub-agents with isolated context:

1. Install CodeGraph globally: `dotnet tool update -g CodeGraph`
2. Index the target codebase: `codegraph index --sln <path-to-sln>`
3. Launch two sub-agents with the same question — one with CodeGraph tools available, one without
4. Compare: precision (did both find the same code?), recall (did one miss things?), token cost, tool call count, wall time

A future improvement would be an automated benchmark harness, but the manual approach gives richer qualitative insights about agent behavior.

---

## Architecture & Codebase Guide

> This section is for agents picking up development work on CodeGraph itself.

### Solution Structure

```
CodeGraph.sln
├── src/
│   ├── CodeGraph.Core/           # Graph model, storage, configuration (no Roslyn dependency)
│   │   ├── Models/               # GraphNode, GraphEdge, NodeKind, EdgeType, Accessibility
│   │   ├── IO/                   # GraphReader, GraphWriter, SQLite storage
│   │   │   └── Sqlite/           # SQLite-specific read/write implementation
│   │   └── Configuration/        # CodeGraphConfig, ConfigLoader (codegraph.json)
│   │
│   ├── CodeGraph.Query/          # All query logic, formatters, analyzers (no Roslyn dependency)
│   │   ├── QueryEngine.cs        # Core: loads graph, runs queries, search, compare, fuzzy match
│   │   ├── ImpactAnalyzer.cs     # Forward reachability ("what breaks if I change X?")
│   │   ├── PathFinder.cs         # Shortest path between two symbols
│   │   ├── ListEngine.cs         # Paginated type listing with --top/--skip/--filter
│   │   ├── SymbolExplainer.cs    # Natural-language symbol explanation
│   │   ├── QuerySessionTracker.cs # Tracks what was queried in an MCP session
│   │   ├── QuerySuggestionGenerator.cs # Suggests next queries based on results
│   │   ├── GraphDiffEngine.cs    # Structural diff between two graph snapshots
│   │   ├── Filters/              # EdgeTypeFilter, DepthFilter, NamespaceFilter, RankingStrategy
│   │   ├── OutputFormatters/     # CompactFormatter, ContextFormatter, ExplainFormatter, etc.
│   │   ├── Metrics/              # QueryCostEstimate, CompressionCalculator
│   │   ├── Report/               # HubAnalyzer, CoverageAnalyzer, DomainClusterAnalyzer
│   │   └── Wiki/                 # Markdown wiki generation (assembly pages, DI wiring, etc.)
│   │
│   └── CodeGraph.Indexer/        # CLI entry point, Roslyn passes, MCP server
│       ├── Program.cs            # CLI dispatch (index, query, search, compare, mcp, etc.)
│       ├── Passes/               # Roslyn analysis passes (SyntaxPass, SemanticPass, DiPass, TestCoveragePass)
│       ├── Mcp/McpServer.cs      # MCP stdio server — 10 tools, graph caching, session tracking
│       ├── Init/                 # Agent template generation (Claude, Copilot, Cursor configs)
│       ├── View/                 # 3D HTML graph visualization
│       └── Workspace/            # Solution/project parsing, compilation factory
│
├── tests/
│   ├── CodeGraph.Core.Tests/     # Model, IO, configuration tests
│   ├── CodeGraph.Query.Tests/    # Query engine, formatters, analyzers, wiki tests
│   ├── CodeGraph.Indexer.Tests/  # Pass tests, MCP tests, workspace tests
│   └── CodeGraph.Integration.Tests/ # Cross-solution integration tests
│
├── docs/                         # Documentation (auto-generated + manual)
├── skills/                       # Agent skill definitions (skills.sh format)
└── .github/workflows/            # CI, publish, mutation testing, doc automation
```

### Layer Boundaries

```
CodeGraph.Indexer (CLI + Roslyn)
    ↓ depends on
CodeGraph.Query (query logic, formatters)
    ↓ depends on
CodeGraph.Core (models, storage)
```

**Never reverse the dependency direction.** Core knows nothing about Query. Query knows nothing about Indexer or Roslyn. This is enforced by project references.

### Graph Model

The graph consists of **nodes** and **edges** stored in SQLite:

**Nodes** (`GraphNode` record):
- `Id` — fully qualified name (e.g., `MyApp.Services.OrderService`)
- `Name` — short name (e.g., `OrderService`)
- `Kind` — `Namespace | Type | Method | Property | Field | Event | Constructor`
- `FilePath` — relative path to source file
- `StartLine` / `EndLine` — source location
- `Signature` — full C# signature
- `DocComment` — XML doc comment (nullable, currently not surfaced in compact format — see [#93](https://github.com/droosma/vibe-codeGraph/issues/93))
- `Accessibility` — `Public | Internal | Protected | Private | ProtectedInternal | PrivateProtected`
- `AssemblyName` — project/assembly this node belongs to

**Edges** (`GraphEdge` record):
- `FromId` / `ToId` — node references
- `Type` — `Contains | Calls | Inherits | Implements | DependsOn | ResolvesTo | Covers | CoveredBy | References | Overrides`
- `IsExternal` — true if target is from a NuGet package
- `Confidence` — `Verified | Inferred | Unresolved`
- `Metadata` — arbitrary key-value pairs (e.g., DI lifetime, package source)

### Indexing Pipeline

The indexing pipeline runs in `Program.cs` → `RunIndexAsync()`:

1. **Parse solution** — `SolutionParser` finds all `.csproj` files
2. **Compile projects** — `CompilationFactory` creates `CSharpCompilation` for each project
3. **SyntaxPass** — walks syntax trees, creates nodes for all type members
4. **SemanticPass** — walks syntax trees again with semantic model, creates call/inheritance/dependency edges
5. **DiPass** — finds `AddScoped<I, T>()` patterns, creates `ResolvesTo` edges
6. **TestCoveragePass** — finds test methods, traces their calls to production code, creates `Covers`/`CoveredBy` edges
7. **Write graph** — `GraphWriter` persists to SQLite in `.codegraph/` directory

Each pass follows the same pattern:
```csharp
public (List<GraphEdge> Edges, List<GraphNode> ExternalNodes) Execute(
    CSharpCompilation compilation,
    string solutionRoot,
    HashSet<string> knownNodeIds)
```

**When adding a new pass**, follow this exact signature. The `knownNodeIds` set lets the pass distinguish internal types from external (NuGet) types.

### MCP Server Architecture

`McpServer.cs` is a JSON-RPC 2.0 stdio server that:

1. Reads Content-Length-framed (or bare newline) JSON from stdin
2. Dispatches to tool handlers based on `params.name`
3. Writes JSON-RPC responses to stdout

Key design decisions:
- **Graph caching**: `GetOrLoadEngineAsync()` caches the `QueryEngine` across tool calls within a session. Only reloads when the solution filter changes.
- **Session tracking**: `QuerySessionTracker` records what was queried, so `QuerySuggestionGenerator` can suggest unexplored neighbors.
- **Cost metadata**: every response includes `[Cost: N nodes, M edges, ~T tokens]` footer.
- **Default format is `compact`**, default mode is `focused`** — these were changed from `context`/`all` in v0.2.2 because MCP agents were wasting 3-5× tokens per query.

### Output Format Design

The `compact` format is the most critical — it's what MCP agents see by default:

```
→ OrderService (Type)  [src/Orders/OrderService.cs:14-87]
    implements IOrderService
    calls IPaymentGateway.Charge()
    calls IOrderRepository.Save()
    calls ILogger.LogInformation()
    resolvedVia AddScoped<IOrderService, OrderService>
```

Design rules for compact output:
- One line per relationship, indented under the target symbol
- File path and line range in brackets
- No XML, no JSON, no markdown headers — pure structural text
- Edge type as lowercase verb prefix (`calls`, `implements`, `inherits`, `resolvedVia`)
- Truncated by `BudgetTruncator` if result exceeds token budget

### Testing Patterns

All passes are tested using inline C# compilations — no disk I/O needed:

```csharp
var code = @"
public interface IOrderService { void PlaceOrder(); }
public class OrderService : IOrderService { public void PlaceOrder() { } }
";
var compilation = CSharpCompilation.Create("Test")
    .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code))
    .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

var pass = new SyntaxPass();
var (nodes, edges) = pass.Execute(compilation, "/", new HashSet<string>());

Assert.Contains(nodes, n => n.Name == "OrderService" && n.Kind == NodeKind.Type);
```

This pattern is used in `DiPassTests`, `SemanticPassTests`, `SyntaxPassTests`, and `TestCoveragePassTests`. Follow it for any new pass.

### CI/CD Workflows

- **ci.yml** — builds + runs all tests on every push/PR. Multi-target: `net8.0` and `net10.0`.
- **publish.yml** — publishes to NuGet on GitHub release creation. Triggered by `release: published` event.
- **mutation-testing.yml** — runs Stryker mutation testing on PR. Minimum threshold: 60%.
- **update-docs.md** — GitHub Copilot workflow that regenerates docs when code changes.
- **auto-merge-docs.yml** — auto-merges doc PRs created by the update-docs bot. Processes ALL open doc PRs in a loop with rebase + admin merge.
- **agentics-maintenance.yml** — periodic maintenance tasks.

**Known CI quirk**: the auto-merge workflow uses `--admin` to bypass CI checks on bot PRs. This is necessary because `GITHUB_TOKEN` can't trigger workflows on PRs it creates, so CI checks never run on bot doc PRs. The `--admin` flag is safe here because the content is auto-generated documentation.

### Configuration

`codegraph.json` in the project root configures indexing:

```json
{
  "solutions": ["MyApp.sln"],
  "outputDirectory": ".codegraph",
  "excludePatterns": ["**/obj/**", "**/bin/**"]
}
```

Multi-solution support is available via `codegraph.multi-solution.json`. The `ConfigLoader` handles both formats.

### How to Add a New Feature

The typical pattern for adding a new capability:

1. **If it needs Roslyn** — create a new pass in `src/CodeGraph.Indexer/Passes/`. Follow the existing pass signature. May need new `EdgeType` values in `GraphEdge.cs`.
2. **If it's a query** — add to `QueryEngine.cs` or create a new analyzer in `src/CodeGraph.Query/`.
3. **CLI command** — add a case to the `switch` in `Program.cs` → `RunAsync()`, implement `RunXxxAsync()`.
4. **MCP tool** — add a handler in `McpServer.cs`, add the tool definition to the `tools/list` response, add routing hints to the tool description.
5. **Tests** — add tests in the matching test project. Use inline compilations for pass tests.
6. **Agent templates** — update `AgentTemplates.cs` if the feature should appear in generated agent instructions.

---

## What Exists Today (v0.4.0)

### Indexing Passes
- **SyntaxPass** — types, methods, properties, fields, constructors, events
- **SemanticPass** — calls, inheritance, implements, dependencies, references, overrides
- **DiPass** — DI container registrations (AddScoped/AddTransient/AddSingleton → ResolvesTo edges)
- **TestCoveragePass** — test-to-production coverage (xUnit, NUnit, MSTest)

### Edge Types
`Contains`, `Calls`, `Inherits`, `Implements`, `DependsOn`, `ResolvesTo`, `Covers`, `CoveredBy`, `References`, `Overrides`

### Query Capabilities
- Symbol query with depth control and format selection
- File-path queries (resolve file → symbols → graph)
- Batch queries (multiple symbols, single graph load)
- Semantic search (case-insensitive substring across names, namespaces, file paths)
- Compare (shared interfaces/bases, unique edges between two symbols)
- Impact analysis (forward reachability — "what breaks if I change X?")
- Explain (natural-language summary of a symbol's role)
- Cost estimation (predict token count before executing)
- Fuzzy matching with Levenshtein distance suggestions

### Output Formats
- `compact` — minimal structural output optimized for tokens (MCP default)
- `context` — full detail with signatures, file paths, doc comments
- `explain` — natural-language explanation

### MCP Server
10 tools: `codegraph_summary`, `codegraph_query`, `codegraph_file`, `codegraph_batch`, `codegraph_search`, `codegraph_compare`, `codegraph_path`, `codegraph_impact`, `codegraph_explain`, `codegraph_list`

### Additional Features
- Graph caching across MCP tool calls within a session
- Session-aware suggestions (tracks what was already queried)
- Domain cluster analysis (groups assemblies by namespace)
- 3D interactive graph visualization (HTML export)
- Agent instruction generation (Claude, Copilot, Cursor, generic)

### Test Suite
3,624 tests across 4 test projects (xUnit, multi-target net8.0/net10.0):
- Core model and storage tests
- Inline-compilation pass tests (SyntaxPass, SemanticPass, DiPass, TestCoverage)
- Formatter tests (Compact, Context, Explain, Path, Impact, Compare, Diff)
- Mutation tests (systematically killing mutants for high confidence)
- Query engine tests (search, fuzzy match, batch, file-path resolution)
- MCP protocol tests
- Integration tests (cross-solution linking)

---

## Where We're Going

### Deeper .NET Intelligence

The current passes extract universal code structure. The next wave extracts .NET-framework-specific intelligence that no language-agnostic tool can provide:

- **ASP.NET route mapping** — `[HttpGet("/api/orders/{id}")]` → handler method → service → repository. Answer "what handles this endpoint?" in one query. ([#89](https://github.com/droosma/vibe-codeGraph/issues/89))
- **EF Core entity modeling** — DbContext → entities → table names → relationships. Answer "what's the data model?" without reading configuration files. ([#92](https://github.com/droosma/vibe-codeGraph/issues/92))
- **Configuration binding** — `IOptions<T>` → configuration section mapping. Answer "what settings does this service need?" ([#90](https://github.com/droosma/vibe-codeGraph/issues/90))
- **Middleware pipeline** — ordered extraction of the request pipeline. ([#91](https://github.com/droosma/vibe-codeGraph/issues/91))

### Unique Capabilities

Features that make CodeGraph the only tool of its kind for .NET:

- **Test impact analysis** — "I changed `OrderService.PlaceOrder()`, which tests should I run?" Using call graph + test coverage edges for precise test selection without runtime instrumentation. ([#97](https://github.com/droosma/vibe-codeGraph/issues/97))
- **Graph diff** — "What changed structurally between these two commits?" Shows added/removed types, changed edges, DI registration changes. ([#94](https://github.com/droosma/vibe-codeGraph/issues/94))
- **NuGet dependency intelligence** — package-level usage analysis and version conflict detection. ([#95](https://github.com/droosma/vibe-codeGraph/issues/95))

### Performance

- **Persistent daemon** — eliminate process spawning overhead, make wall time competitive with grep. ([#98](https://github.com/droosma/vibe-codeGraph/issues/98))
- **Incremental indexing** — re-index only changed files, bring re-index time from 600s to <30s. ([#96](https://github.com/droosma/vibe-codeGraph/issues/96))
- **Doc comments in compact output** — surface XML doc comments without needing file reads. ([#93](https://github.com/droosma/vibe-codeGraph/issues/93))

---

## Evolution History

Understanding why decisions were made helps avoid re-litigating them:

### v0.1.0 → v0.2.0
- Added MCP server mode (`codegraph mcp`) — persistent process, stdin/stdout JSON-RPC
- Added `codegraph_explain` and `codegraph_impact` MCP tools
- Added agent instruction generation (`codegraph init`)
- First A/B benchmark: proved ~4× token savings but high tool call count

### v0.2.0 → v0.2.2
- **Changed MCP defaults**: format `context` → `compact`, mode `all` → `focused`
- This was the single highest-impact change — agents were wasting 3-5× tokens per query because `context` format includes full signatures, all edge types, and verbose headers
- `focused` mode drops `References` and `CoveredBy` edges that add noise for most queries
- CLI defaults were NOT changed (only MCP) to avoid breaking scripts

### v0.2.2 → v0.3.0
- File-path queries ([#74](https://github.com/droosma/vibe-codeGraph/issues/74)) — agents think in files, not symbols
- Batch queries ([#75](https://github.com/droosma/vibe-codeGraph/issues/75)) — 3 symbols in 1 call instead of 3 calls
- Graph caching ([#76](https://github.com/droosma/vibe-codeGraph/issues/76)) — don't reload SQLite on every MCP tool call
- Fuzzy search ([#77](https://github.com/droosma/vibe-codeGraph/issues/77)) — "did you mean?" instead of empty results
- Session tracking ([#79](https://github.com/droosma/vibe-codeGraph/issues/79)) — suggest unexplored neighbors
- Cost estimation ([#80](https://github.com/droosma/vibe-codeGraph/issues/80)) — predict token count before executing
- Benchmark: tool calls dropped from ~50 to ~40, file reads from ~30 to 16, but `list types` had timeout issues

### v0.3.0 → v0.4.0
- Paginated `list types` — fixed the timeout blocker with `--top`/`--skip`/`--filter`
- Semantic `search` — case-insensitive substring across all node fields, ranked by relevance
- `compare` tool — structural comparison of two symbols in one query
- `--include-source` — embed source code snippets in query results (capped at 20 lines)
- Domain cluster analysis — groups assemblies by namespace prefix
- Smart routing hints in MCP tool descriptions — "BEST FOR: ..." / "NOT FOR: ..."
- Benchmark: zero timeouts, `search` became the most-used tool (20 of 45 calls), token ratio stable at ~2×

### Lessons Learned

1. **Defaults matter more than features** — changing format/mode defaults had more impact than adding new tools
2. **Agents don't read documentation** — tool descriptions must be self-explanatory; routing hints in the description work better than external docs
3. **Process spawning dominates wall time** — the graph query itself is fast (<100ms); the .NET cold start + graph load is the bottleneck
4. **Broad discovery queries are the killer app** — `search` was used more than `query`. Agents start broad ("what's related to payments?") and narrow down
5. **Agents default to CLI even when MCP is available** — need to make MCP the path of least resistance
6. **Token savings plateau without domain-specific passes** — structural queries save ~2× tokens, but domain-specific intelligence (routes, entities, DI) would save entire categories of follow-up queries

---

## Success Criteria

The tool succeeds when:

1. **An agent using CodeGraph solves a structural question in fewer tokens than one using grep/view** — validated through A/B benchmarks (currently: 2.1× fewer tokens ✅)
2. **The agent never needs to fall back to file reading for structural understanding** — CodeGraph output is self-sufficient (currently: still needs ~12 file reads ⚠️)
3. **Wall time is competitive with or better than grep-based discovery** — the daemon and caching make structural queries faster than text search (currently: 1.8× slower ❌ — [#98](https://github.com/droosma/vibe-codeGraph/issues/98))
4. **Every .NET-specific question has a CodeGraph answer** — routes, entities, DI, configuration, test coverage, middleware — the full application model is in the graph (currently: DI and test coverage only ⚠️ — [#89](https://github.com/droosma/vibe-codeGraph/issues/89), [#92](https://github.com/droosma/vibe-codeGraph/issues/92), [#90](https://github.com/droosma/vibe-codeGraph/issues/90), [#91](https://github.com/droosma/vibe-codeGraph/issues/91))
