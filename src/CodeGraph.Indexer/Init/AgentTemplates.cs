namespace CodeGraph.Indexer.Init;

/// <summary>
/// Embedded template strings for agent skill files.
/// Templates are version-matched with the CLI binary — no network fetch needed.
/// </summary>
internal static class AgentTemplates
{
    public const string ClaudeSkillMd = """
        # CodeGraph Query Skill

        Use CodeGraph to answer structural questions about the C# codebase.

        ## When to Use

        Use `codegraph query` when you need to understand:
        - What calls a method or type
        - What a method or type depends on
        - Interface implementations and type hierarchies
        - Test coverage relationships
        - Dependency injection wiring

        ## How to Query

        ```bash
        # Find a type and its direct relationships
        codegraph query <symbol-pattern> --depth 1

        # Trace call chains deeper
        codegraph query PlaceOrder --depth 3 --kind calls

        # Find interface implementations
        codegraph query IOrderRepository --kind resolves-to

        # Find what inherits from a base class
        codegraph query BaseController --kind inherits

        # Wildcard search
        codegraph query "Order*" --depth 1

        # Full JSON output for complex analysis
        codegraph query OrderService --format json --depth 2
        ```

        ## Query Options

        | Flag | Description | Default |
        |------|-------------|---------|
        | `--depth <n>` | BFS traversal depth | `1` |
        | `--kind <type>` | Edge filter (see below) | All |
        | `--namespace <pattern>` | Namespace filter (wildcards ok) | All |
        | `--project <name>` | Project filter | All |
        | `--format <fmt>` | `json`, `text`, `context` | `context` |
        | `--max-nodes <n>` | Max nodes in result | `50` |
        | `--include-external` | Include NuGet/external deps | `false` |

        ## Edge Kinds

        | Kind | Description |
        |------|-------------|
        | `calls` / `calls-to` | Method call relationships |
        | `inherits` | Class inheritance |
        | `implements` | Interface implementation |
        | `depends-on` | Project/assembly dependencies |
        | `resolves-to` | DI container wiring |
        | `covers` / `covered-by` | Test coverage |
        | `references` | General references |
        | `overrides` | Method overrides |
        | `contains` | Namespace/type containment |

        ## Tips

        - Start with `--depth 1` and increase if needed
        - Use `--format context` (default) for concise LLM-friendly output
        - Use `--kind` to focus on specific relationship types
        - Wildcard patterns (`Order*`, `*Service`) help when you don't know exact names
        """;

    public const string ClaudeQueryWrapperSh = """
        #!/usr/bin/env bash
        # CodeGraph query wrapper for Claude Code skill scripts
        # Usage: ./query-wrapper.sh <symbol-pattern> [options]
        set -euo pipefail
        codegraph query "$@"
        """;

    public const string CopilotInstructionsSection = """

        ## CodeGraph — Structural Code Intelligence

        This repository has CodeGraph installed. Use `codegraph query` to understand
        code structure instead of grepping through files.

        ### Quick Reference

        ```bash
        # Find a symbol and its relationships
        codegraph query <symbol> --depth 1

        # Trace call chains
        codegraph query PlaceOrder --depth 3 --kind calls

        # Find implementations of an interface
        codegraph query IOrderRepository --kind resolves-to

        # Wildcard search
        codegraph query "Order*" --format context
        ```

        ### When to Use CodeGraph

        - Understanding call chains and dependencies
        - Finding interface implementations
        - Tracing type hierarchies
        - Understanding test coverage relationships
        - Exploring DI container wiring

        Prefer `codegraph query` over grep/find for structural code questions.
        """;

    public const string OpenCodeAgentsSection = """

        ## CodeGraph — Structural Code Intelligence

        This repository has CodeGraph installed (`dotnet tool install -g CodeGraph`).
        Use the `codegraph query` CLI to answer structural questions about the C# codebase.

        ### Usage

        ```bash
        codegraph query <symbol-pattern> [--depth N] [--kind TYPE] [--format FORMAT]
        ```

        ### Examples

        ```bash
        # Find a type and its direct relationships
        codegraph query OrderService --depth 1

        # Trace call chains
        codegraph query PlaceOrder --depth 3 --kind calls

        # Find interface implementations
        codegraph query IOrderRepository --kind resolves-to

        # Wildcard search
        codegraph query "Order*" --format json
        ```

        ### Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`

        Prefer `codegraph query` over grep for structural code questions.
        """;

    public const string CursorRuleMd = """
        # CodeGraph Query Rule

        Use CodeGraph to answer structural questions about the C# codebase.
        Run `codegraph query` instead of grepping for code structure.

        ## Examples

        ```bash
        codegraph query <symbol> --depth 1
        codegraph query PlaceOrder --depth 3 --kind calls
        codegraph query IOrderRepository --kind resolves-to
        codegraph query "Order*" --format context
        ```

        ## Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`

        ## Tips

        - Use `--depth 1` first, increase if needed
        - Use `--kind` to focus on specific relationships
        - `--format context` gives concise LLM-friendly output
        - Wildcard patterns work: `Order*`, `*Service`
        """;

    public const string GenericInstructionsMd = """
        # CodeGraph — Structural Code Intelligence

        This repository uses CodeGraph to provide semantic code understanding.
        Use the `codegraph query` CLI to explore code structure, call chains,
        type hierarchies, and dependency relationships.

        ## Quick Start

        ```bash
        # Find a symbol and its relationships
        codegraph query <symbol-pattern> --depth 1

        # Trace call chains
        codegraph query PlaceOrder --depth 3 --kind calls

        # Find interface implementations
        codegraph query IOrderRepository --kind resolves-to

        # Find what inherits from a base class
        codegraph query BaseController --kind inherits

        # Wildcard search
        codegraph query "Order*" --depth 1

        # JSON output for complex analysis
        codegraph query OrderService --format json --depth 2
        ```

        ## Query Options

        | Flag | Description | Default |
        |------|-------------|---------|
        | `--depth <n>` | BFS traversal depth | `1` |
        | `--kind <type>` | Edge filter (see below) | All |
        | `--namespace <pattern>` | Namespace filter (wildcards ok) | All |
        | `--project <name>` | Project filter | All |
        | `--format <fmt>` | `json`, `text`, `context` | `context` |
        | `--max-nodes <n>` | Max nodes in result | `50` |
        | `--include-external` | Include NuGet/external deps | `false` |

        ## Edge Kinds

        | Kind | Description |
        |------|-------------|
        | `calls` / `calls-to` | Method call relationships |
        | `inherits` | Class inheritance |
        | `implements` | Interface implementation |
        | `depends-on` | Project/assembly dependencies |
        | `resolves-to` | DI container wiring |
        | `covers` / `covered-by` | Test coverage |
        | `references` | General references |
        | `overrides` | Method overrides |
        | `contains` | Namespace/type containment |

        ## When to Use

        Use `codegraph query` instead of grep/find when you need to understand:
        - What calls a method or type
        - What a method or type depends on
        - Interface implementations and type hierarchies
        - Test coverage relationships
        - Dependency injection wiring

        ## Rebuilding the Graph

        If the codebase has changed significantly, rebuild:
        ```bash
        codegraph index --solution <path.sln> --output .codegraph/
        ```
        """;

    /// <summary>
    /// Marker text used to detect if a CodeGraph section has already been appended
    /// to an existing file (Copilot instructions, AGENTS.md).
    /// </summary>
    public const string AppendMarker = "## CodeGraph — Structural Code Intelligence";
}
