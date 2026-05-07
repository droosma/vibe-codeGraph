# CodeGraph Fleet Implementation Plan

## Pre-requisite: Project Quality Gates (DO THIS FIRST)

Before ANY feature work, establish strict quality enforcement:

### 1. Create `Directory.Build.props` (repo root)

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

### 2. Fix all existing analyzer errors (16 total, 4 unique issues)

After adding `Directory.Build.props`, these errors surface:

| Rule | File | Fix |
|------|------|-----|
| CA1854 | `GraphWriter.cs:88,96` | Replace `ContainsKey` + indexer with `TryGetValue` |
| CA1861 | `GraphWriter.cs:151` | Extract constant array to `static readonly` field |
| CA1822 | `GraphMerger.cs:15` | Make `Merge` method `static` |
| CA1822 | `GraphReader.cs:11` | Make `ReadAsync` method `static` |
| CA1305 | `Polyfills/IndexRange.cs:47` (netstandard2.0 only) | Use `ToString(CultureInfo.InvariantCulture)` |

Currently suppressed: `CS1591` (missing XML docs), `CS9057`. Keep `<NoWarn>CS1591</NoWarn>` in projects that pack as NuGet (CodeGraph.Core, CodeGraph.Indexer, CodeGraph.Query) — these are library projects where full XML docs are aspirational.

### 3. Verify: `dotnet build CodeGraph.sln` and `dotnet test CodeGraph.sln` must pass clean

---

## Agent Instructions & Skills

### Update AGENTS.md with development standards

Add these sections to `AGENTS.md`:

```markdown
## Development Standards

### TDD Workflow (Red-Green-Refactor)

1. **Red** — Write a failing test FIRST that describes the expected behavior
2. **Green** — Write the minimum code to make the test pass
3. **Refactor** — Clean up while keeping tests green

Every PR must include tests. For new features:
- Unit tests in the matching `tests/CodeGraph.*.Tests/` project
- Integration tests in `tests/CodeGraph.Integration.Tests/` for end-to-end scenarios

### Architecture Rules

- **Records for data** — All graph model types are immutable records (`GraphNode`, `GraphEdge`, etc.)
- **No unnecessary dependencies** — Only add NuGet packages when absolutely required
- **Single Responsibility** — Each pass, filter, formatter has one job
- **Testable in isolation** — Passes accept a `CSharpCompilation`, filters accept collections, formatters accept `QueryResult`
- **CLI-first design** — Every feature is a CLI command. MCP wraps the same library code.

### Layer Boundaries

```
CodeGraph.Core       — Models, IO, Configuration (no business logic)
CodeGraph.Indexer    — Roslyn analysis passes, workspace loading, MCP server
CodeGraph.Query      — Query engine, filters, formatters, CLI commands
```

Dependencies: Indexer → Query → Core. Never reverse.

### Code Style

- `var` for obvious types, explicit types for complex ones
- Records everywhere for immutable data
- XML doc comments on all public APIs
- Conventional Commits: `feat:`, `fix:`, `test:`, `refactor:`, `docs:`
- xUnit with `[Fact]` and `[Theory]`
- Test naming: `MethodName_Scenario_ExpectedBehavior` or descriptive `Should_*`

### Build Commands

```bash
dotnet build CodeGraph.sln                    # Must pass with zero warnings
dotnet test CodeGraph.sln                     # 461+ tests, all must pass
dotnet stryker (in test project dirs)         # Mutation score ≥60% for new code
```

### PR Checklist

- [ ] All tests pass
- [ ] No new warnings (TreatWarningsAsErrors is on)
- [ ] New public APIs have XML doc comments
- [ ] Conventional commit messages
- [ ] Feature has both unit tests and integration test coverage
```

---

## Implementation Order

Each phase can be parallelized internally. Wait for phase completion before starting the next.

### Phase 0: Quality Gates
**Issue:** None (infra setup)
**Scope:** Add `Directory.Build.props`, fix warnings, update `AGENTS.md`
**Validation:** `dotnet build` + `dotnet test` pass clean with TreatWarningsAsErrors

---

