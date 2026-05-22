# How to Use `codegraph impact`

`codegraph impact` computes the **blast radius** of a change to a symbol — it walks the graph in reverse (who depends on this?) layer by layer and reports what would be affected if the symbol's contract changed.

Use it during code review to understand risk, or before refactoring a widely-used type.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Assess the blast radius of changing OrderService
codegraph impact OrderService

# Increase traversal depth for a wider view
codegraph impact OrderService --depth 5

# Get JSON for scripting
codegraph impact OrderService --json
```

---

## CLI Reference

```
codegraph impact <symbol> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbol>` | Symbol name or pattern (substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Maximum traversal depth (how many hops away to look) | `3` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |
| `--help`, `-h` | Show help | |

---

## Understanding Layers

Impact analysis works layer by layer. Layer 0 is the target symbol itself; each subsequent layer is one hop further away in the dependency graph (i.e., one more level of callers or dependents).

```
Layer 0: OrderService                              ← target
Layer 1: OrdersController, OrderSaga              ← direct dependents
Layer 2: Program, IntegrationTest_OrderFlow       ← dependents of dependents
```

A higher `--depth` value finds more indirect dependents but takes longer and may return more noise.

---

## Output

### Plain Text

```
Impact Analysis: OrderService
Target: MyApp.Services.OrderService (Type)
Total affected: 7

Layer 1 (direct dependents — 2 nodes):
  [Type]   MyApp.Api.OrdersController       src/MyApp.Api/OrdersController.cs:8
  [Type]   MyApp.Sagas.OrderSaga            src/MyApp.Sagas/OrderSaga.cs:22

Layer 2 (2 hops — 5 nodes):
  [Method] MyApp.Api.Program.MapRoutes       src/MyApp.Api/Program.cs:44
  ...
```

### JSON

```json
{
  "pattern": "OrderService",
  "target": { "id": "MyApp.Services.OrderService", "kind": "type" },
  "totalAffected": 7,
  "layers": [
    {
      "depth": 1,
      "nodes": [
        { "id": "MyApp.Api.OrdersController", "kind": "type", "filePath": "...", "startLine": 8 }
      ]
    }
  ]
}
```

---

## Common Workflows

### Pre-refactoring check

```bash
# How many things depend on this repository?
codegraph impact OrderRepository --depth 4
```

### CI blast-radius gate

```bash
# Fail if more than 20 nodes are affected
COUNT=$(codegraph impact "$CHANGED_SYMBOL" --json | jq '.totalAffected')
if [ "$COUNT" -gt 20 ]; then
  echo "High-impact change detected ($COUNT affected nodes). Request senior review."
  exit 1
fi
```

### Code review

```bash
# Quickly see what a PR's core change touches
codegraph impact PaymentGateway
```

---

## Choosing Between `impact` and `test-impact`

| Use case | Command |
|----------|---------|
| Which production code depends on this symbol? | `codegraph impact <symbol>` |
| Which tests cover this symbol (directly or indirectly)? | `codegraph test-impact <symbol>` |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Analysis complete |
| `1` | Error (symbol not found, or graph not built) |
