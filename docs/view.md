# How to Use `codegraph view`

`codegraph view` generates an interactive 3D graph visualization of your CodeGraph output as a self-contained HTML file and opens it in your default browser.

---

## Quick Start

```bash
# Index first (if you haven't already)
codegraph index --solution MyApp.sln

# Open the interactive visualization
codegraph view
```

The command reads `.codegraph/`, generates a self-contained HTML file, and opens it in your default browser.

---

## CLI Reference

```
codegraph view [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory to visualize | `.codegraph` |
| `--output <path>` | Write HTML to a specific file instead of a temp file | (temp file) |
| `--max-nodes <n>` | Maximum nodes to render | `5000` |
| `--no-open` | Generate HTML but do not open it in the browser | `false` |
| `--help`, `-h` | Show this help | |

---

## Examples

```bash
# Open graph in browser (default)
codegraph view

# Save to a named file (useful for sharing or CI artifacts)
codegraph view --output graph.html

# Cap nodes for performance on large codebases
codegraph view --max-nodes 2000

# Visualize a specific sub-graph (multi-solution or federated graphs)
codegraph view --graph-dir .codegraph/Api

# Generate without opening the browser
codegraph view --output graph.html --no-open
```

---

## Visualization Features

The generated HTML is fully self-contained (no server required, works offline). It embeds a 3D force-directed graph powered by [`3d-force-graph`](https://github.com/vasturiano/3d-force-graph) and Three.js via CDN.

### Nodes and Edges

- **Nodes** are colored and sized by `NodeKind` (Type nodes are larger; Namespace nodes are medium; Method/Property/Field nodes are smaller)
- **Edges** are colored by `EdgeType` and rendered with directional arrows
- **External edges** (to assemblies outside your solution) are visually dimmed

### Interactive Controls

| Control | Action |
|---------|--------|
| Click + drag | Rotate the graph |
| Scroll / pinch | Zoom in/out |
| Click a node | Show node details |
| `/` | Focus the search box |
| `Escape` | Reset all filters |

### Sidebar Panels

- **Node kind filter** — toggleable checkboxes for each `NodeKind`, with live counts
- **Edge type filter** — toggleable checkboxes for each `EdgeType`, with live counts
- **Assembly list** — click any assembly to isolate its subgraph
- **Search** — client-side filter by node name or ID; matching nodes are highlighted

### Label Toggle

Press the **Labels** button in the toolbar to show or hide node labels. Labels use sprite text for legibility at any zoom level.

---

## Smart Sampling

When a graph exceeds `--max-nodes`, the visualizer applies smart sampling:

1. **All `Type` and `Namespace` nodes are always retained** — they form the structural backbone of the graph
2. **Remaining slots are filled by connectivity** — nodes with the most edges are kept first
3. **Edges are filtered** to only include connections between retained nodes

This preserves the high-level architecture while keeping the browser responsive.

---

## Saving and Sharing

The `--output` flag writes the HTML to a named file:

```bash
codegraph view --output docs/architecture-graph.html --no-open
```

The file is entirely self-contained — it can be committed to a repository, attached to a PR, or served as a GitHub Pages artifact.

---

## Performance Tips

| Codebase size | Recommended `--max-nodes` |
|---------------|--------------------------|
| < 500 nodes | (no cap needed) |
| 500 – 2 000 | `2000` |
| 2 000 – 10 000 | `1000` – `2000` |
| > 10 000 | `500` – `1000`, or use `--graph-dir` to focus on a sub-graph |

For very large codebases, combine `--graph-dir` with a solution-level sub-directory (e.g., `.codegraph/Api`) to visualize only one service at a time.

---

## Related Commands

- [`codegraph index`](../README.md#codegraph-index) — build the graph before visualizing
- [`codegraph query`](../README.md#codegraph-query) — structural queries (text output)
- [`codegraph diff`](diff.md) — compare two graph snapshots
