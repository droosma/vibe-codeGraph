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
# Build
dotnet build CodeGraph.sln

# Test
dotnet test CodeGraph.sln
```
