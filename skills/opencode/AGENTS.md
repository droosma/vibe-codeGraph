# AGENTS.md — CodeGraph Code Intelligence

## Agent: codegraph

### Description

Query the pre-built semantic code graph instead of grepping the codebase.
Use this agent whenever asked about call chains, dependencies, interface
implementations, DI wiring, test coverage, or impact analysis. Also use it
when asked "what calls X", "what depends on X", "what implements X", or
"what tests cover X" — even if the user doesn't mention the graph.

### Tool definition

```yaml
name: codegraph_query
description: Query the CodeGraph semantic code graph for symbol relationships
command: bash skills/opencode/query-wrapper.sh
args:
  - name: symbol
    description: Symbol name or pattern to query (supports wildcards)
    required: true
  - name: depth
    description: Traversal depth (0 = node only)
    default: "1"
  - name: kind
    description: "Edge filter: calls-to, inherits, implements, depends-on, resolves-to, covers, all"
    default: "all"
  - name: format
    description: "Output format: context, json, text"
    default: "context"
  - name: namespace
    description: Namespace filter (supports wildcards)
  - name: project
    description: Project filter
  - name: max-nodes
    description: Cap output size
    default: "50"
```

### How to query

```bash
codegraph query <symbol> [--depth N] [--kind TYPE] [--format context]
```

**Example 1 — call chain:**
Input: "What calls PlaceOrder?"
Output: `codegraph query PlaceOrder --kind calls-to --depth 1`

**Example 2 — DI resolution:**
Input: "What does IOrderRepository resolve to?"
Output: `codegraph query IOrderRepository --kind resolves-to`

**Example 3 — test coverage:**
Input: "What tests cover Reserve?"
Output: `codegraph query Reserve --kind covers`

**Example 4 — dependency tree:**
Input: "Show me the dependency tree of OrderService"
Output: `codegraph query OrderService --depth 2`

### Workflow

1. Query the graph for the symbol or relationship
2. Use `--format context` output to get file paths and line ranges
3. Read ONLY those files/ranges — avoid broad grep/search
4. If the graph is stale or missing, fall back to grep and note reduced accuracy

### Graph info

- Data: `.codegraph/` (split by assembly)
- Metadata: `.codegraph/meta.json`
- Regenerate: `codegraph index --solution <path.sln> --output .codegraph/`
