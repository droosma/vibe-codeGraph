# How to Use `codegraph query`

`codegraph query` is the primary way to explore your codebase's structure. It traverses the semantic graph outward from one or more matching symbols, returning callers, callees, type hierarchies, DI wiring, test coverage, and more — formatted for both humans and LLM agents.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Query by symbol name (substring match)
codegraph query OrderService

# Increase traversal depth to see two hops
codegraph query OrderService --depth 2

# Filter to only inbound calls
codegraph query OrderService --kind calls-from

# Wildcard pattern: all types ending in "Service"
codegraph query "*Service" --depth 1

# Find all symbols in a file
codegraph query --file src/Orders/OrderService.cs

# Compact output for AI agents with token budget
codegraph query OrderService --format compact --budget 2000
```

---

## CLI Reference

```
codegraph query <symbol-pattern> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbol-pattern>` | Symbol name, wildcard pattern (`Order*`, `*Service`), or kind-prefixed pattern (`type:OrderService`, `method:PlaceOrder`) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth from the matched symbol(s) | `1` |
| `--kind <type>` | Edge type filter (see [Edge kind aliases](#edge-kind-aliases) below) | All kinds |
| `--file <path>` | Find all symbols in the given source file instead of querying by pattern | (none) |
| `--mode <mode>` | Traversal mode: `focused` (high-signal edges only), `structural` (includes containment), `all` | `all` |
| `--namespace <pattern>` | Restrict results to nodes in matching namespaces (supports wildcards) | All namespaces |
| `--project <name>` | Restrict results to nodes in the matching project | All projects |
| `--format <fmt>` | Output format: `json`, `text`, `context`, `compact` | `context` (terminal), `compact` (piped) |
| `--max-nodes <n>` | Maximum number of nodes in the result | `50` |
| `--include-external` | Include external (NuGet) dependency nodes in results | `false` |
| `--include-source` | Embed source code snippets (capped at 20 lines) alongside node references | `false` |
| `--no-rank` | Disable relevance ranking of results | Ranking enabled |
| `--no-docs` | Suppress XML doc comment summary in `compact` output | Doc comments included |
| `--budget <tokens>` | Maximum token budget; output is truncated with a hint when exceeded | (none) |
| `--no-metrics` | Suppress the compression metrics footer | Metrics shown |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--from <solution>` | Scope query to a specific solution sub-graph (multi-solution repos only) | All solutions |
| `--json` | Alias for `--format json` | |
| `--help`, `-h` | Show help | |

---

## Symbol Patterns

Patterns are matched against node names and fully-qualified IDs using case-insensitive substring matching. Three forms are supported:

| Form | Example | What it matches |
|------|---------|----------------|
| Exact / substring | `OrderService` | Any node whose name contains `OrderService` |
| Wildcard | `*Service`, `Order*`, `*Order*` | Glob-style prefix/suffix/contains |
| Kind-prefixed | `type:OrderService`, `method:PlaceOrder` | Only nodes of the specified kind |

When multiple nodes match the pattern, the query returns a merged result starting from each matched node.

---

## Traversal Modes

| Mode | Description |
|------|-------------|
| `all` | All edge types (default) |
| `focused` | High-signal edges only: `Calls`, `Implements`, `Inherits`, `ResolvesTo`, `Covers`, `CoveredBy` |
| `structural` | Everything in `focused`, plus `Contains` edges for member enumeration |

Use `focused` or `structural` to reduce noise when working with deeply connected types. Use `all` when you need a complete picture including references, overrides, and domain-specific edges.

---

## Output Formats

| Format | Description |
|--------|-------------|
| `context` | Markdown-like output optimized for LLM prompts. Default when output is a terminal. |
| `compact` | Prefix-stripped compressed format, 3–5× smaller than `context`. Default when output is piped. Inlines the `<summary>` XML doc comment on the primary symbol's header (suppress with `--no-docs`). |
| `text` | Human-readable tabular summary. |
| `json` | Full `QueryResult` as machine-readable JSON. |

### Example `context` output

