# How to Use `codegraph diff`

`codegraph diff` compares two graph snapshots and reports structural changes to your C# codebase — added or removed types, methods, and relationships, plus signature changes. It is designed for use in CI/CD pipelines, PR reviews, and AI-assisted impact analysis.

---

## Quick Start

```bash
# Take a snapshot before your changes
cp -r .codegraph .codegraph-prev

# Make code changes, then re-index
codegraph index --solution MyApp.sln

# Show what changed
codegraph diff
```

By default, `codegraph diff` reads `.codegraph-prev` as the base and `.codegraph` as the head and outputs a Markdown-style `context` report.

---

## Concepts

A **snapshot** is any directory containing a full CodeGraph output (one JSON file per assembly, `meta.json`, `_external.json`). You take a snapshot simply by copying `.codegraph/` elsewhere before running `codegraph index` again.

The diff engine compares nodes by their **fully-qualified ID** and edges by a composite key (`fromId`, `toId`, `type`, `isExternal`, `resolution`). This makes the diff purely structural — it does not compare source text, only the semantic graph.

---

## CLI Reference

```
codegraph diff [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--base <path>` | Directory of the base (before) graph snapshot | `.codegraph-prev` |
| `--head <path>` | Directory of the head (after) graph snapshot | `.codegraph` |
| `--ref <name>` | Look up a named snapshot saved with `codegraph snapshot save`. Resolves to `<head>/.codegraph/snapshots/<name>`, with SHA-based fallbacks. | (none) |
| `--only <types>` | Comma-separated filter (see table below) | All change types |
| `--format <fmt>` | `context` \| `text` \| `json` | `context` |

### `--only` values

| Token | What it includes |
|-------|-----------------|
| `added` | Added nodes + added edges |
| `removed` | Removed nodes + removed edges |
| `signature-changed` | Nodes whose signature changed |
| `added-nodes` | Only added nodes |
| `removed-nodes` | Only removed nodes |
| `added-edges` | Only added edges |
| `removed-edges` | Only removed edges |

Combine with commas: `--only removed,signature-changed`

---

## Output Formats

### `context` (default)

Markdown suitable for pasting into a prompt or PR description.

```markdown
# Graph Diff: abc1234..def5678

## Added Nodes (1)
- MyApp.Services.ShippingService (type) — src/Services/ShippingService.cs:10-42

## Removed Nodes (0)
- None

## Changed Signatures (1)
- MyApp.Services.OrderService.PlaceOrder(OrderRequest)
  - Was: public async Task<Order> PlaceOrder(OrderRequest request)
  + Now: public async Task<Result<Order>> PlaceOrder(OrderRequest request)

## New Edges (2)
- MyApp.Services.OrderService.PlaceOrder(OrderRequest) → MyApp.Services.ShippingService.Ship(Order) (Calls)
- MyApp.Services → MyApp.Services.ShippingService (Contains)

## Removed Edges (0)
- None
```

### `text`

One-line-per-metric summary, useful for quick CI log scanning.

```
Graph Diff abc1234..def5678
Added nodes: 1
Removed nodes: 0
Signature changes: 1
Added edges: 2
Removed edges: 0
```

### `json`

Full `GraphDiffResult` as indented camelCase JSON. All enum values are camelCase strings.

```json
{
  "baseMetadata": { "commitHash": "abc1234...", "branch": "main", ... },
  "headMetadata":  { "commitHash": "def5678...", "branch": "feature/shipping", ... },
  "addedNodes": [ { "id": "MyApp.Services.ShippingService", "kind": "type", ... } ],
  "removedNodes": [],
  "signatureChangedNodes": [
    {
      "previous": { "id": "...", "signature": "public async Task<Order> PlaceOrder(..." },
      "current":  { "id": "...", "signature": "public async Task<Result<Order>> PlaceOrder(..." }
    }
  ],
  "addedEdges": [ ... ],
  "removedEdges": []
}
```

---

## CI/CD Patterns

### Detect breaking API changes in pull requests

