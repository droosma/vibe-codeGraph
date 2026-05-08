# How to Use `codegraph compare`

`codegraph compare` places two symbols side by side and highlights what they share and what makes each unique — shared interfaces, shared base types, shared dependencies, and the relationships exclusive to each. It is designed for architecture review, duplication detection, and understanding how two related types relate to the rest of the graph.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Compare two service implementations
codegraph compare OrderService PaymentService

# Compare at a higher depth to include indirect relationships
codegraph compare OrderService PaymentService --depth 2
```

---

## CLI Reference

```
codegraph compare <symbolA> <symbolB> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbolA>` | First symbol to compare (exact name or partial match) |
| `<symbolB>` | Second symbol to compare (exact name or partial match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth for collecting relationships | `1` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph compare OrderService PaymentService
codegraph compare OrderService PaymentService --depth 2
codegraph compare IOrderRepository ISqlRepository --depth 1
codegraph compare OrderController PaymentController --graph-dir .codegraph/Api
```

---

## Understanding the Output

The output is structured Markdown with four sections:

```markdown
# Compare: MyApp.Orders.OrderService vs MyApp.Payments.PaymentService

## Shared Interfaces
  - ITransactional
  - ILoggable

## Shared Base Types
  - ServiceBase

## Shared Dependencies
  - calls → IEventBus.PublishAsync
  - depends-on → AppSettings

## Unique to MyApp.Orders.OrderService
  - calls → IOrderRepository.SaveAsync
  - depends-on → Order

## Unique to MyApp.Payments.PaymentService
  - calls → IPaymentGateway.ChargeAsync
  - depends-on → PaymentRequest
```

**Shared Interfaces** — interfaces that both symbols implement. Use this to understand polymorphic equivalence.

**Shared Base Types** — common base classes. Indicates potential for consolidation.

**Shared Dependencies** — edges (calls, depends-on, references, etc.) that appear in both symbols' subgraphs.

**Unique to A / Unique to B** — edges that appear in only one symbol's subgraph.

---

## Common Workflows

### Detecting Duplicate Logic

```bash
# Two service classes that look similar — are they duplicated?
codegraph compare UserService AccountService --depth 2
# Scan the "Shared Dependencies" section for significant overlap.
# Scan the "Unique to" sections for meaningful differences.
```

### Architecture Review

```bash
# Before extracting a shared abstraction, check what these two share
codegraph compare SqlOrderRepository MongoOrderRepository
```

### Candidate for Interface Extraction

```bash
# Both classes implement the same methods — good candidate for a shared interface
codegraph compare EmailNotifier SmsNotifier --depth 1
# Shared Dependencies will show the overlap; unique sections show differences.
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — query a single symbol's relationships in depth
- [`codegraph search`](search.md) — find symbols by name before comparing
- [`codegraph list`](list.md) — browse all types to discover candidates for comparison
