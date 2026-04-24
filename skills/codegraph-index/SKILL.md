---
name: codegraph-index
description: Index or re-index a C# codebase to build its semantic graph. Use when asked to "index the codebase", "build the graph", "update the graph", or before querying when no graph exists.
metadata:
  author: codegraph-contributors
  version: "1.0.0"
  argument-hint: <solution-path>
---

# CodeGraph Index

Build or update the semantic graph of a C# codebase so it can be queried for structural relationships.

## When to Use

Use this skill when:
- Setting up CodeGraph for the first time on a codebase
- The graph is missing (no `.codegraph/` directory)
- The graph is stale (code has changed since last index)
- New projects were added to the solution
- You need to re-index after a major refactor
- A query returns unexpected results (stale graph)

**Trigger phrases:** "index the codebase", "build the graph", "update the graph", "re-index", "graph is stale", "set up CodeGraph"

## Prerequisites

1. **Install CodeGraph** (one-time):
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **.NET SDK** must be installed (8.0 or later)

3. **A C# solution** (`.sln` or `.slnx` file) must exist in the repository

## How It Works

1. CodeGraph runs `dotnet restore` on your solution to generate `project.assets.json` files
2. It walks the Roslyn syntax trees to extract structure (types, methods, properties)
3. It uses the semantic model to resolve relationships (calls, inheritance, DI, tests)
4. It writes JSON graph files to `.codegraph/` — one per assembly, plus externals and metadata

## How to Index

### First-Time Setup

```bash
# Initialize config and MCP registration (optional but recommended)
codegraph init

# Build the full graph
codegraph index --solution YourApp.sln
```

`codegraph init` creates:
- `codegraph.json` — configuration file
- `.vscode/mcp.json` — MCP server for VS Code Copilot
- `.mcp.json` — MCP server for Claude Code
- `apm.yml` — APM configuration

### Incremental Re-Index

After making code changes, re-index only the changed projects:

```bash
codegraph index --solution YourApp.sln --changed-only
```

This compares the current git state to the last indexed commit and only re-processes changed projects.

### Full Re-Index

Force a complete rebuild of the graph:

```bash
codegraph index --solution YourApp.sln
```

### Filtered Index

Index only specific projects:

```bash
codegraph index --solution YourApp.sln --projects "MyApp.Orders.*"
```

### Skip Restore

If you've already restored the solution:

```bash
codegraph index --solution YourApp.sln --skip-restore
```

## CLI Reference

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
| `--skip-restore` | Skip `dotnet restore` step | `false` |
| `--skip-build` | Hidden alias for `--skip-restore` | `false` |
| `--changed-only` | Incremental: only re-index changed projects | `false` |
| `--verbose` | Enable verbose output | `false` |

## Output

After indexing, `.codegraph/` contains:

| File | Content |
|------|---------|
| `<ProjectName>.json` | Semantic graph for one assembly (nodes + edges) |
| `_external.json` | External/NuGet dependency graph (SBOM-like) |
| `meta.json` | Git metadata, statistics, index timestamp |

### Verifying the Index

```bash
# Check that graph files were created
ls .codegraph/

# Check graph statistics
cat .codegraph/meta.json

# Test a quick query
codegraph query "*" --depth 0 --max-nodes 5
```

## Fallback Behavior

1. **Restore fails** → Fix package resolution errors first, then re-index
2. **Solution not found** → Provide explicit `--solution` path
3. **Large solutions are slow** → Use `--projects` to filter, or `--skip-restore` if already restored
4. **Out of memory** → Index subsets with `--projects` filter
5. **CodeGraph not installed** → Run `dotnet tool install -g CodeGraph`

## Best Practices

1. **Index in CI** — add `codegraph index --solution YourApp.sln --changed-only` to your CI pipeline to keep the graph fresh
2. **Commit `.codegraph/` selectively** — graph files can be large; consider `.gitignore`-ing them and regenerating in CI
3. **Use `--changed-only`** — for incremental updates, it's much faster than a full re-index
4. **Use `--skip-restore`** — if your CI already restored packages, skip the redundant restore step
5. **Filter large solutions** — use `--projects` to index only the projects you care about
