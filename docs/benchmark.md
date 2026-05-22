# How to Use `codegraph benchmark`

`codegraph benchmark` runs a set of query scenarios against the indexed graph and measures execution time. Use it to validate query performance regressions, compare graph sizes, or establish a performance baseline for CI.

This command measures **graph query performance** (how fast CodeGraph answers questions). For the A/B benchmark that compares agent effectiveness with vs. without CodeGraph, see [BENCHMARK-PLAYBOOK.md](BENCHMARK-PLAYBOOK.md).

---

## Quick Start

```bash
# Index the codebase first
codegraph index --solution MyApp.sln

# Run the built-in scenarios (5 iterations each)
codegraph benchmark

# Use a custom scenarios file
codegraph benchmark --scenarios benchmarks/scenarios.json

# More iterations for stable averages
codegraph benchmark --iterations 20

# Output as JSON for CI artifact ingestion
codegraph benchmark --format json > benchmark-results.json
```

---

## CLI Reference

```
codegraph benchmark [--scenarios <path>] [--iterations N] [--format json] [--graph-dir <dir>]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--scenarios <path>` | Path to a JSON scenarios file | Built-in scenarios |
| `--iterations <n>` | Number of times to run each scenario | `5` |
| `--format json` | Output as JSON instead of Markdown | Markdown |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## Scenarios File Format

Scenarios are defined in a JSON file:

```json
{
  "scenarios": [
    {
      "name": "query-single",
      "description": "Single type query at depth 1",
      "command": "query",
      "args": {
        "pattern": "OrderService",
        "depth": 1,
        "format": "compact"
      }
    },
    {
      "name": "search-broad",
      "description": "Broad semantic search",
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

The built-in scenarios file ships with CodeGraph at `benchmarks/scenarios.json` (next to the executable). If it exists, it is used by default; otherwise three minimal built-in scenarios run instead.

### Supported `command` values

| Value | Maps to |
|-------|---------|
| `query` | `codegraph query` |
| `search` | `codegraph search` (semantic search) |
| `list` | `codegraph list` |

---

## Example Output

### Markdown (default)

```markdown
## CodeGraph Benchmark Results

| Scenario       | Description                  | Iterations | Avg (ms) | Min (ms) | Max (ms) |
|----------------|------------------------------|------------|----------|----------|----------|
| query-single   | Single type query at depth 1 | 5          | 12       | 10       | 18       |
| search-broad   | Broad semantic search        | 5          | 34       | 30       | 41       |
| list-assemblies| List all assemblies          | 5          | 8        | 7        | 11       |
```

### JSON (`--format json`)

```json
{
  "scenarios": [
    {
      "name": "query-single",
      "description": "Single type query at depth 1",
      "iterations": 5,
      "avgMs": 12,
      "minMs": 10,
      "maxMs": 18
    }
  ]
}
```

---

## CI Integration

Track query performance across commits:

```yaml
# .github/workflows/benchmark.yml
- name: Run benchmarks
  run: codegraph benchmark --iterations 10 --format json > benchmark-results.json

- name: Upload benchmark artifact
  uses: actions/upload-artifact@v4
  with:
    name: benchmark-results
    path: benchmark-results.json
```

To fail CI when a scenario exceeds a threshold:

```bash
result=$(codegraph benchmark --format json)
slow=$(echo "$result" | jq '[.scenarios[] | select(.avgMs > 100)] | length')
if [ "$slow" -gt 0 ]; then
  echo "Performance regression detected:" >&2
  echo "$result" | jq '.scenarios[] | select(.avgMs > 100)' >&2
  exit 1
fi
```

---

## See Also

- [BENCHMARK-PLAYBOOK.md](BENCHMARK-PLAYBOOK.md) — methodology for A/B benchmarking agent effectiveness
- [`codegraph daemon`](daemon.md) — persistent daemon that eliminates process-spawn overhead
- [`codegraph index`](../README.md#codegraph-index) — required before benchmarking