### Phase 1: Core Output Improvements (parallel)

#### Agent A: Issue #56 — Smart Edge Filtering
**Goal:** Reduce noise in BFS traversal by filtering edge types.

**Implementation steps:**
1. Add `QueryMode` enum to `QueryOptions`: `Focused`, `Structural`, `All`
2. Modify `DepthFilter.Traverse()` to accept an edge-type whitelist
3. In `Focused` mode, only follow: Calls, Implements, ResolvesTo, Covers, Inherits, Overrides
4. Suppress DependsOn edges where target ID starts with `System.` or `Microsoft.`
5. Add `--mode` CLI flag to `Program.cs`
6. Add `mode` parameter to MCP tool schema in `McpServer.cs`
7. Update `RankingStrategy` to weight edges by signal value
8. Add `query.defaultMode` to `CodeGraphConfig`
9. Write tests: `QueryEngine_FocusedMode_ExcludesContainsEdges`, etc.

**Files:** `QueryEngine.cs`, `DepthFilter.cs`, `RankingStrategy.cs`, `CodeGraphConfig.cs`, `McpServer.cs`, `Program.cs`
**Tests:** `QueryEngineTests.cs` (new mode tests), `DepthFilterTests.cs`, `RankingStrategyTests.cs`
**Validation:** Run query on a test graph — focused mode returns fewer, more relevant nodes

#### Agent B: Issue #57 — Compact Output Format
**Goal:** 3-5× token reduction in output.

**Implementation steps:**
1. Create `CompactFormatter.cs` in `OutputFormatters/`
2. Implement common namespace prefix detection (strip shared prefix from all node IDs)
3. Implement edge grouping: collapse multiple calls to same type into `{Method1, Method2}`
4. One-line per relationship with arrow notation (→/←)
5. Omit file paths for same-assembly nodes
6. Add `OutputFormat.Compact` to enum
7. Wire `--format compact` in CLI and MCP
8. Write tests comparing token count of compact vs context for same QueryResult

**Files:** Create `CompactFormatter.cs`; modify `QueryEngine.cs` (enum), `Program.cs`, `McpServer.cs`
**Tests:** `CompactFormatterTests.cs` — verify output structure, measure token reduction
**Validation:** Same query produces ≤33% tokens of context format

---

### Phase 2: Storage Migration

#### Agent C: Issue #58 — SQLite Storage
**Goal:** Replace JSON files with SQLite for instant queries.

**Implementation steps:**
1. Add `Microsoft.Data.Sqlite` package to `CodeGraph.Core.csproj`
2. Create `SqliteSchema.cs` — DDL for nodes, edges, metadata tables + indexes + FTS5
3. Create `SqliteGraphWriter.cs` — writes graph to .db file
4. Create `SqliteGraphReader.cs` — on-demand queries (no full load)
5. Create `SqliteQueryEngine.cs` — SQL-backed pattern matching, BFS, ranking
6. Update `codegraph index` to write `.codegraph/graph.db`
7. Update `codegraph query` to read from `.db`
8. Update MCP server to use SQLite-backed engine
9. Add `codegraph export --format json` command for human inspection
10. Add `codegraph migrate` command (JSON → SQLite)
11. Write integration tests: index → query roundtrip via SQLite

**Files:** New: `SqliteSchema.cs`, `SqliteGraphWriter.cs`, `SqliteGraphReader.cs`, `SqliteQueryEngine.cs`, `ExportCommand.cs`; modify all consumers of `GraphReader`/`GraphWriter`
**Tests:** Full roundtrip integration tests, performance benchmarks
**Validation:** Query cold-start <100ms (vs ~2s with JSON). All existing query tests pass against SQLite backend.

---

### Phase 3: Intelligence Layer (parallel)

#### Agent D: Issue #59 — Graph Report
**Goal:** Generate `.codegraph/REPORT.md` with hub types, modules, patterns.