```yaml
# .github/workflows/diff.yml
name: Graph Diff

on:
  pull_request:
    paths:
      - 'src/**'

jobs:
  diff:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Install CodeGraph
        run: dotnet tool install -g CodeGraph

      # Index the base branch
      - name: Index base
        run: |
          git checkout ${{ github.base_ref }}
          codegraph index --solution MyApp.sln --output .codegraph-base
          git checkout ${{ github.head_ref }}

      # Index the PR branch
      - name: Index head
        run: codegraph index --solution MyApp.sln

      # Show breaking changes (removed symbols + signature changes)
      - name: Diff
        run: |
          codegraph diff \
            --base .codegraph-base \
            --head .codegraph \
            --only removed,signature-changed \
            --format text
```

### Keep a rolling snapshot for nightly drift detection

```bash
#!/usr/bin/env bash
# Run nightly, commit the result to a snapshots branch or artifact storage

set -euo pipefail

DATE=$(date -u +%Y%m%d)
YESTERDAY=$(date -u -d 'yesterday' +%Y%m%d 2>/dev/null || date -u -v-1d +%Y%m%d)

# Re-index
codegraph index --solution MyApp.sln

# Save today's snapshot
codegraph snapshot save "nightly-${DATE}"

# Report changes since yesterday (if yesterday's snapshot exists)
codegraph diff --ref "nightly-${YESTERDAY}" --format context || true
```

### Compare against a named branch snapshot

```bash
# Save a snapshot when cutting a release branch
codegraph snapshot save release-1.0

# Later, compare current HEAD against the release baseline
codegraph diff --ref release-1.0

# Or pass the full snapshot path explicitly
codegraph diff --base .codegraph/snapshots/release-1.0
```

---

## Providing Diff Output to an AI Agent

The `context` format is designed to feed directly into an LLM prompt. Pipe it or embed it in a system message:

```bash
DIFF=$(codegraph diff --only removed,signature-changed)

# Prepend to your prompt
echo "The following structural changes were made:\n\n${DIFF}\n\nDescribe the impact on consumers."
```

Because the diff only includes changed symbols (not the entire graph), it stays within typical context window limits even for large codebases.

---

## Snapshot Storage

Named snapshots (created with `codegraph snapshot save`) are stored inside the graph directory:

```
.codegraph/
  graph.db            ← live graph
  snapshots/
    release-1.0/      ← codegraph snapshot save release-1.0
    nightly-20260101/ ← codegraph snapshot save nightly-20260101
    main/             ← codegraph snapshot save main
```

Use `--base <path>` when you need ad-hoc copies (e.g., from a manual `cp -r .codegraph .codegraph-prev`) or to point at an arbitrary directory. Use `--ref <name>` to reference snapshots managed by `codegraph snapshot`.

Add `.codegraph-prev` (and any ad-hoc snapshot directories) to `.gitignore` if you don't want them versioned:

```gitignore
.codegraph-prev/
```

---

## Troubleshooting

**`Error: Could not locate a graph snapshot for ref '<ref>'`**

The `--ref` flag looks for a named snapshot under `.codegraph/snapshots/<ref>` (plus SHA-based variants). Save a snapshot first with `codegraph snapshot save <ref>`, or pass `--base <path>` explicitly.

**Diff shows unexpected removals after re-indexing**

The graph is deterministic for the same source, but staleness can cause phantom diffs. Verify both snapshots were built from clean builds:

```bash
codegraph index --solution MyApp.sln --skip-restore  # only if packages are already restored
```

**Empty diff despite code changes**

Not all code changes produce graph changes. Changes to method bodies (without signature changes), comments, whitespace, and formatting are invisible to the graph. The diff only reflects structural and semantic changes visible to Roslyn's symbol model.

---

## See Also

- [`codegraph compare`](compare.md) — structural side-by-side comparison of two symbols (not snapshots)
- [`codegraph stats`](stats.md) — print graph statistics for quick size comparisons
- [`codegraph report`](report.md) — full Markdown analysis report
