# How to Use `codegraph brief`

`codegraph brief` generates a compact **`BRIEF.md`** codebase orientation file optimised for LLM agents. It is intended to be the first thing an agent reads when starting work on an unfamiliar codebase — a dense, structured summary of the graph that fits within a single context window.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Generate BRIEF.md and print it to stdout
codegraph brief

# Use a non-default graph directory
codegraph brief --graph-dir .codegraph/MyService
```

---

## CLI Reference

```
codegraph brief [options]
```

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing `graph.db` | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## What `BRIEF.md` Contains

The generated file covers:

- **Codebase overview** — assembly count, type count, method count, index timestamp
- **Top hub types** — the most highly-connected types (high in + out degree) that are the backbone of the system
- **Assembly boundaries** — a table of assemblies and their type/method counts
- **Key interfaces** — the interfaces with the most implementations
- **Test coverage summary** — assemblies with the highest and lowest test coverage
- **Suggested starter queries** — ready-to-run `codegraph query` commands to orient further exploration

---

## Where the File Is Written

`BRIEF.md` is written to `<graph-dir>/BRIEF.md` (e.g. `.codegraph/BRIEF.md`). The content is also printed to stdout so you can pipe or redirect it.

---

## Recommended Workflow

Add `brief` to your indexing pipeline so agents always have an up-to-date orientation file:

```bash
codegraph index --solution MyApp.sln
codegraph brief
```

Or in CI:

```yaml
- name: Update CodeGraph
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln
    codegraph brief
```

### Agent instruction

Add the following to your agent's system prompt or instruction file:

```
When starting work on this codebase, read .codegraph/BRIEF.md first.
It contains an LLM-optimised orientation to the architecture.
```

---

## Relationship to `codegraph report`

| Feature | `brief` | `report` |
|---------|---------|---------|
| Audience | LLM agents (compact, machine-friendly) | Humans (detailed Markdown) |
| Output | `.codegraph/BRIEF.md` | `.codegraph/REPORT.md` (configurable) |
| Depth | Concise summary | Full analytics with prose |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | `BRIEF.md` generated successfully |
| `1` | Error (graph database not found — run `codegraph index` first) |
