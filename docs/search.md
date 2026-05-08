# How to Use `codegraph search`

`codegraph search` finds symbols in the code graph by name, namespace, or file path using fast case-insensitive substring matching. It is the broadest discovery tool — use it when you know *something* about a symbol but not its exact fully-qualified name.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Find anything related to "payment"
codegraph search payment

# Find types whose names contain "order"
codegraph search order --kind type

# Find methods related to a file path
codegraph search Controllers/OrderController
```

---

## CLI Reference

```
codegraph search <query> [options]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<query>` | Substring to search for — matched case-insensitively against node names, IDs, file paths, and namespace names |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum number of results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Broad discovery — find anything related to "order"
codegraph search order

# Only types (classes, interfaces, records, enums)
codegraph search order --kind type

# Only methods
codegraph search PlaceOrder --kind method

# Find by file path fragment
codegraph search Controllers/Order

# Return more results
codegraph search payment --top 50

# Search a non-default graph directory
codegraph search service --graph-dir .codegraph/Api
```

---

## How Search Works

Search queries are matched as **case-insensitive substrings** against four fields of every graph node:

| Field | Example value |
|-------|---------------|
| `Name` | `OrderService` |
| `Id` | `MyApp.Services.OrderService` |
| `FilePath` | `src/Orders/OrderService.cs` |
| `ContainingNamespaceId` | `MyApp.Services` |

A node is included in results if the query matches **any** of those fields.

Results are ranked: **types first**, then by **shorter name** (more focused symbols rank above broad containers).

---

## Example Output

```
[Type] OrderService  ns=MyApp.Services (src/Orders/OrderService.cs)
[Type] OrderServiceTests  ns=MyApp.Tests.Services (tests/Orders/OrderServiceTests.cs)
[Method] PlaceOrder  ns=MyApp.Services (src/Orders/OrderService.cs)
[Method] CancelOrder  ns=MyApp.Services (src/Orders/OrderService.cs)
[Property] OrderId  ns=MyApp.Domain (src/Orders/Order.cs)

5 result(s) for 'order'.
```

Each result line shows:
- **Node kind** in brackets
- **Short name** of the symbol
- **Namespace** (`ns=`)
- **File path** in parentheses (when available)

---

## Search vs. Query vs. List

| Tool | Use when… |
|------|-----------|
| `codegraph search` | You want broad discovery — "what's related to payments?" — and don't know exact names |
| `codegraph query` | You know the symbol and want its dependency graph, callers, or implementations |
| `codegraph list` | You want a tabular overview of assemblies, types, interfaces, or namespaces |

A typical workflow: **search** to find the right symbol name, then **query** to explore its relationships.

```bash
# Step 1: discover the symbol
codegraph search checkout

# Step 2: explore its structural relationships
codegraph query MyApp.Orders.CheckoutService --depth 2
```

---

## Common Workflows

### Exploring an Unfamiliar Codebase

```bash
# What deals with "authentication"?
codegraph search auth

# What's in the "infrastructure" layer?
codegraph search Infrastructure --kind type
```

### Finding a Symbol When You Only Know Part of the Name

```bash
# Know it ends with "Repository"
codegraph search Repository --kind type

# Know the file lives in "Controllers"
codegraph search Controllers --kind type
```

### Scoping by Kind to Reduce Noise

```bash
# Skip infrastructure/namespace noise, focus on types
codegraph search customer --kind type

# Look for specific method implementations
codegraph search HandleRequest --kind method
```

---

## Multi-Solution Usage

In a [multi-solution setup](configuration.md#multi-solution-configuration), `search` queries the merged graph by default. Each result includes its file path, which reveals which sub-solution it belongs to.

```bash
# Search across all solutions
codegraph search payment

# Search within a specific solution's sub-graph
codegraph search payment --graph-dir .codegraph/Api
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — structural traversal once you know the symbol
- [`codegraph list`](list.md) — browse assemblies, types, interfaces, and namespaces
- [`codegraph compare`](compare.md) — structural side-by-side comparison of two symbols
