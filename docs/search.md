# How to Use `codegraph search`

`codegraph search` performs a **fast, fuzzy name lookup** across all symbols in the graph — types, methods, namespaces, properties, and fields. Use it to discover symbol names before running `codegraph query` or `codegraph compare`.

Unlike `codegraph query`, which traverses relationships, `codegraph search` only returns a flat list of matching node names with their kind, namespace, and file location. It does not follow edges or compute subgraphs.

---

## CLI Reference

```
codegraph search <query> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--top <n>` | Maximum results to return | `20` |
| `--kind <kind>` | Filter by node kind: `type`, `method`, `namespace`, `property`, `field` | All kinds |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

`<query>` is a **case-insensitive substring** matched against node names, namespace names, and file paths.

---

## Output Format

Each matching node is printed on one line:

```
[Kind] Name  ns=<namespace>  (<file-path>)
```

Example:

```
[Type] OrderService  ns=MyApp.Services  (src/Services/OrderService.cs)
[Method] PlaceOrder  ns=MyApp.Services  (src/Services/OrderService.cs)
[Type] OrderRepository  ns=MyApp.Data  (src/Data/OrderRepository.cs)

3 result(s) for 'Order'.
```

---

## Examples

### Find all symbols containing "Order"

```bash
codegraph search Order
```

### Restrict to types only

```bash
codegraph search Order --kind type
```

### Find methods by partial name

```bash
codegraph search PlaceOrder --kind method
```

### Increase result count

```bash
codegraph search Service --top 50
```

### Search in a specific graph directory

```bash
codegraph search Repository --graph-dir .codegraph/Backend
```

---

## Typical Workflow

Use `search` as a discovery step before running `query`:

```bash
# 1. Discover the exact name
codegraph search PaymentProcessor --kind type

# 2. Query with the full qualified name
codegraph query "MyApp.Payments.PaymentProcessor" --depth 2

# 3. (Optional) Compare with a related type
codegraph compare "MyApp.Payments.PaymentProcessor" "MyApp.Payments.RefundProcessor"
```

---

## When to Use `search` vs `query`

| Task | Recommended command |
|------|---------------------|
| Discover symbol names by substring | `codegraph search <substring>` |
| Explore relationships of a known symbol | `codegraph query <symbol>` |
| Browse all assemblies and types | `codegraph list` |
| Find callers or callees | `codegraph query <symbol> --kind calls-from` |

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — Explore relationships after finding a symbol
- [`codegraph list`](list.md) — Browse the graph hierarchy by assembly, type, or namespace
- [`codegraph compare`](compare.md) — Structurally compare two symbols
