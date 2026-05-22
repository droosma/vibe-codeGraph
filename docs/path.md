# How to Use `codegraph path`

`codegraph path` finds the **shortest dependency path** between two symbols in the graph. Use it to answer questions like "how does `PaymentGateway` end up depending on `Logger`?" or "is there any route from module A to module B?"

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Find the path from OrdersController to the database
codegraph path OrdersController OrderRepository

# Use fully-qualified names for precision
codegraph path MyApp.Api.OrdersController MyApp.Data.OrderRepository

# Get JSON for scripting
codegraph path OrdersController OrderRepository --json
```

---

## CLI Reference

```
codegraph path <from> <to> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<from>` | Starting symbol (substring match) |
| `<to>` | Target symbol (substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--max-depth <n>` | Maximum number of hops to search | `10` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |
| `--help`, `-h` | Show help | |

---

## Output

### Plain Text

```
Path: MyApp.Api.OrdersController → MyApp.Data.OrderRepository

  [1] MyApp.Api.OrdersController
       ──calls──▶
  [2] MyApp.Services.OrderService
       ──calls──▶
  [3] MyApp.Data.OrderRepository

3 hops.
```

### JSON

```json
{
  "from": "MyApp.Api.OrdersController",
  "to": "MyApp.Data.OrderRepository",
  "found": true,
  "steps": [
    {
      "fromId": "MyApp.Api.OrdersController",
      "toId": "MyApp.Services.OrderService",
      "edgeType": "calls",
      "toKind": "type",
      "toFilePath": "src/MyApp.Services/OrderService.cs"
    },
    {
      "fromId": "MyApp.Services.OrderService",
      "toId": "MyApp.Data.OrderRepository",
      "edgeType": "calls",
      "toKind": "type",
      "toFilePath": "src/MyApp.Data/OrderRepository.cs"
    }
  ]
}
```

When no path exists, the output is:

```json
{ "from": "A", "to": "B", "found": false, "steps": [] }
```

---

## Common Workflows

### Trace an unexpected dependency

```bash
# Why does the UI layer depend on the database layer?
codegraph path MyApp.Ui MyApp.Data
```

### Verify layered architecture

```bash
# Confirm there is no path from the domain layer to infrastructure
codegraph path MyApp.Domain MyApp.Infrastructure --json | jq '.found'
# Expect: false
```

### Find the coupling chain in CI

```bash
RESULT=$(codegraph path "$MODULE_A" "$MODULE_B" --json)
if echo "$RESULT" | jq -e '.found' > /dev/null; then
  echo "Unexpected coupling detected:"
  echo "$RESULT" | jq '.steps[].toId'
fi
```

---

## MCP Equivalent

When using CodeGraph via MCP, call the `codegraph_path` tool with `from` and `to` parameters — the same traversal logic is used.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Path found |
| `1` | No path found, or error (graph not built) |
