---
applyTo: "**/*.cs"
---

# CodeGraph — Structural Code Understanding

Use the `codegraph_query` MCP tool to understand C# codebase structure instead of relying on grep.

## When to Use CodeGraph

- **Finding callers/callees** — ask "what calls PlaceOrder?" instead of grep
- **Tracing type hierarchies** — inheritance, interface implementations
- **Understanding dependencies** — which types depend on which
- **Navigating DI registrations** — resolves-to edges show IoC wiring
- **Finding test coverage** — covers/covered-by edges link tests to production code

## How to Query

The `codegraph_query` tool accepts these parameters:

- `symbol` (required) — symbol name or pattern with wildcards (`Order*`, `*Service`, `type:OrderService`)
- `depth` — BFS traversal depth (0 = node only, 1 = direct neighbors). Default: 1
- `kind` — edge type filter: `calls-to`, `calls-from`, `inherits`, `implements`, `depends-on`, `resolves-to`, `covers`, `covered-by`, `references`, `overrides`, `contains`, `all`
- `namespace` — namespace filter with wildcards
- `project` — project/assembly filter
- `format` — output format: `context` (default, markdown), `json`, `text`
- `max_nodes` — maximum nodes to return. Default: 50
- `include_external` — include external/NuGet dependency nodes. Default: false

## Best Practices

1. Start with a broad query at depth 1, then narrow down
2. Use `kind` filters to focus on specific relationship types
3. Use `context` format for readable summaries, `json` for structured data
4. Query interfaces to discover implementations via `resolves-to` or `implements`
5. Prefer CodeGraph over grep for structural questions about the codebase
