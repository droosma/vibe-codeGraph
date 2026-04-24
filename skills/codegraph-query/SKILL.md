---
name: codegraph-query
description: Query a C# codebase's semantic graph for structural relationships instead of grep. Use when asked to "find callers", "trace dependencies", "what calls X", "who implements Y", or any structural code question.
metadata:
  author: codegraph-contributors
  version: "1.0.0"
  argument-hint: <symbol-pattern>
---

# CodeGraph Query

Query a C# codebase's semantic graph to understand structure, dependencies, and relationships — without grep.

## When to Use

Use this skill when:
- Finding callers/callees of a method ("what calls PlaceOrder?")
- Tracing type hierarchies — inheritance, interface implementations
- Understanding dependencies — which types depend on which
- Navigating DI registrations — resolves-to edges show IoC wiring
- Finding test coverage — covers/covered-by edges link tests to production code
- Answering any structural question that grep cannot reliably answer

**Trigger phrases:** "find callers of", "what calls", "who implements", "trace dependencies", "what depends on", "show type hierarchy", "find usages of", "what tests cover"

## Prerequisites

1. **Install CodeGraph** (one-time):
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Index the codebase** (run once, re-run after significant changes):
   ```bash
   codegraph index --solution YourApp.sln
   ```

3. **Verify graph exists**:
   ```bash
   ls .codegraph/*.json
   ```

If `.codegraph/` does not exist or is empty, run the index step first. If the graph is stale (older than recent commits), re-index with `codegraph index --solution YourApp.sln --changed-only`.

## How to Query

### Basic Queries

```bash
# Find a symbol and its direct relationships
codegraph query OrderService --depth 1

# Find what calls a specific method
codegraph query PlaceOrder --kind calls-from --depth 1

# Find what a method calls
codegraph query PlaceOrder --kind calls-to --depth 1

# Find implementations of an interface
codegraph query IOrderService --kind resolves-to

# Find type hierarchy
codegraph query OrderService --kind inherits --depth 2

# Find tests that cover a type
codegraph query OrderService --kind covered-by
```

### Wildcard Patterns

```bash
# All types matching a pattern
codegraph query "Order*" --depth 1

# All services
codegraph query "*Service" --depth 1

# Kind prefix for disambiguation
codegraph query "type:OrderService" --depth 1
codegraph query "method:PlaceOrder" --depth 1
```

### Filtering

```bash
# Filter by namespace
codegraph query OrderService --namespace "MyApp.Orders.*"

# Filter by project
codegraph query OrderService --project MyApp.Orders

# Filter by edge type
codegraph query OrderService --kind calls --depth 2

# Limit result size
codegraph query "Order*" --max-nodes 25
```

## CLI Reference

```
codegraph query <symbol-pattern> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth | `1` |
| `--kind <type>` | Edge filter (see below) | All kinds |
| `--namespace <pattern>` | Namespace filter (supports wildcards) | All |
| `--project <name>` | Project filter | All |
| `--format <fmt>` | Output: `context`, `json`, `text` | `context` |
| `--max-nodes <n>` | Maximum nodes in result | `50` |
| `--include-external` | Include NuGet/external dependencies | `false` |
| `--graph-dir <dir>` | Graph directory | `.codegraph` |

**Edge kind aliases:**

| Alias | Meaning |
|-------|---------|
| `calls`, `calls-to`, `calls-from` | Method call relationships |
| `inherits` | Class inheritance |
| `implements` | Interface implementation |
| `depends-on` | Type dependencies |
| `resolves-to` | DI container resolution |
| `covers` / `covered-by` | Test coverage |
| `references` | Symbol references |
| `overrides` | Method overrides |
| `contains` | Containment (namespace→type→method) |
| `all` | No filter |

## Output Format

Use `--format context` (default) for LLM consumption. It returns structured markdown:

```markdown
# Query: OrderService
Commit: abc1234 | Branch: main | 2026-01-15T10:30:00Z

## Target: MyApp.Services.OrderService (Type)
File: src/Orders/OrderService.cs [14–87]
Signature: public class OrderService : IOrderService

### Outgoing Relationships
**Calls:**
  → IOrderRepository.SaveAsync(Order)
  → IEventBus.PublishAsync(OrderPlacedEvent)

### Incoming Relationships
**Calls:**
  ← OrderController.PlaceOrder(OrderRequest)
```

Use `--format json` when you need structured data for programmatic processing.

## Fallback Behavior

If the query returns no results or the graph is unavailable:

1. **No `.codegraph/` directory** → Run `codegraph index --solution <path.sln>`
2. **Graph is stale** (meta.json timestamp is old) → Run `codegraph index --solution <path.sln> --changed-only`
3. **Symbol not found** → Try wildcard patterns (`*OrderService*`), check spelling, or try `--include-external`
4. **Too many results** → Add `--namespace`, `--project`, or `--kind` filters; reduce `--max-nodes`
5. **CodeGraph not installed** → Run `dotnet tool install -g CodeGraph`

## Best Practices

1. **Start broad, then narrow** — query at depth 1 first, then increase depth or add filters
2. **Use kind filters** — `--kind calls-to` is faster and more focused than `--kind all`
3. **Keep depth ≤ 3** — deeper traversals return large graphs; prefer multiple focused queries
4. **Keep max-nodes ≤ 50** — large results overwhelm context windows
5. **Prefer `context` format** — it's designed for LLM consumption with structured markdown
6. **Use `json` for chaining** — when you need to process results programmatically
7. **Query interfaces** — use `--kind resolves-to` or `--kind implements` to discover implementations
