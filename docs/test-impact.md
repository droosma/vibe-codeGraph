# How to Use `codegraph test-impact`

`codegraph test-impact` analyses **test coverage** for a symbol — it shows which test methods cover it directly (by calling it or its members) and which cover it indirectly (through a chain of calls), plus which callers of the symbol have no test coverage at all.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Find tests for OrderService
codegraph test-impact OrderService

# Use a fully-qualified name
codegraph test-impact MyApp.Services.OrderService

# Increase depth to find more indirect tests
codegraph test-impact OrderService --depth 5

# Get JSON for scripting or CI
codegraph test-impact OrderService --json
```

---

## CLI Reference

```
codegraph test-impact <symbol> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbol>` | Symbol name or pattern (substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for indirect coverage | `3` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--json` | Output as JSON | Plain text |
| `--help`, `-h` | Show help | |

---

## Output

### Plain Text

```
Test Impact: OrderService
Target: MyApp.Services.OrderService (Type)

Direct Tests (2):
  ✓ OrderServiceTests.PlaceOrder_ValidInput_ReturnsOrderId
      Path: OrderService → PlaceOrder → [test]
  ✓ OrderServiceTests.CancelOrder_AlreadyCancelled_Throws

Indirect Tests (1):
  ~ CheckoutIntegrationTests.FullCheckoutFlow_Succeeds
      Path: OrderService → OrderRepository → [test covers OrderRepository]

Uncovered Callers (1):
  ✗ MyApp.Jobs.OrderCleanupJob  (depth 2)
      — no test reaches this caller

Suggested test command:
  dotnet test --filter "FullyQualifiedName~OrderServiceTests"
```

### JSON

```json
{
  "pattern": "OrderService",
  "target": { "id": "MyApp.Services.OrderService", "kind": "type" },
  "directTests": [
    {
      "testId": "MyApp.Tests.OrderServiceTests.PlaceOrder_ValidInput_ReturnsOrderId",
      "path": ["MyApp.Services.OrderService", "MyApp.Services.OrderService.PlaceOrder"]
    }
  ],
  "indirectTests": [
    {
      "testId": "MyApp.Tests.CheckoutIntegrationTests.FullCheckoutFlow_Succeeds",
      "path": ["MyApp.Services.OrderService", "MyApp.Data.OrderRepository"]
    }
  ],
  "uncoveredCallers": [
    { "callerId": "MyApp.Jobs.OrderCleanupJob", "depth": 2 }
  ],
  "suggestedTestCommand": "dotnet test --filter \"FullyQualifiedName~OrderServiceTests\""
}
```

---

## Common Workflows

### Find gaps before a refactor

```bash
# Are there uncovered callers that will be silently broken?
codegraph test-impact PaymentGateway --json | jq '.uncoveredCallers'
```

### Run only the tests that cover changed code

```bash
# In a pre-commit hook or CI step
codegraph test-impact "$CHANGED_SYMBOL" --json | \
  jq -r '[.directTests[].testId, .indirectTests[].testId] | unique[]' | \
  xargs -I{} dotnet test --filter "FullyQualifiedName~{}"
```

### Check coverage threshold in CI

```bash
UNCOVERED=$(codegraph test-impact OrderService --json | jq '.uncoveredCallers | length')
if [ "$UNCOVERED" -gt 0 ]; then
  echo "Warning: $UNCOVERED uncovered callers found."
fi
```

---

## Choosing Between `test-impact` and `impact`

| Use case | Command |
|----------|---------|
| Which tests exercise this symbol? | `codegraph test-impact <symbol>` |
| Which production code depends on this symbol? | `codegraph impact <symbol>` |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Tests found |
| `1` | Error (graph not built) |
| `2` | Symbol not found, or no tests found |
