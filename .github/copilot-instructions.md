# GitHub Copilot Instructions for CodeGraph

## Project Context

CodeGraph is a Roslyn-powered CLI that builds a semantic graph of C# codebases, queryable by LLM agents via MCP. It replaces grep with structured code intelligence.

## Architecture

```
CodeGraph.Core       → Models, IO, Configuration (shared library, no business logic)
CodeGraph.Indexer    → Roslyn passes, workspace loading, CLI entry point, MCP server
CodeGraph.Query      → Query engine, filters, formatters, CLI commands
```

Dependencies flow: Indexer → Query → Core. Never reverse.

## Development Rules

### Quality Gates (non-negotiable)
- `TreatWarningsAsErrors` is enabled via `Directory.Build.props`
- `dotnet build CodeGraph.sln` must produce zero warnings
- `dotnet test CodeGraph.sln` must pass all tests
- Do not suppress analyzer warnings without justification

### TDD (Red-Green-Refactor)
1. Write a failing test first
2. Write minimum code to pass
3. Refactor while keeping green

### Code Patterns
- **Records** for all immutable data types (GraphNode, GraphEdge, QueryResult, etc.)
- **No unnecessary NuGet dependencies** — justify any new package
- **CLI-first** — every feature works as `codegraph <command>`. MCP wraps the same library code.
- **xUnit** with `[Fact]` and `[Theory]`
- **Conventional Commits**: `feat:`, `fix:`, `test:`, `refactor:`, `docs:`

### Style
- `var` for obvious types, explicit types for complex ones
- Nullable reference types enabled — do not suppress nullable warnings
- XML doc comments on public APIs
- Test naming: `MethodName_Scenario_ExpectedBehavior` or `Should_*`

### What NOT to do
- Don't add interfaces for single implementations
- Don't use primary constructors on classes
- Don't add frameworks (DI containers, ORM, etc.) — keep minimal deps
- Don't break existing query behavior without documenting the breaking change

## Key Files

| Purpose | Path |
|---------|------|
| CLI entry point | `src/CodeGraph.Indexer/Program.cs` |
| Query engine | `src/CodeGraph.Query/QueryEngine.cs` |
| MCP server | `src/CodeGraph.Indexer/Mcp/McpServer.cs` |
| Graph models | `src/CodeGraph.Core/Models/` |
| Configuration | `src/CodeGraph.Core/Configuration/CodeGraphConfig.cs` |
| Output formatters | `src/CodeGraph.Query/OutputFormatters/` |
| Integration tests | `tests/CodeGraph.Integration.Tests/` |

## Implementation Roadmap

See `FLEET-PLAN.md` for the phased implementation plan with GitHub issue references.
