# CodeGraph

**Give your AI coding agents structural code understanding instead of grep.**

<!-- Badges -->
[![CI](https://github.com/droosma/vibe-codeGraph/actions/workflows/ci.yml/badge.svg)](https://github.com/droosma/vibe-codeGraph/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/CodeGraph.svg)](https://www.nuget.org/packages/CodeGraph)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8+](https://img.shields.io/badge/.NET-8.0%2B-purple)

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
# → Creates codegraph.json, .vscode/mcp.json, .mcp.json

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

Auto-detect your solution and create config + MCP server registration files.

```
codegraph init [--output <dir>]
```

Creates:
- `codegraph.json` — index/query configuration
- `.vscode/mcp.json` — MCP server for VS Code Copilot / Cursor
- `.mcp.json` — MCP server for Claude Code

| Flag | Description |
|------|-------------|
| `--output <dir>` | Directory to create `codegraph.json` in (default: current directory) |

### `codegraph index`

Build the semantic graph from your C# solution.

```
codegraph index --solution <path.sln> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--solution <path>` | Path to `.sln` file | From `codegraph.json` |
| `--output <dir>` | Output directory for graph files | `.codegraph` |
| `--projects <filter>` | Wildcard filter for project names | All projects |
| `--config <path>` | Path to `codegraph.json` config | Auto-detected |
| `--configuration <name>` | Build configuration | `Debug` |
| `--skip-build` | Skip `dotnet build` step | `false` |
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
| `--namespace <pattern>` | Namespace filter (supports wildcards) | All namespaces |
| `--project <name>` | Project filter | All projects |
| `--format <fmt>` | Output format: `json`, `text`, `context` | `context` |
| `--max-nodes <n>` | Maximum nodes in result | `50` |
| `--include-external` | Include external assembly dependencies | `false` |
| `--rank` | Enable relevance ranking | `true` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

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

### `codegraph mcp`

Start an MCP (Model Context Protocol) stdio server. This is how AI agents discover and use CodeGraph as a native tool.

```
codegraph mcp [--graph-dir <dir>]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

The server exposes one tool — `codegraph_query` — with a typed JSON schema. Agents call it like any other tool (no shell commands, no prompt engineering). The server starts on demand via stdio and exits when the agent disconnects.

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

See [docs/agent-setup.md](docs/agent-setup.md) for detailed per-agent instructions.

---

## Configuration

CodeGraph is configured via a `codegraph.json` file in your repo root. Run `codegraph init` to generate one, or see the full reference at [docs/configuration.md](docs/configuration.md).

| Section | Controls |
|---------|----------|
| `solution` | Path to `.sln` file |
| `output` | Graph output directory (default: `.codegraph`) |
| `splitBy` | File split strategy: `assembly` (default), `project`, or `namespace` |
| `index` | Project filtering, build configuration, external packages |
| `ioc` | IoC/DI container resolution settings |
| `tests` | Test discovery patterns |
| `docs` | Documentation extraction |
| `query` | Default query parameters |

See [codegraph.json.example](codegraph.json.example) for a fully annotated example.

---

## How It Works

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐     ┌───────────┐
│ dotnet build │────▸│ Syntax Pass  │────▸│ Semantic Pass │────▸│ JSON Graph│
│ + Hybrid     │     │ (structure)  │     │ (relations)  │     │ (per-asm) │
│ Workspace    │     └──────────────┘     └──────────────┘     └───────────┘
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

1. **Hybrid Workspace Loader** — Runs `dotnet build`, then parses `.sln` / `.csproj` / `project.assets.json` to assemble Roslyn `CSharpCompilation` objects directly (no MSBuildWorkspace).
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
git tag v0.1.0
git push origin v0.1.0
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

- [Architecture Deep Dive](docs/architecture.md)
- [Graph Schema Reference](docs/graph-schema.md)
- [Configuration Reference](docs/configuration.md)
- [Agent Setup Guide](docs/agent-setup.md)

---

## License

[MIT](LICENSE) © 2026 CodeGraph Contributors
