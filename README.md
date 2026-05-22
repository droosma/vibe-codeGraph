# CodeGraph

**Give your AI coding agents structural code understanding instead of grep.**

<!-- Badges -->
[![CI](https://github.com/droosma/vibe-codeGraph/actions/workflows/ci.yml/badge.svg)](https://github.com/droosma/vibe-codeGraph/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CodeGraph.svg)](https://www.nuget.org/packages/CodeGraph)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8+ | 10](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple)

CodeGraph is a Roslyn-powered CLI tool that builds a **semantic graph** of your C# codebase and writes it as JSON — queryable by LLM agents. Instead of dumping thousands of grep matches into a prompt, your AI assistant gets the actual dependency structure, call chains, and type hierarchy.

---

## Why CodeGraph?

**Before** — your AI assistant runs `grep "OrderService"` and gets 200+ hits:

```
src/Orders/OrderService.cs:14:public class OrderService : IOrderService
src/Orders/OrderService.cs:28:    public async Task<Order> PlaceOrder(...)
src/Api/Startup.cs:42:services.AddScoped<IOrderService, OrderService>();
src/Api/Controllers/OrderController.cs:18:    private readonly IOrderService _orderService;
tests/Orders/OrderServiceTests.cs:12:public class OrderServiceTests
... 195 more results
```

**After** — `codegraph query OrderService --depth 1 --format context` returns the actual structure:

```markdown
# Query: OrderService
Commit: abc1234 | Branch: main | 2026-01-15T10:30:00Z

## Target: MyApp.Services.OrderService (Type)
File: src/Orders/OrderService.cs [14–87]
Signature: public class OrderService : IOrderService
Accessibility: Public

### Outgoing Relationships
**Implements:**
  → IOrderService

**Calls:**
  → IOrderRepository.SaveAsync(Order)
  → IEventBus.PublishAsync(OrderPlacedEvent)

**DependsOn:**
  → Order
  → OrderRequest

### Incoming Relationships
**Calls:**
  ← OrderController.PlaceOrder(OrderRequest)

**ResolvesTo:**
  ← IOrderService (via DI)

**Covers:**
  ← OrderServiceTests.PlaceOrder_ShouldPublishEvent
```

Your AI assistant gets a focused, structured view — not a wall of text.

---

## Quick Start

```bash
# Install as a global .NET tool
dotnet tool install -g CodeGraph

# In your repo — initialize config + MCP registration:
codegraph init
# → Creates codegraph.json, .vscode/mcp.json, .mcp.json, apm.yml

# Build the graph:
codegraph index --solution MyApp.sln

# Query the graph:
codegraph query PlaceOrder --depth 1
codegraph query IOrderRepository --kind resolves-to
codegraph query "Order*" --format json --depth 2
```

After `codegraph init`, AI agents that support MCP (VS Code Copilot, Claude Code, Cursor) will auto-discover `codegraph_query` as a tool — no configuration needed.

### Output

The index command writes JSON files to `.codegraph/`:
- **One file per assembly/project** — sized for LLM context windows
- **`_external.json`** — SBOM-like graph of all external/NuGet dependencies
- **`meta.json`** — git metadata, statistics, and index timestamp

The query command reads those files and returns a focused subgraph.

---

## CLI Reference

### `codegraph init`

Auto-detect your solution, create config + MCP server registration files, and scaffold agent skill files.

```
codegraph init [--agent <name>] [--solution <path.sln|path.slnx>] [--output <dir>] [--force]
```

**Step 1 — Config + MCP files** (always):
- `codegraph.json` — index/query configuration
- `.vscode/mcp.json` — MCP server for VS Code Copilot / Cursor
- `.mcp.json` — MCP server for Claude Code
- `apm.yml` — MCP server for APM (Agent Package Manager)

**Step 2 — Agent skill files** (auto-detected or explicit):

Auto-detects which AI agents are configured in the repo (looks for `.claude/`, `CLAUDE.md`, `.github/copilot-instructions.md`, `AGENTS.md`, `.cursorrules`, `.cursor/rules/`), then writes agent-specific skill files so each agent understands how to use CodeGraph:

| Agent | Detection | File(s) written |
|-------|-----------|-----------------|
| Claude Code | `.claude/` dir or `CLAUDE.md` | `.claude/skills/codegraph/SKILL.md`, `.claude/skills/codegraph/scripts/query-wrapper.sh` |
| GitHub Copilot | `.github/copilot-instructions.md` | Appends CodeGraph section to `.github/copilot-instructions.md` |
| OpenCode / Codex | `AGENTS.md` | Appends CodeGraph section to `AGENTS.md` |
| Cursor | `.cursorrules` or `.cursor/rules/` | `.cursor/rules/codegraph.md` |
| *(all agents)* | — | `.codegraph/INSTRUCTIONS.md` (generic instructions, always written) |

| Flag | Description |
|------|-------------|
| `--agent <name>` | Skip auto-detection and install for a specific agent: `claude`, `copilot`, `opencode`, `cursor`, `all` |
| `--solution <path>` | Combine init + index in one step, then auto-generate `.codegraph/REPORT.md` |
| `--output <dir>` | Output directory for graph data (default: `.codegraph`) |
| `--force` | Overwrite existing skill files |

**Solution auto-discovery:** When no `--solution` flag is given, `codegraph init` scans the repo for `.sln` and `.slnx` files. It prefers root-level solutions over nested ones, and if both a `.sln` and `.slnx` exist with the same name, the `.sln` takes precedence.

### `codegraph index`

Build the semantic graph from your C# solution.

```
codegraph index --solution <path.sln|path.slnx> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--solution <path>` | Path to `.sln` or `.slnx` file | From `codegraph.json` |
| `--output <dir>` | Output directory for graph files | `.codegraph` |
| `--projects <filter>` | Wildcard filter for project names | All projects |
| `--config <path>` | Path to `codegraph.json` config | Auto-detected |
| `--configuration <name>` | Build configuration | `Debug` |
| `--skip-restore` | Skip `dotnet restore` step | `false` |
| `--skip-build` | Hidden alias for `--skip-restore` | `false` |
| `--changed-only` | Incremental re-index; only re-index projects with changes since last indexed commit | `false` |
| `--sequential` | Disable parallel multi-solution indexing (recommended on machines with < 16 GB RAM) | `false` |
| `--extend <db-path>` | Extend an existing `graph.db` with this solution's graph instead of creating a new one | (none) |
| `--verbose` | Enable verbose output | `false` |

### `codegraph query`

Query the graph for symbols, relationships, and dependencies.

```
codegraph query <symbol-pattern> [options]
```

**Symbol patterns** support wildcards (`Order*`, `*Service`), exact match (`OrderService`), and kind prefix (`type:OrderService`, `method:PlaceOrder`).

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth | `1` |
| `--kind <type>` | Edge filter (see table below) | All kinds |
| `--mode <mode>` | Traversal mode: `focused` (high-signal edges only), `structural` (includes containment), `all` | `all` |
| `--namespace <pattern>` | Namespace filter (supports wildcards) | All namespaces |
| `--project <name>` | Project filter | All projects |
| `--format <fmt>` | Output format: `json`, `text`, `context`, `compact` | `context` |
| `--max-nodes <n>` | Maximum nodes in result | `50` |
| `--include-external` | Include external assembly dependencies | `false` |
| `--include-source` | Embed source code snippets alongside node references in output | `false` |
| `--no-rank` | Disable relevance ranking | (ranking enabled by default) |
| `--budget <tokens>` | Maximum token budget; output is truncated with a hint when exceeded | (none) |
| `--no-metrics` | Suppress the compression metrics footer | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |
| `--from <solution>` | Scope query to a specific solution sub-graph (multi-solution only) | All solutions |

**Output formats:**

| Format | Description |
|--------|-------------|
| `context` | Markdown-like, optimized for LLM prompts (default) |
| `compact` | Compressed prefix-stripped format, 3–5× smaller than `context` |
| `text` | Human-readable tabular summary |
| `json` | Machine-readable full `QueryResult` |

**Query suggestions:**

After each query, the CLI prints `💡 Suggested next queries:` to stderr with up to three contextual follow-up commands — deeper traversal, call-chain exploration, DI wiring, or cross-assembly scoping. AI agents can follow these hints automatically to navigate the graph efficiently without additional prompting.

**Edge kind aliases:**

| Alias | EdgeType |
|-------|----------|
| `calls`, `calls-to`, `calls-from` | Calls |
| `inherits` | Inherits |
| `implements` | Implements |
| `depends-on` | DependsOn |
| `resolves-to` | ResolvesTo |
| `covers` | Covers |
| `covered-by` | CoveredBy |
| `references` | References |
| `overrides` | Overrides |
| `contains` | Contains |
| `all` | No filter |

### `codegraph diff`

Compare two graph snapshots and report structural changes.

```
codegraph diff [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--base <dir>` | Base graph snapshot directory | `.codegraph-prev` |
| `--head <dir>` | Head graph snapshot directory | `.codegraph` |
| `--ref <git-ref>` | Use `.codegraph-<ref>` as base snapshot | (none) |
| `--only <types>` | Comma-separated: `added`, `removed`, `signature-changed`, `added-nodes`, `removed-nodes`, `added-edges`, `removed-edges` | All change types |
| `--format <fmt>` | Output format: `json`, `text`, `context` | `context` |

See [docs/diff.md](docs/diff.md) for the full how-to guide.

### `codegraph compare`

Compare two symbols structurally — discover shared interfaces, shared base types, shared dependencies, and relationships unique to each symbol.

```
codegraph compare <symbolA> <symbolB> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for relationship collection | `1` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph compare OrderService InvoiceService
codegraph compare "*OrderRepo*" "*InvoiceRepo*" --depth 2
```

See [docs/compare.md](docs/compare.md) for the full how-to guide, including output format and refactoring workflows.

### `codegraph search`

Search for symbols by name, namespace, or file path using case-insensitive substring matching. Use this to discover the exact qualified name of a symbol before running `codegraph query` or `codegraph compare`.

```
codegraph search <query> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph search Order                        # Find all symbols containing "Order"
codegraph search Order --kind type            # Types only
codegraph search PlaceOrder --kind method     # Methods only
codegraph search Service --top 50            # Show up to 50 results
```

See [docs/search.md](docs/search.md) for the full how-to guide, including discovery workflows.

### `codegraph list`

Browse the code graph hierarchy — enumerate assemblies, types, interfaces, or namespaces without writing a query.

```
codegraph list [scope] [options]
```

| Scope | Description |
|-------|-------------|
| `assemblies` | All assemblies with type/method counts **(default)** |
| `types` | Types ranked by connectivity (in + out degree) |
| `interfaces` | Interfaces with implementation counts |
| `namespaces` | Namespaces with type and method counts |

| Flag | Description | Default |
|------|-------------|---------|
| `--assembly <name>` | Filter by assembly name (applies to `types`, `interfaces`, `namespaces`) | All |
| `--top <n>` | Maximum items to return (applies to `types` only) | `50` |
| `--skip <n>` | Skip the first N results for pagination (applies to `types` only) | `0` |
| `--filter <pattern>` | Filter results by name substring, case-insensitive (applies to `types` only) | All |
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph list                              # List all assemblies
codegraph list types                        # Most-connected types across all assemblies
codegraph list types --assembly MyApp.Core  # Types in a specific assembly
codegraph list interfaces --top 10          # Top 10 most-implemented interfaces
codegraph list namespaces                   # All namespaces
codegraph list types --filter Order         # Types whose name contains "Order"
codegraph list types --top 20 --skip 20     # Page 2 of types
```

See [docs/list.md](docs/list.md) for the full guide, including output format details and orientation workflows.

### `codegraph search`

Search for nodes in the graph by name, namespace, or file path using case-insensitive substring matching.

```
codegraph search <query> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |

```bash
codegraph search Order                       # Find anything named "Order"
codegraph search Order --kind type           # Types only
codegraph search Repository --top 50        # Up to 50 results
codegraph search Async --kind method --json  # JSON output
```

See [docs/search.md](docs/search.md) for the full guide.

### `codegraph explain`

Show detailed information about a single symbol: signature, doc comment, members, all edges, and linked test coverage.

```
codegraph explain <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |

```bash
codegraph explain OrderService               # Full deep-dive on OrderService
codegraph explain OrderService --json        # JSON output for scripting
```

See [docs/explain.md](docs/explain.md) for the full guide including output format.

### `codegraph impact`

Analyse the **blast radius** of a change to a symbol — walk the reverse dependency graph layer by layer to find everything that depends on it.

```
codegraph impact <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Maximum traversal depth | `3` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |

```bash
codegraph impact OrderService                # Blast radius at depth 3
codegraph impact PaymentGateway --depth 5   # Wider search
codegraph impact OrderService --json        # JSON for CI scripts
```

See [docs/impact.md](docs/impact.md) for the full guide.

### `codegraph path`

Find the **shortest dependency path** between two symbols.

```
codegraph path <from> <to> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--max-depth <n>` | Maximum hops to search | `10` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |

```bash
codegraph path OrdersController OrderRepository    # Trace a dependency chain
codegraph path MyApp.Domain MyApp.Infrastructure  # Check for unexpected coupling
```

See [docs/path.md](docs/path.md) for the full guide.

### `codegraph compare`

Perform a **structural side-by-side comparison** of two symbols — shared interfaces, shared callers, shared callees, and what each has exclusively.

```
codegraph compare <symbolA> <symbolB> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for neighbourhood | `1` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph compare OrderService PaymentService         # Compare two services
codegraph compare SqlOrderRepository InMemoryOrderRepository --depth 2
```

See [docs/compare.md](docs/compare.md) for the full guide.

### `codegraph brief`

Generate a compact **`BRIEF.md`** codebase orientation file optimised for LLM agents to read at the start of a session.

```
codegraph brief [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph brief                              # Generate .codegraph/BRIEF.md
codegraph brief --graph-dir .codegraph/Api  # For a specific sub-graph
```

See [docs/brief.md](docs/brief.md) for the full guide.

### `codegraph test-impact`

Analyse **test coverage** for a symbol — show direct tests, indirect tests (through a call chain), and uncovered callers.

```
codegraph test-impact <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for indirect coverage | `3` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |

```bash
codegraph test-impact OrderService           # Which tests cover OrderService?
codegraph test-impact OrderService --json   # JSON for CI test selection
```

See [docs/test-impact.md](docs/test-impact.md) for the full guide.

### `codegraph snapshot`

Save, list, or delete **named graph snapshots** — the foundation for diff workflows.

```
codegraph snapshot <save|list|delete> [name] [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph snapshot save before-refactor      # Save current graph
codegraph snapshot list                      # List all snapshots
codegraph snapshot delete old-experiment     # Remove a snapshot
```

Use with `codegraph diff` to compare graph states:

```bash
codegraph snapshot save baseline
# ... make code changes and re-index ...
codegraph diff --base .codegraph/snapshots/baseline --head .codegraph
```

See [docs/snapshot.md](docs/snapshot.md) for the full guide.

### `codegraph packages`

Analyse **NuGet package usage** across the solution — list packages per project and detect version conflicts.

```
codegraph packages [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--project <name>` | Filter to one project | All |
| `--package <name>` | Show which projects use this package | All |
| `--format json` | Output as JSON | Plain text |
| `--graph-dir <path>` | Graph directory | `.codegraph` |

```bash
codegraph packages                            # All packages across all projects
codegraph packages --package Newtonsoft.Json  # Who uses this package?
codegraph packages --project MyApp.Api        # Packages for one project
```

See [docs/packages.md](docs/packages.md) for the full guide.

### `codegraph daemon`

Run a **persistent background server** that keeps the graph in memory for near-instant query responses.

```
codegraph daemon <start|stop|status> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory to serve | `.codegraph` |

```bash
codegraph daemon start     # Start the daemon (foreground)
codegraph daemon status    # Check if running
codegraph daemon stop      # Stop the daemon
```

See [docs/daemon.md](docs/daemon.md) for the full guide, including background execution and systemd configuration.

### `codegraph stats`

Print a quick overview of the graph: total node and edge counts, broken down by kind and type.

```
codegraph stats [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph stats                          # Stats for .codegraph/
codegraph stats --graph-dir .codegraph-prev  # Stats for an older snapshot
```

Example output:

```
CodeGraph Statistics
  Total nodes: 1 842
  Total edges: 5 219

Nodes by kind:
  Method: 982
  Type: 421
  Property: 287
  ...

Edges by type:
  Calls: 2 104
  Contains: 1 388
  References: 901
  ...
```

See [docs/stats.md](docs/stats.md) for common workflows including CI monitoring and snapshot comparison.

### `codegraph report`

Generate a Markdown report that summarises the graph: hub types (highest connectivity), assembly boundaries, test coverage by assembly, and suggested starter queries.

```
codegraph report [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--output`, `-o <path>` | Output file path | `.codegraph/REPORT.md` |

```bash
codegraph report                         # Write .codegraph/REPORT.md
codegraph report -o docs/report.md       # Write to a custom path
codegraph report --graph-dir .codegraph-prev  # Report on an older snapshot
```

> **Tip:** `codegraph index` automatically generates `REPORT.md` after every successful index run, so agents always have a fresh overview on first use.

See [docs/report.md](docs/report.md) for the full guide, including report sections, CI usage, and sharing with GitHub Wiki.

### `codegraph wiki`

Generate a navigable Markdown wiki from the graph. Useful for publishing auto-generated architecture documentation to GitHub Wiki or any docs site.

```
codegraph wiki [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |
| `--output`, `-o <dir>` | Output directory | `.codegraph/wiki` |

The generator creates:

| File | Content |
|------|---------|
| `INDEX.md` | Graph summary, assembly list, top hub types |
| `assemblies/<name>.md` | Per-assembly: types, methods, DI registrations, test coverage |
| `INTERFACES.md` | All interfaces with their implementations |
| `DI-WIRING.md` | Dependency-injection registrations and resolved types |

```bash
codegraph wiki                           # Generate .codegraph/wiki/
codegraph wiki -o docs/wiki              # Publish to a docs directory
codegraph wiki --graph-dir .codegraph-prev  # Wiki from an older snapshot
```

See [docs/wiki.md](docs/wiki.md) for the full guide, including generated file structure, GitHub Wiki publishing, and CI integration.

### `codegraph export`

Export the graph from its SQLite database (`graph.db`) back to JSON files. Useful for tooling that consumes the JSON format or for archiving a snapshot.

```
codegraph export [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--output <dir>` | Output directory for JSON files | `export` |

```bash
codegraph export                         # Export .codegraph/graph.db → export/
codegraph export --output my-snapshot    # Export to a named directory
```

See [docs/export.md](docs/export.md) for the full guide, including archiving, CI artifact publishing, and jq examples.

### `codegraph mcp`

Start an MCP (Model Context Protocol) stdio server. This is how AI agents discover and use CodeGraph as a native tool.

```
codegraph mcp [--graph-dir <dir>]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

The server exposes six tools. Agents call them like any other tool (no shell commands, no prompt engineering). The server starts on demand via stdio and exits when the agent disconnects.

| MCP Tool | Description |
|----------|-------------|
| `codegraph_query` | Query the graph by symbol pattern with depth, edge-type, and format options |
| `codegraph_list` | Browse the graph hierarchy (assemblies, types, interfaces, namespaces) |
| `codegraph_summary` | Generate an overview report: hub types, assembly boundaries, test coverage |
| `codegraph_path` | Find the shortest dependency path between two symbols |
| `codegraph_impact` | Reverse-dependency analysis — assess the blast radius of a change |
| `codegraph_explain` | Full symbol deep-dive: signature, members, all edges, test coverage |

See [docs/mcp.md](docs/mcp.md) for the full guide, including per-agent configuration and troubleshooting.

### `codegraph view`

Generate an interactive 3D graph visualization as a self-contained HTML file and open it in the default browser.

```
codegraph view [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--output <path>` | Write HTML to a specific file instead of a temp file | (temp file) |
| `--max-nodes <n>` | Maximum nodes to render (smart-sampled when exceeded) | `5000` |
| `--no-open` | Generate HTML but do not open in browser | `false` |

```bash
codegraph view                              # Open graph in browser
codegraph view --output graph.html          # Save to file
codegraph view --max-nodes 2000             # Limit for performance
codegraph view --graph-dir .codegraph/Api   # View specific sub-graph
```

See [docs/view.md](docs/view.md) for the full how-to guide, including sidebar controls, node sampling, and CI usage.

### `codegraph brief`

Generate a compact `BRIEF.md` codebase orientation file optimized for LLM agents. Use this as the first action when an agent starts a session — it provides a structured overview with zero query overhead.

```
codegraph brief [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph brief                          # Generate .codegraph/BRIEF.md (also prints to stdout)
codegraph brief --graph-dir .codegraph-prev  # Brief for an older snapshot
```

**Output sections:**
- **Assemblies** — project list with type/method counts _(top 20 shown for large codebases)_
- **Hub Types** — 10 most-connected types (high in+out degree)
- **Key Interfaces** — top interfaces by implementation count
- **Domain Clusters** — namespace-based domain groups _(top 15 shown)_
- **Entry Points** — HTTP/gRPC endpoints, CLI commands, message handlers _(top 20 shown)_
- **Test Coverage** — assemblies with their test-to-type ratios _(top 10 shown)_

> **Note:** For large codebases, each section shows a `(showing top N of M)` note when results are capped. This keeps the brief under ~4 KB regardless of codebase size.

> **Tip:** `codegraph index` generates `BRIEF.md` automatically after every successful index run.

### `codegraph search`

Search for symbols by name, namespace, or file path using case-insensitive substring matching.

```
codegraph search <query> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--json` | Output as JSON | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph search OrderService            # Find symbols matching "OrderService"
codegraph search Order --kind type       # Only types
codegraph search IRepository --top 10   # Top 10 matches
codegraph search Order --json            # Machine-readable output
```

**Ranking**: Exact name matches and prefix matches rank higher than partial ID matches. Searching `Patient` returns `Patient` types before `List<PatientDto>`.

**Exit codes:** `0` = results found; `2` = no results (when not using `--json`).

### `codegraph explain`

Show detailed information about a single symbol: signature, members, all outgoing and incoming edges, and linked test methods.

```
codegraph explain <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--json` | Output as JSON | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph explain OrderService           # Full deep-dive on OrderService
codegraph explain PlaceOrder --json      # Machine-readable output
```

Use `codegraph explain` when `codegraph query` returns a result you want to drill into.

### `codegraph impact`

Analyze the reverse-dependency blast radius of changes to a symbol — who depends on it, and transitively, what they depend on.

```
codegraph impact <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth | `3` |
| `--json` | Output as JSON | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph impact OrderService            # What breaks if OrderService changes?
codegraph impact PlaceOrder --depth 5    # Deeper traversal
codegraph impact OrderService --json     # Machine-readable layers
```

**Exit codes:**
| Code | Meaning |
|------|---------|
| `0` | Symbol found and has dependents |
| `1` | Symbol not found in graph |
| `2` | Symbol found but has no dependents (safe to change) |

> The exit code `2` (found, no dependents) is distinct from `1` (not found) so CI scripts can distinguish "nothing depends on this" from "symbol doesn't exist".

### `codegraph path`

Find the shortest dependency path between two symbols through calls, inheritance, or other relationships.

```
codegraph path <from> <to> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--max-depth <n>` | Maximum search depth | `10` |
| `--json` | Output as JSON | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph path OrderService PaymentGateway       # How does A reach B?
codegraph path PlaceOrder SendEmail --max-depth 5
codegraph path OrderService PaymentGateway --json
```

**Exit codes:**
| Code | Meaning |
|------|---------|
| `0` | Path found |
| `1` | Missing arguments or graph not found |
| `2` | No path found between the two symbols |

### `codegraph test-impact`

Analyze which test methods cover a symbol, directly or transitively through the call graph.

```
codegraph test-impact <symbol> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for indirect tests | `3` |
| `--json` | Output as JSON | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

```bash
codegraph test-impact OrderService       # Which tests cover OrderService?
codegraph test-impact PlaceOrder --json  # Machine-readable output
```

**Configuration is automatic** — `codegraph init` generates the MCP config files. To add manually:

```json
// .vscode/mcp.json (VS Code Copilot, Cursor)
{
  "servers": {
    "codegraph": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["codegraph", "mcp"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

```json
// .mcp.json (Claude Code)
{
  "mcpServers": {
    "codegraph": {
      "command": "dotnet",
      "args": ["codegraph", "mcp"]
    }
  }
}
```

---

## Agent Integration

`codegraph init` generates the MCP config files automatically. The agent sees `codegraph_query` as a native tool.

| Agent | Config File | Auto-generated |
|-------|------------|----------------|
| VS Code Copilot | `.vscode/mcp.json` | ✅ by `codegraph init` |
| Cursor | `.vscode/mcp.json` | ✅ by `codegraph init` |
| Claude Code | `.mcp.json` | ✅ by `codegraph init` |
| APM | `apm.yml` | ✅ by `codegraph init` |

### Agent Skills ([skills.sh](https://skills.sh))

CodeGraph provides packaged agent skills compatible with the [skills.sh](https://skills.sh) ecosystem. Install all skills with:

```bash
npx skills add droosma/vibe-codeGraph
```

Or install individual skills:

| Skill | Purpose | Install |
|-------|---------|---------|
| `codegraph-query` | Query the semantic graph for structural relationships | `npx skills add droosma/vibe-codeGraph@codegraph-query` |
| `codegraph-index` | Index or re-index a C# codebase | `npx skills add droosma/vibe-codeGraph@codegraph-index` |
| `codegraph-review` | Impact analysis for code review | `npx skills add droosma/vibe-codeGraph@codegraph-review` |

Each skill includes a standalone `SKILL.md` with trigger phrases, CLI reference, examples, and best practices. See the [`skills/`](skills/) directory for details.

### APM (Agent Package Manager) Support

CodeGraph supports [Microsoft APM](https://github.com/microsoft/apm) for managing agent configuration. If you use APM, run:

```bash
apm install --mcp codegraph -- dotnet codegraph mcp
```

Or use the `apm.yml` generated by `codegraph init` and run `apm install` to wire CodeGraph into all detected agent clients.

See [docs/agent-setup.md](docs/agent-setup.md) for detailed per-agent instructions.

---

## Configuration

CodeGraph is configured via a `codegraph.json` file in your repo root. Run `codegraph init` to generate one, or see the full reference at [docs/configuration.md](docs/configuration.md).

| Section | Controls |
|---------|----------|
| `solution` | Path to a single `.sln` or `.slnx` file |
| `solutions` | Array of solution entries for multi-solution mono-repos |
| `output` | Graph output directory (default: `.codegraph`) |
| `splitBy` | File split strategy: `project` (default) or `assembly` (both are assembly-based), or `namespace` |
| `index` | Project filtering, build configuration, external packages |
| `ioc` | IoC/DI container resolution settings |
| `tests` | Test discovery patterns |
| `docs` | Documentation extraction |
| `query` | Default query parameters |

See [codegraph.json.example](codegraph.json.example) for a single-solution example, or [codegraph.multi-solution.json.example](codegraph.multi-solution.json.example) for a multi-solution example.

---

## How It Works

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐     ┌───────────┐
│ dotnet       │────▸│ Syntax Pass  │────▸│ Semantic Pass │────▸│ JSON Graph│
│ restore +    │     │ (structure)  │     │ (relations)  │     │ (per-asm) │
│ Hybrid       │     └──────────────┘     └──────────────┘     └───────────┘
└─────────────┘           │                     │                    │
                          │              ┌──────────────┐            ▼
                          │              │   DI Pass    │     ┌─────────────┐
                          │              │ (ResolvesTo) │     │ _external   │
                          │              └──────────────┘     │   (SBOM)    │
                          │              ┌──────────────┐     └─────────────┘
                          │              │ Test Pass    │            │
                          │              │  (Covers)    │            ▼
                          │              └──────────────┘     ┌─────────────┐
                          └──────────────────────────────────▸│ Query Engine│
                                                              │ (BFS + rank)│
                                                              └─────────────┘
```

1. **Hybrid Workspace Loader** — Runs `dotnet restore`, then parses `.sln` / `.slnx` / `.csproj` / `project.assets.json` to assemble Roslyn `CSharpCompilation` objects directly (no MSBuildWorkspace). If the restore fails, a warning is emitted to stderr and indexing continues with best-effort compilation.
2. **Syntax Pass** — Walks syntax trees to extract namespaces, types, methods, properties, fields, constructors, and events. Creates structural `Contains` edges. Enriches nodes with metadata (`isAbstract`, `isStatic`, `isAsync`, `returnType`, etc.).
3. **Semantic Pass** — Uses the Roslyn semantic model to resolve calls, inheritance, interface implementations, type dependencies, references, and overrides. Creates external nodes for cross-assembly references.
4. **DI Pass** — Detects `AddScoped/AddTransient/AddSingleton` patterns to emit `ResolvesTo` edges with lifetime metadata.
5. **Test Coverage Pass** — Detects test methods (xUnit, NUnit, MSTest) and emits `Covers`/`CoveredBy` edges linking tests to production code.
6. **Graph Writer** — Outputs JSON files split by assembly (one per project), external nodes to `_external.json` (SBOM), plus `meta.json` with git metadata and statistics.
7. **Query Engine** — Pattern-matches symbols, performs BFS traversal, filters by edge type/namespace/project, ranks by relevance, and formats output.

For a deep dive, see [docs/architecture.md](docs/architecture.md).

---

## CI/CD Integration

### Keep your graph fresh in CI

```yaml
- name: Update CodeGraph
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln --changed-only
```

### Publishing a new release

Push a version tag to trigger the NuGet publish workflow:

```bash
git tag v0.4.0
git push origin v0.4.0
```

This runs `.github/workflows/publish.yml` which builds, tests, packs, and pushes to NuGet.org. You'll need to add a `NUGET_API_KEY` secret to your GitHub repository.

---

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, code style, and PR guidelines.

### Running Tests

```bash
# Unit tests
dotnet test

# Mutation testing (requires dotnet-stryker tool)
dotnet tool restore
cd tests/CodeGraph.Core.Tests && dotnet stryker
cd tests/CodeGraph.Indexer.Tests && dotnet stryker
cd tests/CodeGraph.Query.Tests && dotnet stryker
```

Stryker generates HTML reports in `StrykerOutput/` with mutation scores per project. The CI pipeline runs mutation testing automatically on PRs that touch `src/` or `tests/`.

---

## Documentation

### Architecture & Internals

- [Architecture Deep Dive](docs/architecture.md)
- [Graph Schema Reference](docs/graph-schema.md)
- [Configuration Reference](docs/configuration.md)
- [Vision & Roadmap](docs/VISION.md)

### Agent & MCP Integration

- [Agent Setup Guide](docs/agent-setup.md)
- [MCP Server Guide](docs/mcp.md)

### CLI Command Guides

- [Graph List How-to Guide](docs/list.md)
- [Symbol Search Guide](docs/search.md)
- [Symbol Explain Reference](docs/explain.md)
- [Impact Analysis Guide](docs/impact.md)
- [Dependency Path Guide](docs/path.md)
- [Symbol Compare Guide](docs/compare.md)
- [LLM Brief Generator](docs/brief.md)
- [Test Impact Analysis](docs/test-impact.md)
- [Graph Stats Reference](docs/stats.md)
- [Graph Export Guide](docs/export.md)
- [Graph Diff How-to Guide](docs/diff.md)
- [Snapshot Management Guide](docs/snapshot.md)
- [NuGet Packages Guide](docs/packages.md)
- [Background Daemon Guide](docs/daemon.md)
- [Interactive Graph Visualization](docs/view.md)
- [Graph Report Reference](docs/report.md)
- [Wiki Generator Guide](docs/wiki.md)

---

## License

[MIT](LICENSE) © 2026 CodeGraph Contributors
