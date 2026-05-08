# How to Use `codegraph packages`

`codegraph packages` analyzes NuGet package usage across your indexed solution. It shows which packages each project references, how many external types and internal usages are attributed to each package, and highlights version conflicts where the same package is referenced at different versions.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# List all packages by project
codegraph packages

# Show which projects reference a specific package
codegraph packages --package Newtonsoft.Json

# Scope to a specific project
codegraph packages --project MyApp.Core
```

---

## CLI Reference

```
codegraph packages [options]
```

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--project <name>` | Scope output to a single project (substring match) | All projects |
| `--package <name>` | Show usage details for a specific package (substring match) | All packages |
| `--format <fmt>` | Output format: `text` or `json` | `text` |
| `--graph-dir <path>` | Directory containing the indexed graph | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph packages                                  # All packages, all projects
codegraph packages --project MyApp.Core             # Only MyApp.Core's packages
codegraph packages --package Newtonsoft.Json        # Who uses Newtonsoft.Json and how?
codegraph packages --format json                    # JSON output
codegraph packages --format json > packages.json    # Write to file
```

---

## Understanding the Output

### Default (project view)

```
# Package usage (12 entries)

## MyApp.Core
- Newtonsoft.Json v13.0.3
  Types: 4, Usages: 27
  External symbols: JObject, JToken, JsonConvert, JsonSerializer
  Internal users: OrderSerializer, PaymentSerializer

- Microsoft.Extensions.Logging.Abstractions v8.0.0
  Types: 2, Usages: 41
  External symbols: ILogger, ILoggerFactory
  Internal users: OrderService, PaymentProcessor, ...

## MyApp.Infrastructure
- Microsoft.EntityFrameworkCore v8.0.0
  Types: 8, Usages: 63
  External symbols: DbContext, DbSet<T>, ...
  Internal users: AppDbContext, OrderRepository, ...
```

**Types** — number of distinct external types from this package that appear in the graph.

**Usages** — number of edges in the graph that reference external symbols from this package.

**External symbols** — a sample of the external types/methods referenced.

**Internal users** — a sample of your internal types that reference this package.

### Version conflicts

When the same package is referenced at different versions, a conflicts section is appended:

```
# Version conflicts (1 package)

## Newtonsoft.Json
  MyApp.Core: v13.0.3
  MyApp.Legacy: v12.0.1
```

---

## Common Workflows

### Dependency Audit

```bash
# Get a full picture of all external dependencies
codegraph packages --format json > audit.json
```

### Finding Over-Used Packages

```bash
# Find packages with high internal usage counts — candidates for abstraction
codegraph packages
# Packages with "Usages: 100+" are likely worth wrapping in an abstraction layer.
```

### Resolving Version Conflicts

```bash
# Check for conflicts before upgrading
codegraph packages
# Look for the "Version conflicts" section at the bottom.
```

### Package Impact Analysis

```bash
# Which types in MyApp.Core depend on Newtonsoft.Json?
codegraph packages --package Newtonsoft.Json --project MyApp.Core
```

---

## See Also

- [`codegraph report`](report.md) — full Markdown analysis report including assembly and dependency summaries
- [`codegraph list`](list.md) — browse assemblies and types without package focus
- [`codegraph diff`](diff.md) — detect structural changes (including added/removed package edges)
