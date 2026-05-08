# How to Use `codegraph compare`

`codegraph compare` performs a **structural side-by-side comparison** of two symbols. It shows what they have in common (shared interfaces, base types, and dependencies) and what makes each unique — in a single, focused output.

Use it when you want to understand how two types relate, identify duplication, or decide which of two similar types to depend on.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Compare two service implementations
codegraph compare OrderService InvoiceService

# Compare with a deeper traversal depth
codegraph compare SqlOrderRepository InMemoryOrderRepository --depth 2
```

---

## CLI Reference

```
codegraph compare <symbolA> <symbolB> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<symbolA>` | First symbol — name or pattern (supports wildcards) |
| `<symbolB>` | Second symbol — name or pattern (supports wildcards) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | BFS traversal depth for gathering edges | `1` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Compare two repository implementations
codegraph compare SqlOrderRepository InMemoryOrderRepository

# Compare with deeper depth to include transitive dependencies
codegraph compare OrderService InvoiceService --depth 2

# Compare from a non-default graph directory
codegraph compare UserService AccountService --graph-dir .codegraph/Api
```

---

## Output Format

The output is rendered as Markdown sections:

```markdown
# Compare: MyApp.Services.OrderService vs MyApp.Services.InvoiceService

## Shared Interfaces
  - MyApp.Abstractions.ITransactional
  - MyApp.Abstractions.ILoggable

## Shared Base Types
  - MyApp.Services.BaseService

## Shared Dependencies
  - depends-on → MyApp.Domain.Money
  - calls → IEventBus.PublishAsync(OrderPlacedEvent)

## Unique to MyApp.Services.OrderService
  - calls → IOrderRepository.SaveAsync(Order)
  - depends-on → MyApp.Domain.Order

## Unique to MyApp.Services.InvoiceService
  - calls → IInvoiceRepository.SaveAsync(Invoice)
  - depends-on → MyApp.Domain.Invoice
```

Each section only appears when it contains data. If a symbol is not found, a warning line is printed instead:

```
⚠ Symbol A not found
```

### Section Reference

| Section | Meaning |
|---------|---------|
| **Shared Interfaces** | Interfaces implemented by both symbols |
| **Shared Base Types** | Base classes inherited by both symbols |
| **Shared Dependencies** | Outgoing edges (calls, depends-on, implements, etc.) present in both |
| **Unique to A** | Outgoing edges only present in the first symbol |
| **Unique to B** | Outgoing edges only present in the second symbol |

---

## Symbol Patterns

Both symbol arguments support the same patterns as `codegraph query`:

```bash
# Exact match (case-insensitive)
codegraph compare OrderService InvoiceService

# Wildcard — picks the first match
codegraph compare "Order*" "Invoice*"

# Fully-qualified name for precision
codegraph compare MyApp.Services.OrderService MyApp.Services.InvoiceService

# Kind prefix to avoid ambiguity
codegraph compare type:OrderService type:InvoiceService
```

If a pattern matches multiple nodes, the first match is used. Use a more specific pattern or the fully-qualified name to control which node is selected.

---

## Common Workflows

### Choosing Between Two Implementations

```bash
# Understand what's shared and what's different between two repositories
codegraph compare SqlOrderRepository InMemoryOrderRepository
# → If they share an interface, either can be swapped in as a dependency.
# → Unique edges reveal what's database-specific vs. in-memory-specific.
```

### Detecting Duplication

```bash
# Two services that grew separately — do they duplicate logic?
codegraph compare UserNotificationService OrderNotificationService
# → Large "Shared Dependencies" section suggests refactoring opportunity.
```

### Reviewing an Interface Contract

```bash
# All implementations should share the interface edges
codegraph compare CachedProductRepository DatabaseProductRepository
# → If "Shared Interfaces" is empty, one of them may have drifted from the contract.
```

### Assessing Refactoring Impact

```bash
# Before merging two similar types, understand what would change
codegraph compare LegacyPaymentProcessor ModernPaymentProcessor --depth 2
```

---

## Compare vs. Query vs. Diff

| Tool | Use when… |
|------|-----------|
| `codegraph compare` | You want to see how two *symbols* relate to each other structurally |
| `codegraph query` | You want to explore the full graph around a *single* symbol |
| `codegraph diff` | You want to see what changed in the graph between two *git snapshots* |

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), both symbols are resolved against the merged graph. To restrict resolution to a specific sub-graph:

```bash
codegraph compare OrderService InvoiceService --graph-dir .codegraph/Backend
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — explore relationships around a single symbol
- [`codegraph search`](search.md) — find symbols by name when you don't know the exact identifier
- [`codegraph diff`](diff.md) — compare two graph snapshots for structural changes over time
