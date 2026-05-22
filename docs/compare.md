# How to Use `codegraph compare`

`codegraph compare` performs a **structural side-by-side comparison** of two symbols. It highlights what they share (interfaces, callers, callees) and where they differ — useful for spotting duplication, planning consolidation, or reviewing parallel implementations.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Compare two service types
codegraph compare OrderService PaymentService

# Use fully-qualified names for precision
codegraph compare MyApp.Services.OrderService MyApp.Services.PaymentService

# Increase traversal depth
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
| `<symbolA>` | First symbol to compare (substring match) |
| `<symbolB>` | Second symbol to compare (substring match) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for neighbourhood comparison | `1` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## Output

The comparison report is structured as a Markdown document:

```markdown
# Compare: MyApp.Services.OrderService vs MyApp.Services.PaymentService

## Shared Interfaces
- ITransactional
- IService

## Shared Callers
- MyApp.Api.CheckoutController

## Shared Callees
- MyApp.Data.UnitOfWork

## Only in OrderService
- Calls: MyApp.Data.OrderRepository
- Implements: IOrderService

## Only in PaymentService
- Calls: MyApp.Payments.PaymentGateway
- Implements: IPaymentService
```

---

## Common Workflows

### Spot duplication before refactoring

```bash
# Do these two handlers share enough structure to merge?
codegraph compare CreateOrderCommandHandler UpdateOrderCommandHandler
```

### Review parallel implementations

```bash
# Compare two implementations of the same interface
codegraph compare SqlOrderRepository InMemoryOrderRepository
```

### Check before extracting a base class

```bash
# What do these two services share that could move to a base class?
codegraph compare OrderService SubscriptionService --depth 2
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Comparison complete |
| `1` | One or both symbols not found, or graph not built |
