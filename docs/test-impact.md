# How to Use `codegraph test-impact`

`codegraph test-impact` analyzes the test coverage graph for a symbol, showing which tests directly or indirectly exercise it and which callers remain uncovered. It is designed for understanding risk before making changes and for finding coverage gaps without running the test suite.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# See which tests cover OrderService
codegraph test-impact OrderService

# Include indirect coverage through deeper call chains (depth 5)
codegraph test-impact OrderService --depth 5
```

---

## CLI Reference

```
codegraph test-impact <symbol> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbol>` | Symbol to analyze (partial name or exact match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for indirect coverage (how many hops from the target to reach tests) | `3` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph test-impact OrderService
codegraph test-impact OrderService --depth 5
codegraph test-impact PlaceOrder --depth 2
codegraph test-impact OrderService --graph-dir .codegraph/Api
```

---

## Understanding the Output

```
# Test impact for MyApp.Orders.OrderService

## Direct coverage (3 tests)
  ✓ OrderServiceTests.PlaceOrder_ShouldPublishEvent
  ✓ OrderServiceTests.PlaceOrder_WhenInvalid_ShouldReturnError
  ✓ OrderServiceTests.CancelOrder_ShouldUpdateStatus

## Indirect coverage (2 tests)
  ⚠ IntegrationTests.OrderFlow_EndToEnd
    via: OrderController.Post → OrderService
  ⚠ IntegrationTests.CheckoutPipeline_HappyPath
    via: CheckoutService.Checkout → OrderService

## Uncovered callers (1 path)
  ✗ ReportingService.GenerateMonthlyReport [depth: 2]

## Suggested test command
  dotnet test --filter "OrderServiceTests"
```

### Sections

**Direct coverage** — test methods that call the target symbol directly (via a `Covers` edge in the graph). These are the tests most likely to catch regressions.

**Indirect coverage** — test methods that reach the target through intermediate call hops, up to `--depth` hops away. The `via:` path shows the call chain. These tests may catch regressions but with lower signal-to-noise.

**Uncovered callers** — callers of the target that are not reached by any test. These represent coverage gaps — production code paths with no test exercise.

**Suggested test command** — a `dotnet test --filter` command derived from the direct coverage tests.

---

## Common Workflows

### Pre-Change Risk Assessment

```bash
# About to refactor OrderService — which tests will catch regressions?
codegraph test-impact OrderService
# Check DirectCoverage count. If low or zero, add tests before refactoring.
```

### Finding Coverage Gaps

```bash
# Find callers with no test coverage
codegraph test-impact PaymentProcessor --depth 3
# Look at "Uncovered callers" — these are the riskiest call sites.
```

### CI Test Selection

```bash
# Identify tests to run for a changed symbol (faster than running everything)
codegraph test-impact ChangedService --depth 2
# Use the suggested test command in CI.
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — query a symbol's full relationship graph (including `--kind covers`)
- [`codegraph report`](report.md) — full coverage analysis across all assemblies
- [`codegraph diff`](diff.md) — detect structural changes between commits
