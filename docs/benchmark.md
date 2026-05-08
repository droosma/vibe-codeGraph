# How to Use `codegraph benchmark`

`codegraph benchmark` runs a set of named query scenarios against your indexed graph and reports timing statistics (min, max, median, mean) alongside result sizes (nodes, edges, estimated tokens). It is useful for measuring query performance before and after changes to the graph or query engine, and for validating that your graph is fast enough for interactive agent use.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Run the built-in default scenarios (5 iterations each)
codegraph benchmark

# Run with more iterations for stable measurements
codegraph benchmark --iterations 20

# Use a custom scenario file
codegraph benchmark --scenarios benchmarks/scenarios.json
```

---

## CLI Reference

```
codegraph benchmark [options]
```

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--scenarios <path>` | Path to a JSON scenarios file | Built-in defaults |
| `--iterations <n>` | Number of times to run each scenario | `5` |
| `--format <fmt>` | Output format: `text` (Markdown table) or `json` | `text` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph benchmark                                      # Built-in scenarios, 5 iterations
codegraph benchmark --iterations 20                      # More stable measurements
codegraph benchmark --scenarios my-scenarios.json        # Custom scenarios
codegraph benchmark --format json > results.json         # JSON output for CI
codegraph benchmark --graph-dir .codegraph/Api           # Benchmark a sub-graph
```

---

## Scenario File Format

Scenarios are defined in a JSON file. Each scenario specifies a command and its arguments:

```json
{
  "scenarios": [
    {
      "name": "query-order-service",
      "description": "Single type query at depth 1",
      "command": "query",
      "args": {
        "pattern": "OrderService",
        "depth": 1
      }
    },
    {
      "name": "search-service",
      "description": "Broad search for Service types",
      "command": "search",
      "args": {
        "query": "Service",
        "top": 50
      }
    },
    {
      "name": "list-assemblies",
      "description": "List all assemblies",
      "command": "list",
      "args": {
        "scope": "assemblies"
      }
    }
  ]
}
```

The repository ships with a default `benchmarks/scenarios.json` that covers common query patterns.

---

## Understanding the Output

### Markdown (default)

```markdown
## Benchmark Results

| Scenario        | Iterations | Min   | Max   | Median | Mean  | Nodes | Edges | Tokens |
|-----------------|------------|-------|-------|--------|-------|-------|-------|--------|
| query-single    | 5          | 12ms  | 18ms  | 14ms   | 14ms  | 23    | 41    | 1240   |
| search-broad    | 5          | 8ms   | 11ms  | 9ms    | 9ms   | 50    | 0     | 620    |
| list-assemblies | 5          | 3ms   | 5ms   | 4ms    | 4ms   | 0     | 0     | 180    |
```

**Min/Max/Median/Mean** — wall-clock timing across all iterations.

**Nodes / Edges** — result size of the last iteration.

**Tokens** — estimated LLM token count of the formatted output, useful for checking context window fit.

### JSON (`--format json`)

```json
[
  {
    "scenario": "query-single",
    "iterations": 5,
    "min_ms": 12.1,
    "max_ms": 18.3,
    "median_ms": 14.0,
    "mean_ms": 14.2,
    "result_nodes": 23,
    "result_edges": 41,
    "output_tokens": 1240
  }
]
```

---

## Common Workflows

### Baseline + Regression Check

```bash
# Measure before a change
codegraph benchmark --iterations 20 --format json > before.json

# Make changes, re-index
codegraph index --solution MyApp.sln

# Measure after
codegraph benchmark --iterations 20 --format json > after.json

# Compare manually or with jq
jq '[.[] | {scenario, mean_ms}]' before.json after.json
```

### CI Performance Gate

```yaml
- name: Run graph benchmarks
  run: codegraph benchmark --iterations 10 --format json > benchmark-results.json

- name: Upload benchmark results
  uses: actions/upload-artifact@v4
  with:
    name: benchmark-results
    path: benchmark-results.json
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — run individual queries
- [`codegraph stats`](stats.md) — quick graph size overview
- [BENCHMARK-PLAYBOOK.md](BENCHMARK-PLAYBOOK.md) — A/B benchmark methodology for validating CodeGraph's effectiveness
