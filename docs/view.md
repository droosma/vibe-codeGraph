# How to Use `codegraph view`

`codegraph view` generates a self-contained, interactive 3D graph visualization of your C# codebase as an HTML file and opens it in your default browser. It is designed for visual exploration — understanding module boundaries, spotting highly-connected types, and navigating large codebases without reading JSON files.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Open the interactive graph in your browser
codegraph view
```

The HTML file is written to a temp directory and opened automatically. No web server is required — it is fully self-contained.

---

## CLI Reference

```
codegraph view [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--output <path>` | Save the HTML file to a specific path instead of a temp file | (temp file) |
| `--max-nodes <n>` | Maximum number of nodes to render (see [Node Sampling](#node-sampling)) | `5000` |
| `--no-open` | Generate the HTML file but do not open it in the browser | `false` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Open graph in browser (default)
codegraph view

# Save to a specific file
codegraph view --output graph.html

# Limit nodes for performance on large codebases
codegraph view --max-nodes 2000

# View a specific sub-graph (multi-solution setup)
codegraph view --graph-dir .codegraph/Api

# Generate without opening (e.g. in CI)
codegraph view --output report.html --no-open
```

---

## The Visualization

The graph renders using [3d-force-graph](https://github.com/vasturiano/3d-force-graph), a WebGL-based force-directed layout. Each node is a C# symbol; each link is a relationship.

### Node colours

| Kind | Colour |
|------|--------|
| Namespace | Purple |
| Type | Blue |
| Method | Green |
| Property | Orange |
| Field | Red |
| Event | Teal |
| Constructor | Amber |

### Edge colours

| Type | Colour | Meaning |
|------|--------|---------|
| `Contains` | Dark grey | Parent contains child (namespace → type, type → member) |
| `Calls` | Green | Method calls another method |
| `Inherits` | Blue | Class inherits from base class |
| `Implements` | Purple | Class implements interface |
| `DependsOn` | Red | Constructor injection or field dependency |
| `ResolvesTo` | Orange | DI registration resolved to concrete type |
| `Covers` | Teal | Test method exercises production code |
| `CoveredBy` | Dark teal | Production code covered by test |
| `References` | Light grey | General reference (field type, parameter type, etc.) |
| `Overrides` | Amber | Method overrides a virtual member |

External edges (edges crossing assembly boundaries) are rendered at reduced opacity (`0.2`) to reduce visual noise.

---

## Sidebar Controls

The left sidebar provides filtering and navigation tools:

- **Node Kinds** — click any kind to show or hide those nodes. The count shows how many nodes of that kind are in the current graph.
- **Edge Types** — click any type to show or hide those edges.
- **Show labels** — toggle floating name labels over each node. Disable on large graphs for better performance.
- **Search** — filter to nodes whose name or ID contains the search term. Press `/` to focus the search box; press `Escape` to clear it.
- **Assemblies** — click an assembly to isolate its nodes. Click again to deselect.

---

## Node Sampling

For large codebases, rendering all nodes at once can be slow. When the node count exceeds `--max-nodes`, the generator applies smart sampling:

1. All `Type` and `Namespace` nodes are always retained (they provide structural context).
2. Remaining nodes (methods, properties, fields, etc.) are ranked by their **degree** (number of connected edges). The most-connected nodes are kept until the cap is reached.
3. Edges are then filtered to only those connecting retained nodes.

Reduce `--max-nodes` if rendering is slow; increase it on powerful machines or for smaller graphs.

---

## Viewing Sub-Graphs (Multi-Solution)

In a [multi-solution setup](configuration.md#multi-solution-configuration), each solution's graph is stored in a subdirectory of `.codegraph/`. Use `--graph-dir` to visualize one solution at a time:

```bash
codegraph view --graph-dir .codegraph/Api
codegraph view --graph-dir .codegraph/Workers
```

---

## CI / Artifact Publishing

Use `--no-open` and `--output` together to generate the visualization as a CI artifact without spawning a browser:

```yaml
- name: Generate graph visualization
  run: codegraph view --output artifacts/graph.html --no-open

- uses: actions/upload-artifact@v4
  with:
    name: codegraph-visualization
    path: artifacts/graph.html
```

---

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `/` | Focus the search box |
| `Escape` | Clear search and deselect assembly filter |
