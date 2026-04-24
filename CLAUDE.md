# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Repository Overview

**CodeGraph** gives AI coding agents structural code understanding instead of grep. It builds a semantic graph of C# codebases, queryable via MCP.

## Available Skills

Install skills with:

```bash
npx skills add droosma/vibe-codeGraph
```

Or use individual skills:

| Skill | Purpose | Install |
|-------|---------|---------|
| `codegraph-query` | Query the semantic graph | `npx skills add droosma/vibe-codeGraph@codegraph-query` |
| `codegraph-index` | Index/re-index a codebase | `npx skills add droosma/vibe-codeGraph@codegraph-index` |
| `codegraph-review` | Impact analysis for code review | `npx skills add droosma/vibe-codeGraph@codegraph-review` |

See the [`skills/`](skills/) directory for detailed instructions per skill.

## Using CodeGraph

### Prerequisites

```bash
dotnet tool install -g CodeGraph
codegraph init
codegraph index --solution YourApp.sln
```

### Querying

Use the `codegraph_query` MCP tool or the CLI:

```bash
codegraph query <symbol> --depth 1 --format context
```

### Key Parameters

- `symbol` — symbol name or pattern (`Order*`, `type:OrderService`)
- `depth` — BFS traversal depth (default: 1)
- `kind` — edge filter: `calls-to`, `calls-from`, `inherits`, `implements`, `depends-on`, `resolves-to`, `covers`, `covered-by`
- `format` — `context` (markdown, default), `json`, `text`
- `max_nodes` — limit results (default: 50)

### Best Practices

1. Start with depth 1, narrow with filters
2. Use `--kind` to focus on specific relationship types
3. Use `context` format for readable summaries
4. Query interfaces to discover implementations
5. Prefer CodeGraph over grep for structural questions

## Development

```bash
dotnet build CodeGraph.sln
dotnet test CodeGraph.sln
```
