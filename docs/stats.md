# How to Use `codegraph stats`

`codegraph stats` prints a quick numeric overview of the indexed code graph: total node and edge counts, broken down by kind and type. It is useful for sanity-checking an index, comparing graph sizes between branches, or monitoring codebase growth over time.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Print statistics for the default graph directory (.codegraph/)
codegraph stats
```

---

## CLI Reference

```
codegraph stats [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph stats                                  # Stats for .codegraph/
codegraph stats --graph-dir .codegraph-prev      # Stats for an older snapshot
codegraph stats --graph-dir .codegraph/Api       # Stats for a multi-solution sub-graph
```

---

## Example Output

```
CodeGraph Statistics
  Total nodes: 1 842
  Total edges: 5 219

Nodes by kind:
  Method: 982
  Type: 421
  Property: 287
  Namespace: 64
  Field: 58
  Constructor: 22
  Event: 8

Edges by type:
  Calls: 2 104
  Contains: 1 388
  References: 901
  Implements: 312
  DependsOn: 298
  Covers: 127
  Inherits: 58
  ResolvesTo: 31
```

---

## Common Workflows

### Sanity Check After Indexing

```bash
codegraph index --solution MyApp.sln
codegraph stats
# Confirm node and edge counts look reasonable for your codebase size.
```

### Comparing Graph Sizes Between Branches

```bash
# On main — save current graph
cp -r .codegraph .codegraph-main

# On feature branch — re-index and compare
codegraph index --solution MyApp.sln
codegraph stats --graph-dir .codegraph-main  # Before
codegraph stats                              # After
```

### CI Monitoring

Use `codegraph stats` to emit graph metrics as part of a CI step for trend tracking:

```yaml
- name: Index code graph
  run: codegraph index --solution MyApp.sln

- name: Print graph statistics
  run: codegraph stats
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), each solution's graph is stored in a subdirectory. Run `stats` per sub-directory or on the root to aggregate:

```bash
codegraph stats --graph-dir .codegraph/Api
codegraph stats --graph-dir .codegraph/Workers
```

---

## See Also

- [`codegraph list`](list.md) — browse assemblies, types, interfaces, and namespaces interactively
- [`codegraph report`](report.md) — full Markdown report with hub analysis and suggested queries
- [`codegraph diff`](diff.md) — compare structural changes between two graph snapshots
