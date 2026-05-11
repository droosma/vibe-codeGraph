# How to Use `codegraph brief`

`codegraph brief` generates a compact `BRIEF.md` orientation file — a single Markdown document designed to be the **first thing an LLM agent reads** when opening your codebase. It packs a meaningful structural overview into ~500–1 000 tokens: assemblies, hub types, key interfaces, domain clusters, entry points, test coverage summary, and suggested starter queries.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Generate BRIEF.md
codegraph brief
# → Writes .codegraph/BRIEF.md and prints to stdout
```

---

## CLI Reference

```
codegraph brief [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--help`, `-h` | Show help | |

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success — `BRIEF.md` written |
| `1` | Error — graph database not found or I/O failure |

### Examples

```bash
# Default — write to .codegraph/BRIEF.md
codegraph brief

# Use a non-default graph directory
codegraph brief --graph-dir .codegraph/Api

# Pipe the brief into another command (outputs only the Markdown)
codegraph brief | head -40
```

---

## Output Structure

`BRIEF.md` contains the following sections.

### Solution Header

```markdown
# MyApp — 5 projects, 842 nodes, 3 104 edges
```

A single line giving the project count, total node count, and total edge count.

### Assemblies

```markdown
## Assemblies

| Assembly | Types | Methods |
|----------|------:|--------:|
| MyApp.Core | 84 | 320 |
| MyApp.Api | 32 | 118 |
| MyApp.Tests | 24 | 87 |
```

Lists every assembly with its type and method counts, sorted by size descending.

### Hub Types

```markdown
## Hub Types

| Type | In | Out | Total |
|------|---:|----:|------:|
| OrderService | 12 | 8 | 20 |
| IRepository | 0 | 15 | 15 |
```

The ten most-connected types by total edge degree. Hub types are architectural hotspots where changes have wide blast radius — useful entry points for exploration.

### Key Interfaces

```markdown
## Key Interfaces

| Interface | Implementations |
|-----------|----------------:|
| IOrderService | 3 |
| IRepository | 7 |
```

The ten interfaces with the most implementations, giving a quick view of where abstraction boundaries exist.

### Domain Clusters

```markdown
## Domain Clusters

| Domain | Types | Key Types |
|--------|------:|-----------|
| Orders | 28 | OrderService, Order, OrderRepository |
| Users | 19 | UserService, User |
```

Namespace-based clusters detected automatically from the graph. Omitted when fewer than two clusters are detected.

### Entry Points

```markdown
## Entry Points

- Controller: OrderController (MyApp.Api)
- Program: MyApp.Api
- HostedService: BackgroundEmailSender (MyApp.Api)
```

Controllers, `Program` classes, and hosted services — the natural starting points for request tracing.

### Test Coverage

```markdown
## Test Coverage

| Assembly | Types | Covered | Coverage |
|----------|------:|--------:|---------:|
| MyApp.Core | 84 | 71 | 84.5% |
| MyApp.Api | 32 | 18 | 56.3% |
```

Per-assembly coverage based on `Covers`/`CoveredBy` edges. Omitted when no test edges are present.

### Suggested Queries

```markdown
## Suggested Queries

- `codegraph query OrderService --depth 2 --mode focused`
- `codegraph query OrderService --depth 1 --kind calls`
- `codegraph list interfaces`
- `codegraph list types --assembly MyApp.Core`
- `codegraph stats`
```

Pre-built queries derived from hub types and assemblies — ready to paste and run.

---

## How `brief` Differs from `report`

| | `codegraph brief` | `codegraph report` |
|--|-------------------|--------------------|
| Output file | `.codegraph/BRIEF.md` | `.codegraph/REPORT.md` |
| Target audience | LLM agents (first read) | Humans and agents (architecture review) |
| Size | ~500–1 000 tokens | ~2 000–5 000 tokens |
| Sections | Assemblies, hubs, interfaces, clusters, entry points, coverage, queries | Hubs, assemblies + cross-boundary edges, coverage, queries |
| Auto-generated on index | No | Yes |

Use `brief` as the lightweight orientation step; use `report` for deeper architectural review.

---

## When Agents Should Read `BRIEF.md`

Agents that follow the [agent-setup guide](agent-setup.md) are instructed to read `.codegraph/BRIEF.md` as their **first action** before querying or editing code. This gives them:

- Awareness of all assemblies and their relative sizes
- The hotspot types to query first
- The domain vocabulary used in the codebase
- Suggested starter queries to run

You can regenerate it at any time:

```bash
codegraph brief
```

Or add it to your CI pipeline alongside `codegraph index`.

---

## CI Integration

```yaml
- name: Update graph and brief
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln
    codegraph brief
```

Committing `.codegraph/BRIEF.md` to your repository means agents always have an up-to-date orientation file, even without running any tools.
