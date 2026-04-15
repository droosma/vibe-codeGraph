# Configuration Reference

CodeGraph is configured via a `codegraph.json` file in your repo root. All settings have sensible defaults — you only need to set `solution`.

Generate a starter config: `codegraph init`

## Config File Discovery

The `ConfigLoader` searches for `codegraph.json` by walking up the directory tree from the current working directory. You can also specify a path explicitly with `--config <path>`.

---

## Top-Level Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `solution` | `string?` | `null` | Path to the `.sln` file. Can also be set via `--solution` CLI flag. |
| `output` | `string` | `".codegraph"` | Output directory for graph JSON files. |
| `splitBy` | `string` | `"project"` | How to split graph files: `"project"` (one file per project) or `"namespace"` (one file per root namespace). |

---

## `index` — Indexing Options

Controls which projects are indexed and how.

```json
{
  "index": {
    "includeProjects": ["*"],
    "excludeProjects": ["*.Benchmarks"],
    "includeExternalPackages": ["MediatR", "FluentValidation"],
    "excludeExternalPackages": ["Microsoft.*", "System.*"],
    "maxDepthForExternals": 1,
    "configuration": "Debug",
    "preprocessorSymbols": []
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `includeProjects` | `string[]` | `["*"]` | Wildcard patterns for projects to include. `*` matches all. |
| `excludeProjects` | `string[]` | `[]` | Wildcard patterns for projects to exclude. Applied after includes. |
| `includeExternalPackages` | `string[]` | `[]` | NuGet package names to track as external nodes (e.g., `"MediatR"`). |
| `excludeExternalPackages` | `string[]` | `["Microsoft.*", "System.*"]` | NuGet package patterns to ignore. |
| `maxDepthForExternals` | `int` | `1` | How many levels deep to traverse into external types. |
| `configuration` | `string` | `"Debug"` | Build configuration passed to `dotnet build`. |
| `preprocessorSymbols` | `string[]` | `[]` | Additional preprocessor symbols for compilation. |

---

## `ioc` — IoC/DI Resolution

Controls how CodeGraph discovers dependency injection registrations to create `ResolvesTo` edges.

```json
{
  "ioc": {
    "enabled": true,
    "entryPoints": ["src/MyApp.Api/Program.cs"],
    "additionalEntryPoints": ["tests/MyApp.Tests/TestFixture.cs"],
    "registrationMethodPatterns": ["Add*", "Register*", "Bind*", "Map*"],
    "ignoreMethodPatterns": ["AddLogging", "AddOptions"],
    "inferSingleImplementations": true,
    "scanAssemblyRegistrations": true,
    "followExtensionMethodDepth": 1
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `enabled` | `bool` | `true` | Enable DI resolution analysis. |
| `entryPoints` | `string[]` | `[]` | Paths to DI registration files (e.g., `Program.cs`, `Startup.cs`). |
| `additionalEntryPoints` | `string[]` | `[]` | Extra entry points (e.g., test fixtures with custom DI setup). |
| `registrationMethodPatterns` | `string[]` | `["Add*", "Register*", "Bind*", "Map*"]` | Wildcard patterns for method names that register services. |
| `ignoreMethodPatterns` | `string[]` | `["AddLogging", "AddOptions"]` | Method names to skip during DI scanning. |
| `inferSingleImplementations` | `bool` | `true` | Auto-resolve interfaces with exactly one implementation in the solution. |
| `scanAssemblyRegistrations` | `bool` | `true` | Scan compiled assemblies for registration patterns. |
| `followExtensionMethodDepth` | `int` | `1` | How many levels deep to follow extension method chains during DI analysis. |

---

## `tests` — Test Discovery

Controls how CodeGraph discovers test methods and creates `Covers` edges.

```json
{
  "tests": {
    "enabled": true,
    "testAttributePatterns": ["*Fact", "*Theory", "*Test", "*TestCase", "*TestMethod"],
    "setupAttributePatterns": ["*SetUp", "*Initialize", "*ClassInitialize"],
    "includeSetupMethods": true
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `enabled` | `bool` | `true` | Enable test discovery. |
| `testAttributePatterns` | `string[]` | `["*Fact", "*Theory", "*Test", "*TestCase", "*TestMethod"]` | Attribute patterns that identify test methods. Supports xUnit, NUnit, MSTest. |
| `setupAttributePatterns` | `string[]` | `["*SetUp", "*Initialize", "*ClassInitialize"]` | Attribute patterns for setup/teardown methods. |
| `includeSetupMethods` | `bool` | `true` | Include setup/teardown methods as nodes in the graph. |

---

## `docs` — Documentation Extraction

Controls extraction of documentation from code and markdown files.

```json
{
  "docs": {
    "enabled": true,
    "markdownDirs": ["docs/", "README.md"],
    "includeXmlDocs": true
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `enabled` | `bool` | `true` | Enable documentation extraction. |
| `markdownDirs` | `string[]` | `["docs/", "README.md"]` | Markdown files or directories to analyze. |
| `includeXmlDocs` | `bool` | `true` | Extract `<summary>` from XML doc comments on symbols. |

---

## `query` — Query Defaults

Default values for the `codegraph query` command. CLI flags override these.

```json
{
  "query": {
    "defaultDepth": 1,
    "defaultFormat": "context",
    "maxNodes": 50
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `defaultDepth` | `int` | `1` | Default BFS traversal depth. |
| `defaultFormat` | `string` | `"context"` | Default output format: `"json"`, `"text"`, or `"context"`. |
| `maxNodes` | `int` | `50` | Maximum nodes returned in a query result. |

---

## Example Configurations

### Minimal

```json
{
  "solution": "MyApp.sln"
}
```

Everything else uses defaults: output to `.codegraph`, split by project, index all projects.

### Monorepo with Selective Indexing

```json
{
  "solution": "MyApp.sln",
  "output": ".codegraph",
  "index": {
    "includeProjects": ["MyApp.Api", "MyApp.Services", "MyApp.Data"],
    "excludeProjects": ["*.Benchmarks", "*.Migrations"]
  },
  "ioc": {
    "entryPoints": ["src/MyApp.Api/Program.cs"]
  }
}
```

### With External Package Tracking

```json
{
  "solution": "MyApp.sln",
  "index": {
    "includeExternalPackages": ["MediatR", "FluentValidation", "AutoMapper"],
    "excludeExternalPackages": ["Microsoft.*", "System.*"],
    "maxDepthForExternals": 2
  }
}
```

### Full Configuration

See [`codegraph.json.example`](../codegraph.json.example) in the repo root for a fully annotated example.

---

## CLI Overrides

CLI flags take precedence over config file values:

| CLI Flag | Overrides |
|----------|-----------|
| `--solution` | `solution` |
| `--output` | `output` |
| `--configuration` | `index.configuration` |
| `--projects` | `index.includeProjects` |
| `--config <path>` | Config file location |

---

## Tips

- **Minimal config**: You only need `solution` — everything else has sensible defaults.
- **CI override**: Use `--configuration Release --skip-build` in CI if you've already built.
- **Large repos**: Increase `maxNodes` or use `--namespace` / `--project` filters to scope queries.
- **External packages**: Add frequently-used libraries to `includeExternalPackages` to see cross-assembly call chains.
