# How to Use `codegraph export`

`codegraph export` reads the SQLite graph database (`graph.db`) produced by `codegraph index` and writes the graph back out as JSON files — one file per assembly, plus `_external.json` and `meta.json`. This is useful for interoperability with tools that consume JSON, archiving a snapshot, or migrating between CodeGraph versions.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Export graph.db to JSON (written to ./export/)
codegraph export
```

---

## CLI Reference

```
codegraph export [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--output <dir>` | Output directory for JSON files | `export` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph export                                       # Export .codegraph/graph.db → export/
codegraph export --output my-snapshot                  # Export to custom directory
codegraph export --graph-dir .codegraph --output .     # Export JSON alongside graph.db
codegraph export --graph-dir .codegraph/Api --output export/Api  # Multi-solution sub-graph
```

---

## Output Structure

After running `codegraph export`, the output directory contains:

```
export/
├── meta.json               # Git metadata, statistics, index timestamp
├── MyApp.Core.json         # Graph for the MyApp.Core assembly
├── MyApp.Api.json          # Graph for the MyApp.Api assembly
├── MyApp.Infrastructure.json
└── _external.json          # External/NuGet dependencies (SBOM-like graph)
```

Each assembly JSON file follows the [Graph Schema](graph-schema.md).

---

## When to Use Export

| Use Case | Recommendation |
|----------|---------------|
| **Interoperability** — feed the graph to external tools | Use `export` to produce JSON files for consumption |
| **Archiving** — save a versioned snapshot for later comparison | Use `export` to create a timestamped directory |
| **Migration** — move between SQLite and JSON storage | Use `export` to convert the SQLite graph to JSON |
| **Querying** — use the CodeGraph query engine | Use the SQLite database directly (no export needed) |

---

## Common Workflows

### Archive a Snapshot Before a Major Refactor

```bash
# Save current state
codegraph export --output snapshots/before-refactor

# Do the refactor...

# Re-index and compare
codegraph index --solution MyApp.sln
codegraph export --output snapshots/after-refactor

codegraph diff --base snapshots/before-refactor --head snapshots/after-refactor
```

### Export for External Tooling

```bash
# Export as JSON for processing with jq or other tools
codegraph export --output graph-json

# Example: count all types using jq
jq '[.nodes[] | select(.kind == "Type")] | length' graph-json/MyApp.Core.json
```

### CI Artifact Publishing

```bash
# Export JSON graph as a CI artifact for download or analysis
codegraph export --output artifacts/graph-json
```

```yaml
- name: Export graph as JSON
  run: codegraph export --output artifacts/graph-json

- uses: actions/upload-artifact@v4
  with:
    name: codegraph-json
    path: artifacts/graph-json/
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), export one sub-graph at a time:

```bash
codegraph export --graph-dir .codegraph/Api --output export/Api
codegraph export --graph-dir .codegraph/Workers --output export/Workers
```

---

## See Also

- [Graph Schema Reference](graph-schema.md) — structure of the exported JSON files
- [`codegraph diff`](diff.md) — compare two graph snapshots
- [`codegraph stats`](stats.md) — print statistics from the graph
