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

All 461+ tests should pass (922 total runs across net8.0 and net10.0). If they don't, check that you have both .NET 8.0 and .NET 10.0 SDKs installed.

### Mutation Testing

We use [Stryker.NET](https://stryker-mutator.io/) for mutation testing to verify test quality:

```bash
dotnet tool restore
cd tests/CodeGraph.Core.Tests && dotnet stryker
cd tests/CodeGraph.Indexer.Tests && dotnet stryker
cd tests/CodeGraph.Query.Tests && dotnet stryker
```

Each test project has a `stryker-config.json` with thresholds. Stryker generates HTML reports in `StrykerOutput/`. The mutation testing CI workflow runs automatically on PRs.

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
│   ├── CodeGraph.Core.Tests/        # Models, config, schema validation
│   ├── CodeGraph.Indexer.Tests/     # Pass logic, workspace parsing
│   ├── CodeGraph.Query.Tests/       # Query engine, filters, formatters
│   └── CodeGraph.Integration.Tests/ # End-to-end scenarios
├── nupkg/                           # Local NuGet package output
├── docs/                            # Documentation
├── codegraph.json.example           # Annotated config example
└── global.json                      # SDK version pinning (10.0.201)
```

### Key Design Decisions

- **No MSBuildWorkspace** — The indexer uses a hybrid approach: `dotnet build` for compilation, then manual assembly of Roslyn `CSharpCompilation` objects via `HybridWorkspaceLoader`. This avoids MSBuildWorkspace reliability issues.
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

---

## PR Process

### Before Submitting

1. **Build succeeds**: `dotnet build CodeGraph.sln`
2. **All tests pass**: `dotnet test CodeGraph.sln`
3. **Mutation score acceptable**: Run `dotnet stryker` in affected test projects — aim for ≥60% mutation score on new code
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

## Questions?

Open an issue if you're unsure about an approach. We're happy to discuss before you invest time in a PR.
