# How to Use `codegraph snapshot`

`codegraph snapshot` saves, lists, and deletes named copies of the current graph. Snapshots are the building block for `codegraph diff` — save a snapshot before a change, re-index after the change, then diff the two to see exactly what moved.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Save the current graph as "before-refactor"
codegraph snapshot save before-refactor

# … make changes to your code …

# Re-index
codegraph index --solution MyApp.sln

# Diff the current graph against the saved snapshot
codegraph diff --base .codegraph-snapshots/before-refactor
```

---

## CLI Reference

```
codegraph snapshot <save|list|delete> [name] [options]
```

### Sub-commands

| Sub-command | Description |
|-------------|-------------|
| `save <name>` | Save the current graph as a named snapshot |
| `list` | Print all saved snapshots with timestamps and paths |
| `delete <name>` | Delete a named snapshot |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory to snapshot (save) or containing snapshots (list/delete) | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Save
codegraph snapshot save before-refactor
codegraph snapshot save v1.2.0-baseline
codegraph snapshot save pre-merge --graph-dir .codegraph

# List
codegraph snapshot list

# Delete
codegraph snapshot delete before-refactor
```

---

## Understanding the Output

### `snapshot list`

```
  before-refactor      2026-01-15 09:30:00  .codegraph-snapshots/before-refactor
  v1.2.0-baseline      2026-01-10 14:45:12  .codegraph-snapshots/v1.2.0-baseline
```

Each row shows the snapshot name, the timestamp it was saved, and the path where the snapshot files are stored.

---

## Common Workflows

### Before/After Refactoring

```bash
# 1. Save current state
codegraph snapshot save before-refactor

# 2. Make your code changes

# 3. Re-index
codegraph index --solution MyApp.sln

# 4. Compare
codegraph diff --base .codegraph-snapshots/before-refactor
```

### Tagging a Release Baseline

```bash
# After tagging v2.0.0 in git:
codegraph snapshot save v2.0.0

# Later, compare feature work against the release baseline:
codegraph diff --base .codegraph-snapshots/v2.0.0
```

### CI: Track Structural Drift

```yaml
- name: Save baseline snapshot
  run: codegraph snapshot save ci-baseline

- name: Apply migrations / code-gen
  run: ./generate.sh

- name: Re-index
  run: codegraph index --solution MyApp.sln

- name: Diff against baseline
  run: codegraph diff --base .codegraph-snapshots/ci-baseline --format json
```

### Cleaning Up Old Snapshots

```bash
codegraph snapshot list
codegraph snapshot delete before-refactor
codegraph snapshot delete v1.1.0-baseline
```

---

## See Also

- [`codegraph diff`](diff.md) — compare two graph snapshots and report structural changes
- [`codegraph stats`](stats.md) — quick numeric comparison between snapshot directories
- [`codegraph index`](../README.md#codegraph-index) — re-index before saving or comparing snapshots
