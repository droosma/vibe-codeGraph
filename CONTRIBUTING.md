# Contributing to CodeGraph

Thanks for your interest in contributing! This guide covers everything you need to get started.

## Dev Setup

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.201 or later — see `global.json`). .NET 8.0 SDK is also needed for multi-target builds.
- Git

### Clone and Build

```bash
git clone https://github.com/droosma/vibe-codeGraph.git
cd vibe-codeGraph
dotnet build CodeGraph.sln
```

### Run Tests

```bash
dotnet test CodeGraph.sln
```

All 2,000+ tests should pass (4,000+ total runs across net8.0 and net10.0). If they don't, check that you have the correct .NET SDK version.

> **CI runs on Linux** (`ubuntu-latest`). Ensure tests are cross-platform — see [Cross-Platform Test Guidelines](#cross-platform-test-guidelines) below.

### Mutation Testing

We use [Stryker.NET](https://stryker-mutator.io/) for mutation testing to verify test quality:

```bash
dotnet tool restore
cd tests/CodeGraph.Core.Tests && dotnet stryker
cd tests/CodeGraph.Indexer.Tests && dotnet stryker
cd tests/CodeGraph.Query.Tests && dotnet stryker
```

Each unit test project (`Core.Tests`, `Indexer.Tests`, `Query.Tests`) has a `stryker-config.json` with thresholds (break at 80%, low at 90%, high at 100%). Stryker generates HTML reports in `StrykerOutput/`. The mutation testing CI workflow runs automatically on PRs. Integration tests are excluded from mutation testing.

---

## Solution Structure

```
CodeGraph.sln
├── src/
│   ├── CodeGraph.Core/              # Shared models, IO, configuration
│   │   ├── Models/                  # GraphNode, GraphEdge, GraphMetadata, enums
│   │   ├── IO/                      # GraphWriter, GraphReader, GraphMerger
│   │   └── Configuration/           # CodeGraphConfig, ConfigLoader
│   ├── CodeGraph.Indexer/           # CLI: codegraph index
│   │   ├── Passes/                  # SyntaxPass, SemanticPass, DiPass, TestCoveragePass
│   │   └── Workspace/               # HybridWorkspaceLoader, parsers, resolvers
│   └── CodeGraph.Query/             # CLI: codegraph query
│       ├── QueryEngine.cs           # Subgraph extraction + pattern matching
│       ├── Filters/                 # DepthFilter, RankingStrategy, EdgeTypeFilter
│       └── OutputFormatters/        # ContextFormatter, JsonFormatter, TextFormatter
├── tests/
│   ├── Directory.Build.props        # Shared test analyzer suppressions (CA1707, CA1816, CA1861)
│   ├── CodeGraph.Core.Tests/        # Models, config, schema validation
│   ├── CodeGraph.Indexer.Tests/     # Pass logic, workspace parsing
│   ├── CodeGraph.Query.Tests/       # Query engine, filters, formatters
│   └── CodeGraph.Integration.Tests/ # End-to-end scenarios (cross-solution, graph diff)
├── nupkg/                           # Local NuGet package output
├── docs/                            # Documentation
├── codegraph.json.example           # Annotated config example
└── global.json                      # SDK version pinning (10.0.201)
```

### Key Design Decisions

- **No MSBuildWorkspace** — The indexer uses a hybrid approach: `dotnet restore` for NuGet resolution, then manual assembly of Roslyn `CSharpCompilation` objects via `HybridWorkspaceLoader`. This avoids MSBuildWorkspace reliability issues and skips unnecessary compilation to disk.
- **Four-pass indexing** — `SyntaxPass` extracts structure, `SemanticPass` resolves relationships, `DiPass` maps DI registrations, `TestCoveragePass` links tests to production code. Each pass is focused and independently testable.
- **Records everywhere** — `GraphNode`, `GraphEdge`, `ProjectGraph`, `GraphMetadata`, `QueryResult` are all immutable records.
- **Split output** — Graph files are split by assembly (one per project), external dependencies go to `_external.json` as an SBOM-like graph. Designed for LLM context window consumption.
- **Minimal dependencies** — Only `Microsoft.CodeAnalysis.CSharp` in the indexer. Core and Query have no external NuGet dependencies.

---

## How to Add a Test

Tests use **xunit** with `[Fact]` and `[Theory]` attributes. Indexer tests typically create inline Roslyn compilations:

```csharp
public class MyNewFeatureTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void ShouldExtractExpectedNodes()
    {
        var compilation = CreateCompilation("namespace Foo { public class Bar { } }");
        var pass = new SyntaxPass();
        var (nodes, edges) = pass.Execute(compilation, "");

        Assert.Contains(nodes, n => n.Kind == NodeKind.Type && n.Name == "Bar");
    }
}
```

### Cross-Platform Test Guidelines

The CI runs on **Linux** (`ubuntu-latest`). Tests must work on all platforms:

- **Avoid hardcoded Windows paths** — never use literals like `@"D:\repo\Service.cs"`. Use `Path.Combine` and `Path.GetTempPath()` for real filesystem paths.
- **In-memory test data** — when a path is just a string stored in a model property (e.g., `GraphNode.FilePath`) and never passed to the filesystem or `Path.GetRelativePath`, any literal string is safe. But prefer platform-neutral values like `"repo/Service.cs"` to signal intent.
- **Roslyn syntax tree paths** — when creating a `CSharpSyntaxTree` with a file path, use a relative path (e.g., `"Service.cs"`) or `Path.Combine` so `Path.GetRelativePath` works on Linux.
- **Temp directories** — use `Path.Combine(Path.GetTempPath(), ...)` for test temp directories, then clean up in `IDisposable.Dispose()`.
- **Order-independent assertions** — directory enumeration order differs between Linux and Windows. When testing that a collection contains results from multiple sub-directories (e.g., federated graph loading), use `Assert.True(result.Count >= N)` or `Assert.Contains` rather than asserting on exact positions. Never rely on `result[0]` being a specific item when results come from directory scans.
- **Unique symbol queries** — when testing query results, use a symbol name that matches only one node in the test graph. Ambiguous patterns (e.g., a generic name that matches multiple nodes) can return results in different orders on different platforms, causing `result.MatchedNodes[0].Id` assertions to fail non-deterministically.

```csharp
// ❌ Fails on Linux — Path.GetRelativePath can't relativize against a Windows root
var (nodes, _) = pass.Execute(compilation, @"D:\repo", rootPath: @"D:\repo");

// ✅ Use an empty string or a relative root for in-memory tests
var (nodes, _) = pass.Execute(compilation, solutionRoot: string.Empty);

// ✅ Use Path.Combine for real temp paths
var dir = Path.Combine(Path.GetTempPath(), $"cg-test-{Guid.NewGuid():N}");

// ❌ Brittle — directory scan order varies; result[0] may differ on Linux
Assert.Equal("Backend.OrderService", result.MatchedNodes[0].Id);

// ✅ Order-independent: assert membership, not position
Assert.Contains(result.MatchedNodes, n => n.Id == "Backend.OrderService");

// ❌ Brittle — "Service" matches both "Backend.Service" and "Frontend.Service"
var result = engine.Query(new QueryOptions { Pattern = "Service", Depth = 0 });
Assert.Equal("Backend.Service", result.MatchedNodes[0].Id);

// ✅ Use a pattern that uniquely identifies one node
var result = engine.Query(new QueryOptions { Pattern = "Backend.OrderService", Depth = 0 });
Assert.Single(result.MatchedNodes);
```

---

## PR Process

### Before Submitting

1. **Build succeeds**: `dotnet build CodeGraph.sln`
2. **All tests pass**: `dotnet test CodeGraph.sln`
3. **Mutation score acceptable**: Run `dotnet stryker` in affected test projects — aim for ≥80% mutation score on new code (CI breaks below 80%)
4. **No unrelated changes**: Keep PRs focused on a single concern

### Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add --changed-only flag to index command
fix: handle missing meta.json gracefully in incremental index
docs: add architecture documentation
test: add fixtures for SemanticPass edge cases
refactor: extract git helper methods from Program.cs
```

### PR Requirements

- All existing tests must pass
- New features should include tests
- Documentation updates for user-facing changes
- Keep the PR description clear about what and why

---

## Code Style

- Follow existing patterns in the codebase
- **ImplicitUsings** and **Nullable** are enabled across all projects
- Prefer explicit types over `var` for non-obvious types (e.g., `List<GraphNode>` not `var`)
- `var` is acceptable for obvious assignments (e.g., `var path = Path.Combine(...)`)
- Use records for immutable data types (see `GraphNode`, `GraphEdge`)
- Keep methods focused — prefer small, testable units
- XML doc comments on public APIs
- No unnecessary dependencies — the project intentionally avoids heavy frameworks

---

## Agentic Workflows

The repository uses [GitHub Agentic Workflows](https://github.github.com/gh-aw/introduction/overview/) defined in `.github/workflows/*.md`. Each workflow `.md` file has a corresponding `.lock.yml` file that is auto-generated.

### Editing Workflow Files

If you edit a workflow `.md` file (especially its YAML frontmatter), you **must** recompile the lock file before committing:

```bash
gh aw compile
```

Then commit both the `.md` and the updated `.lock.yml` file together. If the lock file is out of sync, the workflow CI will fail with an `ERR_CONFIG: Lock file '...' is outdated!` error.

> **Note**: Changes to the markdown body (below the frontmatter) do not always require recompilation, but frontmatter changes always do.

---

## Questions?

Open an issue if you're unsure about an approach. We're happy to discuss before you invest time in a PR.
