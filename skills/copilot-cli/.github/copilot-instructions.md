# CodeGraph — Code Intelligence Graph

This project has a pre-built semantic code graph. **Always query the graph before
grepping or searching** for symbol relationships, call chains, or dependencies.

## Quick reference

```bash
# Query syntax
codegraph query <symbol> [--depth N] [--kind TYPE] [--format context]

# Or use the wrapper (handles install/graph checks):
bash skills/copilot-cli/query-wrapper.sh <symbol> [options]
```

## When to use the graph

Use `codegraph query` instead of grep/rg/find when:
- Finding what calls a method or depends on a type
- Understanding IOC wiring (interface → concrete resolution)
- Finding which tests cover a piece of code
- Tracing call chains across projects
- Assessing impact of a change

## Common queries

```bash
# What calls this method?
codegraph query PlaceOrder --kind calls-to --depth 1

# Full dependency tree, 2 levels deep
codegraph query OrderService --depth 2

# Test coverage for a method
codegraph query Reserve --kind covers

# Interface resolution
codegraph query IOrderRepository --kind resolves-to

# Scoped search within a project
codegraph query "*.Order*" --project MyApp.Services --depth 1
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--depth N` | Traversal depth (0=node only) | 1 |
| `--kind TYPE` | calls-to, calls-from, inherits, implements, depends-on, resolves-to, covers, all | all |
| `--namespace FILTER` | Namespace filter (wildcards OK) | none |
| `--project FILTER` | Project filter | none |
| `--format FMT` | json, text, context | context |
| `--max-nodes N` | Cap output size | 50 |
| `--include-external` | Include external deps | false |
| `--graph-dir PATH` | Graph data directory | .codegraph |

## Workflow

1. Query the graph for the symbol or relationship
2. Use `--format context` to get file paths and line ranges
3. Read only those files/ranges — avoid broad searches
4. If the graph is stale or missing, fall back to grep (note reduced accuracy)

## Graph info
- Data: `.codegraph/graph/`
- Metadata: `.codegraph/meta.json`
- Regenerate: `codegraph index --solution <path.sln> --output .codegraph/`
