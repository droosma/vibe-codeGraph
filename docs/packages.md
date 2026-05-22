# How to Use `codegraph packages`

`codegraph packages` analyses **NuGet package usage** across your solution — which packages each project depends on, at what versions, and where version conflicts exist between projects.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# List all NuGet packages across all projects
codegraph packages

# Show packages for a specific project
codegraph packages --project MyApp.Api

# Show which projects use a specific package
codegraph packages --package Newtonsoft.Json

# JSON output for scripting
codegraph packages --format json
```

---

## CLI Reference

```
codegraph packages [options]
```

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--project <name>` | Filter to a specific project | All projects |
| `--package <name>` | Show which projects use this package | All packages |
| `--format json` | Output as JSON | Plain text |
| `--graph-dir <path>` | Graph directory | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## Output

### Plain Text (all projects)

```
NuGet Package Usage

MyApp.Api
  Microsoft.AspNetCore.OpenApi    8.0.0
  Swashbuckle.AspNetCore          6.5.0
  Newtonsoft.Json                 13.0.3

MyApp.Data
  Microsoft.EntityFrameworkCore   8.0.0
  Newtonsoft.Json                 13.0.1   ⚠ version conflict

Version Conflicts:
  Newtonsoft.Json
    MyApp.Api      13.0.3
    MyApp.Data     13.0.1
```

### Plain Text (by package)

```
Newtonsoft.Json usage:

  MyApp.Api      13.0.3
  MyApp.Data     13.0.1   ⚠ conflict
```

### JSON

```json
[
  {
    "project": "MyApp.Api",
    "packages": [
      { "name": "Newtonsoft.Json", "version": "13.0.3" }
    ]
  }
]
```

---

## Common Workflows

### Audit all NuGet dependencies

```bash
codegraph packages
```

### Find which projects use a deprecated package

```bash
codegraph packages --package Newtonsoft.Json
```

### Detect version conflicts across projects

```bash
# Conflicts are highlighted automatically in plain text output
codegraph packages

# In CI, fail if any conflicts are found
CONFLICTS=$(codegraph packages --format json | jq '[.[] | select(.conflicts != null)] | length')
if [ "$CONFLICTS" -gt 0 ]; then
  echo "Package version conflicts detected — resolve before merging."
  exit 1
fi
```

### Audit a single project

```bash
codegraph packages --project MyApp.Infrastructure
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Analysis complete |
| `1` | Error (graph not built — run `codegraph index` first) |