**Implementation steps:**
1. Create `Report/` folder in `CodeGraph.Query`
2. `HubAnalyzer.cs` — calculate in-degree + out-degree per node (excluding Contains), return top-N
3. `ClusterAnalyzer.cs` — group by assembly, count cross-assembly edges
4. `StrategyPatternDetector.cs` — find interfaces with ≥2 ResolvesTo incoming edges
5. `CoverageAnalyzer.cs` — count nodes with/without CoveredBy edges per assembly
6. `QuerySuggester.cs` — template-based: pick hub types, generate valid `codegraph query` commands
7. `ReportGenerator.cs` — orchestrates all analyzers, renders markdown
8. Add `codegraph report` CLI subcommand
9. Add `codegraph_summary` MCP tool
10. Write tests for each analyzer

**Files:** New: `Report/*.cs`; modify `Program.cs`, `McpServer.cs`
**Tests:** Unit tests per analyzer with synthetic graphs; integration test generating report from test fixture
**Validation:** `codegraph report` on real codebase produces readable, accurate REPORT.md

#### Agent E: Issue #60 — Hierarchical Navigation
**Goal:** `codegraph list` for drill-down browsing.

**Implementation steps:**
1. Create `ListEngine.cs` — aggregation queries (assemblies, types by degree, interfaces, namespaces)
2. Create `Commands/ListCommand.cs` — CLI handler
3. Wire subcommands: `codegraph list assemblies`, `types`, `interfaces`, `namespaces`, `methods`
4. Add `--sort`, `--top`, `--assembly`, `--type` flags
5. Add `codegraph_list` MCP tool to McpServer
6. Write tests for each list scope

**Files:** New: `ListEngine.cs`, `Commands/ListCommand.cs`; modify `Program.cs`, `McpServer.cs`
**Tests:** `ListEngineTests.cs` for each scope
**Validation:** `codegraph list assemblies` shows projects sorted by type count

#### Agent F: Issue #62 — Token Budget
**Goal:** `--budget <tokens>` limits output size.

**Implementation steps:**
1. Add `Budget` property to `QueryOptions`
2. Modify formatters to serialize rank-ordered, stopping when budget reached
3. Append continuation hint when truncated
4. Add `--budget` CLI flag
5. Add `budget` to MCP tool schema
6. Write tests: verify output stays within budget ±10%

**Files:** Modify `QueryOptions`, all formatters, `Program.cs`, `McpServer.cs`
**Tests:** Budget enforcement tests with known output sizes
**Validation:** `--budget 300` consistently produces ~300 tokens of output

#### Agent G: Issue #65 — Confidence Tags
**Goal:** Tag edges with confidence level.