```markdown
# Query: OrderService --depth 1 --kind all
Commit: abc1234 | Branch: main | 2026-01-15T10:30:00Z

## Target: MyApp.Services.OrderService (Type)
File: src/MyApp.Services/OrderService.cs [14–87]
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

---

## Edge Kind Aliases

Use these values with `--kind`:

| Alias | EdgeType | Description |
|-------|----------|-------------|
| `calls`, `calls-to`, `calls-from` | `Calls` | Method invocations |
| `inherits` | `Inherits` | Class inheritance |
| `implements` | `Implements` | Interface implementation |
| `depends-on` | `DependsOn` | Type dependency (parameters, fields, return types) |
| `resolves-to` | `ResolvesTo` | DI container resolution |
| `covers` | `Covers` | Test → production coverage |
| `covered-by` | `CoveredBy` | Production → test (inverse of `covers`) |
| `references` | `References` | General reference |
| `overrides` | `Overrides` | Method override |
| `contains` | `Contains` | Structural containment (namespace → type, type → member) |
| `all` | (no filter) | All edge types |

---

## Query Suggestions

After every successful query, the CLI writes up to three contextual follow-up suggestions to `stderr`:

```
💡 Suggested next queries:
  codegraph query OrderService --depth 2
  codegraph query IOrderService --kind resolves-to
  codegraph query OrderService --kind calls-from
```

AI agents can follow these hints automatically to navigate the graph efficiently without extra prompting.

---

## Source Snippets

Add `--include-source` after narrowing to a specific method or type to embed a short implementation snippet in the output. Snippets are capped at 20 lines for token safety.

> **Tip:** When you know the file path but not the exact symbol name, use `--file <path>` or the MCP `codegraph_file` tool instead.

---

## Common Workflows

### Find callers of a method

```bash
codegraph query PlaceOrder --kind calls-from
```

### Trace DI wiring for an interface

```bash
codegraph query IOrderService --kind resolves-to
```

### Discover what a service depends on

```bash
codegraph query OrderService --kind depends-on --depth 2
```

### Find tests that cover a symbol

```bash
codegraph query OrderService --kind covers
```

### Scope to a specific namespace

```bash
codegraph query "*Repository" --namespace "MyApp.Data.*"
```

### Token-budget-safe output for agents

```bash
codegraph query OrderService --format compact --budget 3000
```

### Query from a file path

```bash
codegraph query --file src/Orders/OrderService.cs
```

### Multi-solution: query one sub-graph

```bash
codegraph query OrderService --from backend
```

---

## Choosing Between `query`, `explain`, `search`, and `impact`

| Use case | Command |
|----------|---------|
| Traverse the graph from a known symbol | `codegraph query <symbol>` |
| Full deep-dive on a single symbol | `codegraph explain <symbol>` |
| Find symbols by partial name when you're unsure of the exact name | `codegraph search <text>` |
| Assess the blast radius of a change | `codegraph impact <symbol>` |
| Find which tests cover a symbol | `codegraph test-impact <symbol>` |
| Compare two symbols structurally | `codegraph compare <a> <b>` |

---

## MCP Equivalent

When used via [MCP](mcp.md), use the `codegraph_query` tool:

```json
{
  "name": "codegraph_query",
  "arguments": {
    "pattern": "OrderService",
    "depth": 1,
    "kind": "calls-from",
    "format": "compact",
    "mode": "focused",
    "confidence": "verified"
  }
}
```

The MCP tool supports an additional `confidence` parameter not available in the CLI:

| Value | Meaning |
|-------|---------|
| `verified` | Only edges confirmed by Roslyn semantic analysis |
| `inferred` | Verified + inferred edges (e.g., single DI implementation) |
| `unresolved` | All edges including unresolved targets |
| (omit) | All edges regardless of confidence (default) |

See [agent-setup.md](agent-setup.md) for full MCP parameter reference.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Results found and printed |
| `1` | Error (bad arguments, graph not found, unrecognised `--kind` value) |
| `2` | No nodes matched the pattern |
