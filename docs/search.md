# How to Use `codegraph search`

`codegraph search` finds symbols in the indexed graph by name, namespace, or file path using fast case-insensitive substring matching. It is the quickest way to locate a symbol when you know part of its name but don't yet know its full qualified ID.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Find all symbols containing "Order"
codegraph search Order

# Find only types containing "Repository"
codegraph search Repository --kind type

# Narrow results to the top 5 matches
codegraph search PlaceOrder --top 5
```

---

## CLI Reference

```
codegraph search <query> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<query>` | Case-insensitive substring to search for across symbol names, namespace names, and file paths |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum number of results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph search Order                              # All symbols containing "Order"
codegraph search Order --kind type                  # Only types
codegraph search PlaceOrder --kind method           # Only methods
codegraph search IRepository --kind type --top 10  # Top 10 matching types
codegraph search MyApp.Orders --kind namespace      # Namespace search
codegraph search OrderService --graph-dir .codegraph/Api  # Scoped to a sub-graph
```

---

## Understanding the Output

Each result is printed on a single line:

```
[Type]    OrderService      ns=MyApp.Services (src/Services/OrderService.cs)
[Method]  PlaceOrder        ns=MyApp.Services (src/Services/OrderService.cs)
[Type]    IOrderService     ns=MyApp.Services (src/Services/IOrderService.cs)

3 result(s) for 'Order'.
```

| Column | Description |
|--------|-------------|
| `[Kind]` | Node kind: `Type`, `Method`, `Namespace`, `Property`, `Field` |
| Name | Short symbol name |
| `ns=` | Containing namespace ID |
| `(path)` | Source file path, when available |

---

## Common Workflows

### Discovering a Symbol Before Querying

`search` is most useful as a first step before `query`. When you know part of a type or method name but need its exact ID:

```bash
# Step 1: Find the exact symbol
codegraph search "OrderService"
# → [Type] OrderService  ns=MyApp.Services (src/Services/OrderService.cs)

# Step 2: Use the full qualified name to query relationships
codegraph query "MyApp.Services.OrderService" --depth 2
```

### Finding All Methods on a Type

```bash
codegraph search OrderService --kind method
# → Shows all methods whose name or file path contains "OrderService"
```

### Checking If a Symbol Exists After Refactoring

```bash
codegraph search OldClassName
# → No results confirms the type was removed or renamed
```

### Exploring an Unfamiliar Namespace

```bash
codegraph search "MyApp.Payments" --kind namespace
# → Confirms namespace exists and shows its full ID

codegraph search "MyApp.Payments" --kind type
# → All types in that namespace subtree
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), use `--graph-dir` to scope the search to a specific solution's sub-graph:

```bash
codegraph search PaymentService --graph-dir .codegraph/Api
codegraph search IWorker --graph-dir .codegraph/Workers
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — traverse relationships once you have a symbol ID
- [`codegraph compare`](compare.md) — compare two symbols structurally
- [`codegraph list`](list.md) — browse assemblies, types, interfaces, and namespaces