**Implementation steps:**
1. Add `Confidence` property to `GraphEdge` model (nullable string)
2. In `SemanticPass` — set `verified` when symbol resolved, `unresolved` when null
3. In `DiPass` — set `static` for explicit registration, `inferred` for single-impl
4. In `TestCoveragePass` — always `verified`
5. In `SyntaxPass` — always `verified` (structural)
6. Update formatters to show confidence (only when not `verified` — don't clutter)
7. Add `--confidence` filter to CLI and MCP
8. Bump schema version to 2, add migration logic
9. Write tests for confidence assignment in each pass

**Files:** Modify `GraphEdge.cs`, all passes, formatters, `GraphSchema.cs`
**Tests:** Per-pass tests verifying correct confidence; schema migration test
**Validation:** Index a codebase with unresolved refs → see `unresolved` confidence on those edges

---

### Phase 4: Advanced Features (parallel)

#### Agent H: Issue #61 — Expanded MCP Tools
**Goal:** Add path, impact, explain, summary tools.

**Implementation steps:**
1. Create `PathFinder.cs` — bidirectional BFS, return shortest path with edge annotations
2. Create `ImpactAnalyzer.cs` — reverse BFS (incoming edges), group by depth, include test coverage
3. Create `SymbolExplainer.cs` — comprehensive single-symbol view (identity, deps, consumers, tests, members)
4. Create formatters for each: `PathFormatter.cs`, `ImpactFormatter.cs`, `ExplainFormatter.cs`
5. Add CLI subcommands: `codegraph path <from> <to>`, `codegraph impact <symbol>`, `codegraph explain <symbol>`
6. Register 4 new tools in `McpServer.HandleToolsList()`
7. Add handlers in `McpServer.HandleToolsCallAsync()`
8. Update `codegraph_query` description to guide agents to the right tool
9. Write tests for PathFinder (path exists, no path, cycle handling)

**Files:** New: `PathFinder.cs`, `ImpactAnalyzer.cs`, `SymbolExplainer.cs`, formatters; modify `McpServer.cs`, `Program.cs`
**Tests:** Unit tests for each analyzer; integration tests via MCP protocol
**Validation:** `codegraph path A B` returns valid shortest path; `codegraph explain X` returns complete profile

#### Agent I: Issue #64 — Wiki Generation
**Goal:** Generate `.codegraph/wiki/` markdown files.

**Implementation steps:**
1. Create `Wiki/WikiGenerator.cs` — orchestrates all page generators
2. `IndexPageGenerator.cs` — overview with links to assembly pages
3. `AssemblyPageGenerator.cs` — hub types, namespaces, interfaces per assembly
4. `InterfacesPageGenerator.cs` — all interfaces grouped by impl count
5. `DiWiringPageGenerator.cs` — full DI registration map
6. `TestCoveragePageGenerator.cs` — per-assembly coverage table
7. Add `codegraph wiki` CLI subcommand
8. Ensure all internal links are relative and valid
9. Write integration test: generate wiki from test graph, verify file structure

**Files:** New: `Wiki/*.cs`; modify `Program.cs`
**Tests:** Integration test verifying file structure and link validity
**Validation:** Generated wiki navigable by reading files only; all links resolve

#### Agent J: Issue #66 — Cost Tracking
**Goal:** Measure and report token compression ratio.

**Implementation steps:**
1. Create `Metrics/QueryMetrics.cs` — per-query cost calculation
2. Create `Metrics/CompressionCalculator.cs` — estimate file-equivalent tokens
3. Modify formatters to append metrics footer
4. Add `--quiet` flag to suppress footer
5. Add `codegraph stats` CLI command (graph-level statistics)
6. Track session metrics in MCP server
7. Write tests for metrics calculation

**Files:** New: `Metrics/*.cs`; modify formatters, `Program.cs`, `McpServer.cs`
**Tests:** Metrics calculation tests with known inputs
**Validation:** Query output shows compression ratio; `codegraph stats` shows graph statistics

---

### Phase 5: Cross-Solution (depends on Phase 2)

#### Agent K: Issue #52 — Cross-Solution Indexing
**Goal:** Resolve inter-solution references in mono-repos.

**Implementation steps:**
1. Modify `HybridWorkspaceLoader` to accept optional existing `graph.db` path
2. When indexing Solution B, look up already-indexed nodes from Solution A in SQLite
3. Create metadata references from resolved nodes (stub DLLs or type-forward)
4. Tag cross-solution edges with confidence `inferred`
5. Update `codegraph index` to support `--extend <existing-db>` flag
6. Update multi-solution config to specify indexing order
7. Integration test: two solutions, shared project, verify cross-solution edges exist

**Files:** Modify `HybridWorkspaceLoader.cs`, `AssetsFileResolver.cs`, `Program.cs`; modify SQLite writer
**Tests:** Integration test with multi-solution fixture
**Validation:** Cross-solution call edges appear in graph with correct confidence

---

## Validation Checklist (every phase)

```bash
# Must pass after every change
dotnet build CodeGraph.sln              # Zero warnings (TreatWarningsAsErrors)
dotnet test CodeGraph.sln               # All tests pass
dotnet stryker (affected test project)  # Mutation score ≥60% on new code
```

## Key Principles for All Agents

1. **TDD** — Write the test first, then the implementation
2. **CLI-first** — Every feature works as a CLI command; MCP wraps the same code
3. **Records** — New data types are immutable records
4. **No new dependencies** without justification (exception: `Microsoft.Data.Sqlite` for #58)
5. **Conventional Commits** — `feat:`, `fix:`, `test:`, `refactor:`
6. **Small PRs** — One issue = one PR. Don't bundle unrelated changes.
7. **Backward compat** — Existing queries must produce same results unless explicitly changed (document breaking changes)
