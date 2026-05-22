# How to Use `codegraph explain`

`codegraph explain` gives you a full deep-dive into a single symbol: its signature, doc comment, members, all incoming and outgoing edges, and linked test coverage. It is the fastest way to understand what a specific type or method does and how it fits into the codebase.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Explain a type
codegraph explain OrderService

# Explain using a fully-qualified name
codegraph explain MyApp.Services.OrderService

# Get the result as JSON
codegraph explain OrderService --json
```

---

## CLI Reference

```
codegraph explain <symbol> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbol>` | Symbol name or pattern (substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |
| `--help`, `-h` | Show help | |

---

## Output

### Plain Text

```
## MyApp.Services.OrderService (Type)

Signature:   public class OrderService : IOrderService
File:        src/MyApp.Services/OrderService.cs:12
Doc:         Handles order creation, fulfilment, and cancellation.

### Members
  [Method] PlaceOrder
  [Method] CancelOrder
  [Property] ActiveOrders

### Outgoing Relationships
  Calls       → MyApp.Data.OrderRepository.SaveAsync
  Implements  → MyApp.Services.IOrderService
  References  → MyApp.Models.Order

### Incoming Relationships
  CalledBy    ← MyApp.Api.OrdersController.Post
  CoveredBy   ← MyApp.Tests.OrderServiceTests.PlaceOrder_ValidInput_ReturnsOrderId
```

### JSON

```json
{
  "id": "MyApp.Services.OrderService",
  "name": "OrderService",
  "kind": "type",
  "filePath": "src/MyApp.Services/OrderService.cs",
  "startLine": 12,
  "endLine": 87,
  "signature": "public class OrderService : IOrderService",
  "docComment": "Handles order creation, fulfilment, and cancellation.",
  "members": [
    { "id": "MyApp.Services.OrderService.PlaceOrder", "name": "PlaceOrder", "kind": "method" }
  ],
  "outgoingEdges": [
    { "type": "calls", "toId": "MyApp.Data.OrderRepository.SaveAsync" }
  ],
  "incomingEdges": [
    { "type": "calledby", "fromId": "MyApp.Api.OrdersController.Post" }
  ],
  "tests": [
    { "id": "MyApp.Tests.OrderServiceTests.PlaceOrder_ValidInput_ReturnsOrderId", "name": "PlaceOrder_ValidInput_ReturnsOrderId" }
  ]
}
```

---

## Common Workflows

### Understand an unfamiliar type

```bash
codegraph explain PaymentGateway
```

### Find test coverage for a symbol

```bash
codegraph explain OrderService --json | jq '.tests'
```

### Check what a type depends on

```bash
codegraph explain OrderService --json | jq '.outgoingEdges'
```

### Find callers of a method

```bash
codegraph explain OrderService.PlaceOrder --json | jq '.incomingEdges'
```

---

## Choosing Between `explain`, `query`, and `impact`

| Use case | Command |
|----------|---------|
| Full deep-dive on one symbol | `codegraph explain <symbol>` |
| Traverse the graph from a symbol | `codegraph query <symbol> --depth 2` |
| Assess the blast radius of a change | `codegraph impact <symbol>` |
| Find which tests cover a symbol | `codegraph test-impact <symbol>` |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Symbol found and explained |
| `1` | Error (symbol not found, or graph not built) |
