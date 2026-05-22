# How to Use `codegraph snapshot`

`codegraph snapshot` lets you **save, list, and delete named copies** of the current graph. Snapshots are the foundation for diff workflows — save a snapshot before a PR, then run `codegraph diff` after merging to see exactly what changed structurally.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Save the current graph as "before-refactor"
codegraph snapshot save before-refactor

# List all saved snapshots
codegraph snapshot list

# Delete a snapshot
codegraph snapshot delete before-refactor
```

---

## CLI Reference

```
codegraph snapshot <subcommand> [name] [options]
```

### Subcommands

| Subcommand | Description |
|-----------|-------------|
| `save <name>` | Copy the current graph into a named snapshot |
| `list` | Show all saved snapshots with creation timestamps and paths |
| `delete <name>` | Remove a named snapshot |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory to snapshot | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## Where Snapshots Are Stored

Snapshots are stored as subdirectories inside the graph directory:

```
.codegraph/
  graph.db          ← current live graph
  BRIEF.md
  snapshots/
    before-refactor/  ← snapshot created with `snapshot save before-refactor`
      graph.db
    v1.5.0/
      graph.db
```

---

## Common Workflows

### Snapshot before a refactor

```bash
# Save the baseline
codegraph snapshot save before-refactor

# Make code changes...

# Re-index
codegraph index --solution MyApp.sln

# Compare
codegraph diff --base .codegraph/snapshots/before-refactor --head .codegraph
```

### Snapshot at release time

```bash
# Tag the graph at each release
codegraph snapshot save v$(cat VERSION)

# Later, compare releases
codegraph diff \
  --base .codegraph/snapshots/v1.4.0 \
  --head .codegraph/snapshots/v1.5.0
```

### List and clean up old snapshots

```bash
codegraph snapshot list

# Remove snapshots you no longer need
codegraph snapshot delete old-experiment
```

### CI: automatic nightly snapshot

```yaml
- name: Save graph snapshot
  run: |
    codegraph index --solution MyApp.sln
    codegraph snapshot save nightly-$(date +%Y%m%d)
```

---

## Relationship to `codegraph diff`

`snapshot` and `diff` are complementary:

| Tool | Purpose |
|------|---------|
| `codegraph snapshot save` | Capture the current graph state under a name |
| `codegraph diff` | Compare any two graph directories (including snapshots) |

See [docs/diff.md](diff.md) for the full diff guide.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Subcommand completed successfully |
| `1` | Error (missing name, snapshot not found, or graph not built) |
