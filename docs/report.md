# How to Use `codegraph report`

`codegraph report` generates a Markdown report that summarises your indexed code graph. It covers hub types (the most-connected symbols), assembly boundaries and cross-cutting dependencies, test coverage by assembly, and suggested starter queries for your AI agent.

The report is written automatically after every `codegraph index` run (to `.codegraph/REPORT.md`), so agents always have a fresh overview of the codebase without any extra steps.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Generate (or regenerate) the report
codegraph report
```

The report is written to `.codegraph/REPORT.md` by default.

---

## CLI Reference

```
codegraph report [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--output`, `-o <path>` | Output file path | `.codegraph/REPORT.md` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Default — write to .codegraph/REPORT.md
codegraph report

# Write to a custom path (e.g., publish alongside your docs)
codegraph report -o docs/architecture/report.md

# Report on a previous snapshot for comparison
codegraph report --graph-dir .codegraph-prev -o report-prev.md
```

---

## Report Structure

The generated Markdown report contains four sections.

### Hub Types

The types with the highest total degree (incoming + outgoing edges). High-degree nodes are architectural hotspots — changes here have wide blast radius.

```markdown
## Hub Types (highest connectivity)

| Type | In | Out | Total |
|------|----|-----|-------|
| OrderService | 12 | 8 | 20 |
| IRepository | 0 | 15 | 15 |
| ...
```

### Assemblies

A table of all assemblies, with node counts, internal edge counts, and cross-boundary edge counts. High cross-boundary counts indicate tight coupling between assemblies.

```markdown
## Assemblies

| Assembly | Nodes | Internal Edges | Cross-boundary Edges |
|----------|-------|----------------|----------------------|
| MyApp.Core | 312 | 1 041 | 87 |
| MyApp.Api | 128 | 340 | 54 |
| ...
```

### Test Coverage (by assembly)

If the graph includes `Covers`/`CoveredBy` edges (emitted by the Test Coverage Pass), the report shows the percentage of types covered by at least one test method.

```markdown
## Test Coverage (by assembly)

| Assembly | Types | Covered | Coverage |
|----------|-------|---------|----------|
| MyApp.Core | 84 | 71 | 84.5% |
| MyApp.Api | 32 | 18 | 56.3% |
```

This section is omitted if no coverage edges are present (e.g., test projects were excluded from indexing).

### Suggested Queries

The report ends with a list of `codegraph query` commands pre-populated with the hub types and assemblies found, giving you a quick starting point for exploration.

```markdown
## Suggested Queries

\```
codegraph query OrderService --depth 2
\```

\```
codegraph query IRepository --kind resolves-to
\```
```

---

## Using the Report in CI

Add an artifact upload step after indexing to make the report available on every CI run:

```yaml
- name: Index and report
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln
    codegraph report -o artifacts/REPORT.md

- uses: actions/upload-artifact@v4
  with:
    name: codegraph-report
    path: artifacts/REPORT.md
```

---

## Sharing with GitHub Wiki

Combine `codegraph report` with `codegraph wiki` to publish a full architecture snapshot:

```bash
# Generate report and full wiki
codegraph report -o wiki/REPORT.md
codegraph wiki -o wiki/
```

See [docs/wiki.md](wiki.md) for details on the wiki generator.
