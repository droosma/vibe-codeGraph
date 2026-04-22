# Code Explorer

Explore and understand C# codebase structure using CodeGraph's semantic graph.

## When to Use

Use this skill when:
- Investigating how components are connected in a C# codebase
- Tracing call chains, type hierarchies, or dependency flows
- Finding which tests cover specific production code
- Understanding IoC/DI wiring between interfaces and implementations
- Answering structural questions that grep cannot reliably answer

## What It Does

- Queries the CodeGraph semantic graph via the `codegraph_query` MCP tool
- Returns focused subgraphs with nodes (types, methods, properties) and edges (calls, inherits, implements, depends-on)
- Supports wildcard symbol patterns, edge type filtering, and depth-limited BFS traversal
- Provides output in context (markdown), JSON, or plain text format

## Prerequisites

- CodeGraph must be installed (`dotnet tool install -g CodeGraph`)
- The codebase must be indexed (`codegraph index --solution YourApp.sln`)
- Graph files must exist in `.codegraph/` (or configured output directory)
