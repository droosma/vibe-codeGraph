# How to Use `codegraph search`

`codegraph search` finds symbols by name, namespace, or file path using case-insensitive substring matching. It is the fastest way to locate a specific type, method, or namespace when you know part of the name but not the fully-qualified identifier needed for `codegraph query`.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Find all symbols containing "Order"
codegraph search Order

# Find only types matching "Repository"
codegraph search Repository --kind type

# Limit to top 5 results
codegraph search "PaymentGateway" --top 5
```

---

## CLI Reference

```
codegraph search <query> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<query>` | Substring to search for (case-insensitive) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum number of results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph search Order                       # All symbols with "Order" in name
codegraph search Order --kind type           # Only types
codegraph search Order --kind method         # Only methods
codegraph search Service --top 10            # Top 10 results
codegraph search IRepository --kind type     # Find interfaces/types named IRepository*
codegraph search "Startup" --graph-dir .codegraph/Api  # Scope to a sub-graph
```

---

## Understanding the Output

Each matching symbol is printed on one line:

```
[Type]   MyApp.Orders.OrderService  ns=MyApp.Orders (src/Orders/OrderService.cs)
[Method] MyApp.Orders.OrderService.PlaceOrder  ns=MyApp.Orders (src/Orders/OrderService.cs)
[Type]   MyApp.Payments.OrderPaymentService  ns=MyApp.Payments (src/Payments/OrderPaymentService.cs)

3 result(s) for 'Order'.
```

Each line shows:
- **Kind** — `Type`, `Method`, `Namespace`, `Property`, or `Field`
- **Fully-qualified name** — the identifier to use in `codegraph query`
- **Namespace** — the containing namespace
- **File path** — source file location (when available)

---

## Common Workflows

### Finding a Symbol Before Querying

```bash
# Step 1: Locate the right symbol
codegraph search "OrderService"
# → [Type] MyApp.Orders.OrderService

# Step 2: Query its relationships in detail
codegraph query "MyApp.Orders.OrderService" --depth 2 --format context
```

### Discovering All Repositories

```bash
codegraph search Repository --kind type
# Lists every type with "Repository" in its name — useful for understanding the data layer.
```

### Locating a Method by Name

```bash
codegraph search PlaceOrder --kind method
# Returns all methods named PlaceOrder (or containing "PlaceOrder"), across all assemblies.
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — deep traversal query using a symbol identifier
- [`codegraph list`](list.md) — browse the full list of types, interfaces, and namespaces
- [`codegraph compare`](compare.md) — compare two symbols side by side
