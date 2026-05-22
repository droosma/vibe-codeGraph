# How to Use `codegraph search`

`codegraph search` finds nodes in the graph by name, namespace, or file path using fast case-insensitive substring matching. Use it when you know part of a symbol's name but not its exact identifier, or when `codegraph query` pattern matching returns too many results.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Search for anything containing "Order"
codegraph search Order

# Narrow to types only
codegraph search Order --kind type

# Return up to 50 results
codegraph search Repository --top 50
```

---

## CLI Reference

```
codegraph search <query> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<query>` | Text to search for (case-insensitive substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum number of results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output results as JSON | Plain text |
| `--help`, `-h` | Show help | |

---

## What Gets Searched

The search engine matches against:

- **Type names** — classes, interfaces, enums, structs, records
- **Method names** — includes constructors and operators
- **Property names**
- **Field names**
- **Namespace names**
- **File paths** — partial file path matches

Results are deduped and ranked by relevance.

---

## Examples

```bash
# Find all symbols containing "Service"
codegraph search Service

# Find types named "Handler"
codegraph search Handler --kind type

# Find methods mentioning "Async"
codegraph search Async --kind method

# Find namespaces containing "Infrastructure"
codegraph search Infrastructure --kind namespace

# Get up to 100 results for a broad search
codegraph search Repository --top 100

# JSON output for scripting
codegraph search Order --json

# Use a different graph directory
codegraph search Order --graph-dir .codegraph/MyService
```

---

## JSON Output

With `--json`, each result is an object:

```json
[
  {
    "id": "MyApp.Services.OrderService",
    "name": "OrderService",
    "kind": "type",
    "namespace": "MyApp.Services",
    "filePath": "src/MyApp.Services/OrderService.cs",
    "startLine": 12
  }
]
```

---

## Choosing Between `search` and `query`

| Use case | Command |
|----------|---------|
| You know part of a name | `codegraph search <partial-name>` |
| You want callers/callees of a specific symbol | `codegraph query <symbol> --kind calls-to` |
| You want to traverse the graph from a symbol | `codegraph query <symbol> --depth 2` |
| You want detailed information about one symbol | `codegraph explain <symbol>` |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Results found |
| `1` | Error (graph not found, run `codegraph index` first) |
| `2` | No results found (non-JSON mode) |
