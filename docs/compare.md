# How to Use `codegraph compare`

`codegraph compare` performs a structural diff of two symbols. It shows what they share — interfaces, base types, and dependencies — and what is unique to each. This is useful for understanding similarities and differences between types, detecting copy-paste classes, or assessing whether two implementations are interchangeable.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Compare two service implementations
codegraph compare OrderService InvoiceService

# Increase traversal depth to capture more relationships
codegraph compare OrderService InvoiceService --depth 2
```

---

## CLI Reference

```
codegraph compare <symbolA> <symbolB> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbolA>` | First symbol pattern (name, qualified name, or wildcard) |
| `<symbolB>` | Second symbol pattern (name, qualified name, or wildcard) |

Both patterns are resolved the same way as `codegraph query`: exact match, suffix match (`OrderService` matches `MyApp.Services.OrderService`), or wildcard (`Order*`).

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth for collecting each symbol's relationships | `1` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph compare OrderService InvoiceService
codegraph compare OrderService InvoiceService --depth 2
codegraph compare "MyApp.Orders.OrderService" "MyApp.Billing.InvoiceService"
codegraph compare IOrderRepository ISalesRepository --graph-dir .codegraph/Api
```

---

## Understanding the Output

The output is structured Markdown with four sections. Empty sections are omitted.

```markdown
# Compare: MyApp.Orders.OrderService vs MyApp.Billing.InvoiceService

## Shared Interfaces
  - implements → ITransactionService

## Shared Base Types
  - inherits → BaseService

## Shared Dependencies
  - depends-on → Order
  - calls → IEventBus.PublishAsync(OrderPlacedEvent)

## Unique to MyApp.Orders.OrderService
  - depends-on → OrderRequest
  - calls → IOrderRepository.SaveAsync(Order)

## Unique to MyApp.Billing.InvoiceService
  - depends-on → InvoiceRequest
  - calls → IInvoiceRepository.SaveAsync(Invoice)
```

| Section | Description |
|---------|-------------|
| **Shared Interfaces** | Interfaces implemented by both symbols |
| **Shared Base Types** | Base classes inherited by both symbols |
| **Shared Dependencies** | Edges (calls, depends-on, etc.) that appear in both symbols' subgraphs |
| **Unique to A / B** | Edges present in one symbol's subgraph but not the other |

---

## Common Workflows

### Identifying Candidate Abstractions

When two types share many dependencies, they may benefit from a shared base class or interface:

```bash
codegraph compare OrderService InvoiceService --depth 2
# If Shared Dependencies is long, consider extracting a shared abstraction.
```

### Verifying Two Implementations Are Interchangeable

Check whether two classes that implement the same interface have equivalent dependency structures:

```bash
codegraph compare SqlOrderRepository InMemoryOrderRepository
# Shared Interfaces: IOrderRepository ✓
# If Unique sections are empty, both implementations depend on the same things.
```

### Detecting Copy-Paste Code

```bash
codegraph compare UserValidator OrderValidator
# Large Shared Dependencies section with small Unique sections suggests shared logic
# that could be extracted.
```

### Pre-Merge Impact Assessment

Before merging a branch that changes one service, compare it to a related service to understand divergence:

```bash
codegraph compare OldPaymentService NewPaymentService --depth 2
```

---

## Symbol Resolution

If a symbol is not found, the output reports it explicitly:

```markdown
# Compare: MyApp.Orders.OrderService vs (not found)
⚠ Symbol B not found
```

Use [`codegraph search`](search.md) to locate the correct symbol name if needed:

```bash
codegraph search InvoiceService
# → [Type] InvoiceService  ns=MyApp.Billing
codegraph compare OrderService "MyApp.Billing.InvoiceService"
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), scope the comparison to a specific sub-graph with `--graph-dir`:

```bash
codegraph compare OrderService InvoiceService --graph-dir .codegraph/Api
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — explore relationships for a single symbol
- [`codegraph search`](search.md) — locate symbols by name before comparing
- [`codegraph diff`](diff.md) — compare two graph *snapshots* for structural changes across the whole codebase
