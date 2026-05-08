# How to Use `codegraph list`

`codegraph list` lets you browse the code graph hierarchy without writing a query. It is the fastest way to orient yourself in an unfamiliar codebase — enumerate all assemblies, find the most-connected types, or discover which interfaces have the most implementations.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# List all assemblies with type and method counts
codegraph list

# List the most-connected types across all assemblies
codegraph list types

# List interfaces ranked by implementation count
codegraph list interfaces
```

---

## CLI Reference

```
codegraph list [scope] [options]
```

### Scopes

| Scope | Description |
|-------|-------------|
| `assemblies` | All assemblies with type/method counts **(default)** |
| `types` | Types ranked by connectivity (sum of in-degree + out-degree) |
| `interfaces` | Interfaces ranked by implementation count |
| `namespaces` | Namespaces with type and method counts |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--assembly <name>` | Filter by assembly name (applies to `types`, `interfaces`, `namespaces`) | All assemblies |
| `--top <n>` | Maximum items to return (applies to `types` scope only) | `50` |
| `--skip <n>` | Skip the first N results for pagination (applies to `types` scope only) | `0` |
| `--filter <pattern>` | Filter results by name substring, case-insensitive (applies to `types` scope only) | All |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph list                                   # All assemblies (default)
codegraph list assemblies                        # Explicit scope

codegraph list types                             # Top 50 types by connectivity
codegraph list types --top 20                    # Top 20 types
codegraph list types --assembly MyApp.Core       # Types in a specific assembly
codegraph list types --filter Order              # Types whose name contains "Order"
codegraph list types --top 20 --skip 20          # Page 2 (results 21-40)

codegraph list interfaces                        # All interfaces
codegraph list interfaces --assembly MyApp.Api   # Interfaces in one assembly

codegraph list namespaces                        # All namespaces with counts
codegraph list namespaces --assembly MyApp.Core  # Namespaces in one assembly

codegraph list --graph-dir .codegraph-prev       # List from an older snapshot
```

---

## Understanding the Output

### `assemblies`

```
MyApp.Core            42 types  |  198 methods
MyApp.Api             17 types  |   83 methods
MyApp.Infrastructure  31 types  |  152 methods
```

Use this to understand the size and shape of each project before drilling deeper.

### `types`

```
MyApp.Core.OrderService          degree=47  (in=12, out=35)
MyApp.Infrastructure.DbContext   degree=38  (in=29, out=9)
...
```

High-degree types are **hubs** — they are worth understanding first when exploring the graph, as they tend to be the most impactful to change.

### `interfaces`

```
IOrderService     3 implementations
IRepository<T>    5 implementations
IEventBus         1 implementation
```

This is useful for understanding how polymorphism is used in the codebase.

### `namespaces`

```
MyApp.Core.Orders       8 types  |  34 methods
MyApp.Core.Payments     5 types  |  19 methods
MyApp.Infrastructure    31 types | 152 methods
```

---

## Common Workflows

### Orientation in a New Codebase

```bash
# Step 1: See what assemblies exist
codegraph list

# Step 2: Find the highest-traffic types in the most important assembly
codegraph list types --assembly MyApp.Core --top 10

# Step 3: Dive into a specific type
codegraph query OrderService --depth 2 --format context
```

### Finding Widely-Implemented Interfaces

```bash
codegraph list interfaces
# → IRepository<T>  5 implementations — good candidate for a query
codegraph query "IRepository" --kind implements
```

### Comparing Two Snapshots

```bash
# Check what assemblies existed in an older snapshot
codegraph list --graph-dir .codegraph-prev

# Compare with current
codegraph list
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), use `--graph-dir` to list symbols from a specific solution's sub-graph:

```bash
codegraph list --graph-dir .codegraph/Api
codegraph list types --graph-dir .codegraph/Workers
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — query for specific symbols and their relationships
- [`codegraph stats`](stats.md) — print numeric statistics (node/edge counts)
- [`codegraph report`](report.md) — full Markdown analysis report
