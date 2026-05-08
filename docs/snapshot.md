# How to Use `codegraph snapshot`

`codegraph snapshot` saves, lists, and deletes named copies of the graph directory. Snapshots are stored under `.codegraph-snapshots/<name>/` and can be used as the `--base` or `--head` argument to `codegraph diff`.

Snapshots are the recommended way to preserve a graph state for later comparison (e.g., before a refactor, before cutting a release branch, or in CI between runs).

---

## Quick Start

```bash
# Save the current graph as "before-refactor"
codegraph snapshot save before-refactor

# Make changes, re-index
codegraph index --solution MyApp.sln

# Diff before vs. current
codegraph diff --base .codegraph-snapshots/before-refactor

# List all snapshots
codegraph snapshot list

# Clean up
codegraph snapshot delete before-refactor
```

---

## CLI Reference

```
codegraph snapshot <save|list|delete> [name] [--graph-dir <dir>]
```

| Sub-command | Description |
|-------------|-------------|
| `save <name>` | Copy the current graph to `.codegraph-snapshots/<name>/` |
| `list` | Print all stored snapshots with creation date and path |
| `delete <name>` | Remove a named snapshot |

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Source graph directory (for `save`) | `.codegraph` |

---

## What Gets Saved

`codegraph snapshot save` copies the following files from the source graph directory:

| File | Description |
|------|-------------|
| `graph.db` | SQLite graph database (primary) |
| `meta.json` | Index metadata (commit hash, branch, stats) |
| `*.json` | Per-assembly JSON files (backwards-compat) |

The snapshot is stored at `.codegraph-snapshots/<name>/`. If a snapshot with the same name already exists, it is overwritten.

---

## Example: Release Workflow

```bash
# Before cutting a release branch, save the graph
codegraph snapshot save release-1.2

# Later, on main after more development
codegraph diff --base .codegraph-snapshots/release-1.2 --format context
```

---

## Example: CI Pre/Post Check

```yaml
# .github/workflows/structural-diff.yml
- name: Save pre-change snapshot
  run: |
    codegraph index --solution MyApp.sln
    codegraph snapshot save pre-change

- name: Apply changes
  run: git apply patch.diff

- name: Re-index and diff
  run: |
    codegraph index --solution MyApp.sln
    codegraph diff --base .codegraph-snapshots/pre-change --format text
```

---

## Listing Snapshots

```bash
codegraph snapshot list
```

Output example:

```
  before-refactor      2026-05-01 14:23:11  .codegraph-snapshots/before-refactor
  release-1.2          2026-04-15 09:00:44  .codegraph-snapshots/release-1.2
  pre-change           2026-05-08 21:05:30  .codegraph-snapshots/pre-change
```

---

## `.gitignore` Recommendation

Snapshots can be large. Add the snapshots directory to `.gitignore` unless you intentionally version them:

```gitignore
.codegraph-snapshots/
```

To version specific named snapshots for auditing, add them explicitly:

```gitignore
.codegraph-snapshots/
!.codegraph-snapshots/release-*/
```

---

## See Also

- [`codegraph diff`](diff.md) — compare any two snapshots
- [`codegraph index`](../README.md#codegraph-index) — generate or update the graph
- [Graph Schema Reference](graph-schema.md) — what's in `meta.json` and `graph.db`
