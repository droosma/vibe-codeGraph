# How to Use `codegraph test-impact`

`codegraph test-impact` analyzes test coverage for a symbol and reports:

- **Direct tests** — test methods with a `CoveredBy` edge to the target
- **Indirect tests** — tests that reach the target through call chains (BFS traversal)
- **Uncovered callers** — callers with no test coverage at any depth
- **`dotnet test` filter** — a ready-to-run filter expression for the covering tests

Use it before changing a method to know exactly which tests to run, or after a change to assess coverage gaps.

---

## Quick Start

```bash
# Index the codebase first
codegraph index --solution MyApp.sln

# Show test coverage for a symbol
codegraph test-impact OrderService.PlaceOrder

# Increase traversal depth for broader indirect coverage
codegraph test-impact OrderService --depth 5
```

---

## CLI Reference

```
codegraph test-impact <symbol> [--depth N] [--graph-dir <dir>]
```

| Flag | Description | Default |
|------|-------------|---------|
| `<symbol>` | Symbol name or pattern (exact or suffix match) | *(required)* |
| `--depth <n>` | Backward traversal depth for indirect coverage | `3` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

**Symbol matching** uses exact name match, or suffix match (e.g., `PlaceOrder` matches `MyApp.Services.OrderService.PlaceOrder`).

---

## Example Output

```
Test Impact: OrderService.PlaceOrder
============================================================
Target: MyApp.Services.OrderService.PlaceOrder
File:   src/Orders/OrderService.cs [28-52]

Direct Tests (CoveredBy edges)
  ✓ OrderServiceTests.PlaceOrder_ValidRequest_ReturnsOrder
  ✓ OrderServiceTests.PlaceOrder_OutOfStock_ThrowsException

Indirect Tests (via call chains, depth ≤ 3)
  ~ IntegrationTests.Api_PostOrder_Returns201
      via: OrderController.Post → OrderService.PlaceOrder

Uncovered Callers
  ✗ AdminOrderService.ResubmitOrder (no test found within depth 3)

dotnet test --filter
  "FullyQualifiedName~OrderServiceTests.PlaceOrder_ValidRequest_ReturnsOrder|FullyQualifiedName~OrderServiceTests.PlaceOrder_OutOfStock_ThrowsException|FullyQualifiedName~IntegrationTests.Api_PostOrder_Returns201"
```

---

## Common Workflows

### Before changing a method

```bash
# Find tests to run after your change
codegraph test-impact MyService.MyMethod

# Copy the dotnet test filter line and run it
dotnet test --filter "FullyQualifiedName~..."
```

### Checking coverage gaps

```bash
# Find uncovered callers — these are risk areas
codegraph test-impact PaymentService --depth 4
```

### In CI

```bash
# Fail if the target symbol has zero test coverage
result=$(codegraph test-impact OrderService.PlaceOrder)
if echo "$result" | grep -q "Direct Tests (0)"; then
  echo "No direct test coverage for OrderService.PlaceOrder" >&2
  exit 1
fi
```

---

## How It Works

1. **Resolves the target symbol** — matches by exact ID or name suffix.
2. **Finds direct tests** — follows `CoveredBy` edges from the target outward.
3. **BFS backward through `Calls` edges** — at each depth level, checks for `CoveredBy` edges on every caller. Tests found this way are *indirect*.
4. **Reports uncovered callers** — callers at any depth that have no covering test.
5. **Generates a `dotnet test --filter`** — combines all direct and indirect test method names.

Coverage links (`Covers`/`CoveredBy` edges) are written by the `TestCoveragePass` during `codegraph index`. Symbols in projects not matching the test project heuristics will not have coverage edges.

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — general graph queries including coverage edges (`covered-by`, `covers`)
- [`codegraph diff`](diff.md) — detect structural changes that may affect test coverage
- [Graph Schema Reference](graph-schema.md) — `CoveredBy` and `Covers` edge types
