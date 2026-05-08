# How to Use `codegraph compare`

`codegraph compare` performs a **structural side-by-side comparison** of two symbols in the graph. It identifies what the two symbols have in common (shared interfaces, shared base types, shared dependencies) and what is unique to each — giving you a focused diff of two code elements without needing to read their source.

---

## CLI Reference

```
codegraph compare <symbolA> <symbolB> [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--depth <n>` | Traversal depth for relationship collection | `1` |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

`<symbolA>` and `<symbolB>` are symbol patterns (same syntax as `codegraph query`). Wildcards are supported.

---

## Output Format

The command outputs Markdown sections:

| Section | Description |
|---------|-------------|
| `## Shared Interfaces` | Interfaces implemented by **both** symbols |
| `## Shared Base Types` | Base types inherited by **both** symbols |
| `## Shared Dependencies` | Edges (calls, depends-on, etc.) that appear in **both** symbols' subgraphs |
| `## Unique to <A>` | Relationships present only in symbolA's subgraph |
| `## Unique to <B>` | Relationships present only in symbolB's subgraph |

If either symbol is not found in the graph, a warning line is printed and the relevant sections are omitted.

---

## Examples

### Compare two service implementations

```bash
codegraph compare OrderService InvoiceService
```

Example output:

```markdown
# Compare: MyApp.Services.OrderService vs MyApp.Services.InvoiceService

## Shared Interfaces
  - ITransactionalService

## Shared Dependencies
  - calls → IRepository.SaveAsync(...)
  - depends-on → MyApp.Domain.Money

## Unique to MyApp.Services.OrderService
  - calls → IEventBus.PublishAsync(OrderPlacedEvent)
  - depends-on → MyApp.Domain.OrderRequest

## Unique to MyApp.Services.InvoiceService
  - calls → IPdfGenerator.GenerateAsync(Invoice)
  - depends-on → MyApp.Domain.InvoiceRequest
```

### Compare with deeper traversal

```bash
codegraph compare CacheService SessionService --depth 2
```

### Compare using wildcard patterns

```bash
codegraph compare "*OrderRepo*" "*InvoiceRepo*"
```

---

## When to Use `compare` vs `query`

| Task | Recommended command |
|------|---------------------|
| Understand a single symbol's relationships | `codegraph query <symbol>` |
| Find what two symbols share or differ | `codegraph compare <A> <B>` |
| Trace full dependency graph | `codegraph query <symbol> --depth 3` |
| Assess impact of changing a symbol | `codegraph impact` (via MCP) |

---

## Workflow: Refactoring two similar classes

When considering merging or extracting a shared base class, `compare` reveals whether the extraction is safe:

```bash
# 1. Find what they share
codegraph compare UserNotificationService AdminNotificationService

# 2. Query each in depth to confirm
codegraph query UserNotificationService --depth 2
codegraph query AdminNotificationService --depth 2
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — Query a single symbol
- [`codegraph search`](search.md) — Find symbols by name before comparing
- [`codegraph diff`](diff.md) — Compare graph snapshots (structural changes over time)
