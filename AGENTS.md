# AGENTS.md

This file provides guidance to AI coding agents (OpenCode, Codex, Cursor, Copilot, etc.) when working with code in this repository.

## Repository Overview

**CodeGraph** gives AI coding agents structural code understanding instead of grep. It's a Roslyn-powered CLI tool that builds a semantic graph of C# codebases and exposes it via MCP (Model Context Protocol).

## Available Skills

This repository provides three agent skills, compatible with [skills.sh](https://skills.sh):

### codegraph-query

Query the semantic graph for structural relationships — callers, callees, type hierarchies, DI wiring, test coverage.

```bash
codegraph query <symbol-pattern> --depth 1 --format context
```

**Use when:** finding callers, tracing dependencies, understanding type hierarchies, or answering structural questions about C# code.

See [`skills/codegraph-query/SKILL.md`](skills/codegraph-query/SKILL.md) for full instructions.

### codegraph-index

Build or update the semantic graph from a C# solution.

```bash
codegraph index --solution YourApp.sln --changed-only
```

**Use when:** the graph is missing, stale, or needs updating after code changes.

See [`skills/codegraph-index/SKILL.md`](skills/codegraph-index/SKILL.md) for full instructions.

### codegraph-review

Use the graph for code review impact analysis — find dependents, check test coverage, assess blast radius.

```bash
codegraph query <changed-symbol> --kind calls-from --depth 1
```

**Use when:** reviewing PRs, checking impact of changes, or assessing blast radius.

See [`skills/codegraph-review/SKILL.md`](skills/codegraph-review/SKILL.md) for full instructions.

## Quick Start

```bash
# Install CodeGraph
dotnet tool install -g CodeGraph

# Initialize config + MCP registration
codegraph init

# Index the codebase
codegraph index --solution YourApp.sln

# Query the graph
codegraph query PlaceOrder --depth 1
```

## MCP Integration

If your agent supports MCP, CodeGraph registers `codegraph_query` as a native tool. Run `codegraph init` to generate the MCP config files, or add manually:

```json
{
  "servers": {
    "codegraph": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["codegraph", "mcp"]
    }
  }
}
```

## Development

```bash
# Build (must pass with zero warnings — TreatWarningsAsErrors is enabled)
dotnet build CodeGraph.sln

# Test (all 461+ tests must pass across net8.0 and net10.0)
dotnet test CodeGraph.sln

# Mutation testing (run in affected test project directory)
dotnet tool restore && dotnet stryker
```

## Development Standards

### TDD Workflow (Red-Green-Refactor)

1. **Red** — Write a failing test FIRST that describes the expected behavior
2. **Green** — Write the minimum code to make the test pass
3. **Refactor** — Clean up while keeping tests green

Every change must include tests. For new features:
- Unit tests in the matching `tests/CodeGraph.*.Tests/` project
- Integration tests in `tests/CodeGraph.Integration.Tests/` for end-to-end scenarios
- Mutation score ≥80% on new code (run `dotnet stryker` in test project dir)

### Architecture Rules

- **Records for data** — All graph model types are immutable records (`GraphNode`, `GraphEdge`, etc.)
- **No unnecessary dependencies** — Only add NuGet packages when absolutely required and justified
- **Single Responsibility** — Each pass, filter, formatter has one job
- **Testable in isolation** — Passes accept a `CSharpCompilation`, filters accept collections, formatters accept `QueryResult`
- **CLI-first design** — Every feature is a CLI command first. MCP wraps the same library code.
- **Layer boundaries**: `Indexer → Query → Core`. Never reverse the dependency direction.

### Code Style

- `var` for obvious types, explicit types for complex/non-obvious ones
- Records everywhere for immutable data
- XML doc comments on all public APIs (CS1591 is suppressed only in packaged projects)
- Conventional Commits: `feat:`, `fix:`, `test:`, `refactor:`, `docs:`
- xUnit with `[Fact]` and `[Theory]`
- Test naming: `MethodName_Scenario_ExpectedBehavior` or descriptive `Should_*`
- Nullable reference types enabled — never suppress nullable warnings without justification

### Quality Gates

```bash
dotnet build CodeGraph.sln   # Zero warnings (TreatWarningsAsErrors)
dotnet test CodeGraph.sln    # All tests pass
```

Both must pass before considering any change complete. Do not weaken warnings or suppress analyzers to make code compile.

### Implementation Plan

See `FLEET-PLAN.md` for the comprehensive implementation roadmap with issue references and phase ordering.
