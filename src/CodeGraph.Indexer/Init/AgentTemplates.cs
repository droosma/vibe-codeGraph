namespace CodeGraph.Indexer.Init;

/// <summary>
/// Embedded template strings for agent skill files.
/// Templates are version-matched with the CLI binary — no network fetch needed.
/// </summary>
internal static class AgentTemplates
{
    public const string ClaudeSkillMd = """
        # CodeGraph — Structural Code Intelligence

        CodeGraph is a pre-built index of this C# codebase. It knows every type,
        method, call chain, interface implementation, and DI wiring — without
        reading source files. Use it as your **primary** tool for structural
        questions, falling back to grep/view only for implementation detail.

        ## Strategy — Follow This Order

        1. **Orient** — read `.codegraph/REPORT.md` if it exists (free overview, zero tool calls)
        2. **Scope** — `codegraph list assemblies` to find relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. **Deepen** — increase `--depth` or add `--kind` filters to follow specific edges
        5. **Detail** — only grep/view specific source lines when you need method bodies

        Default MCP settings are optimized for token efficiency:
        - Format defaults to `compact` (~3-5× fewer tokens than `context`)
        - Mode defaults to `focused` (high-signal edges only)
        - Use `--format context` when you need full signatures and metadata
        - Use `--mode all` only for exhaustive analysis

        This strategy uses ~4× fewer tokens than reading source files directly.

        ## Commands

        ```bash
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph query <symbol> --kind implements             # implementations
        codegraph list assemblies                               # project overview
        codegraph list types --project <name>                   # types in a project
        codegraph report                                        # generate full report
        codegraph stats                                         # node/edge counts
        ```

        ## Key Flags

        | Flag | Purpose |
        |------|---------|
        | `--depth <n>` | BFS depth (start at 1, increase as needed) |
        | `--kind <type>` | Filter: `calls`, `inherits`, `implements`, `resolves-to`, `covers`, `depends-on` |
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens (MCP default) |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only high-signal relationships — calls, inheritance, DI (MCP default) |
        | `--include-external` | Include NuGet/framework dependencies |

        ## When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph list types --assembly <name>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "What's the overall architecture?" | `codegraph summary` (or read REPORT.md) |
        | "Tell me everything about X" | `codegraph explain <symbol>` |
        | "What does this method body do?" | `grep`/`view` source files |
        | "Find all TODO/FIXME comments" | `grep` |
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

        This repository has a pre-built code graph. Use `codegraph` as your **primary**
        tool for structural questions — it uses ~4× fewer tokens than reading source files.

        ### Strategy — Follow This Order

        1. **Orient** — read `.codegraph/REPORT.md` if it exists (free overview)
        2. **Scope** — `codegraph list assemblies` to find relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact`
        4. **Deepen** — increase `--depth` or add `--kind` to follow edges
        5. **Detail** — only grep/view source when you need method bodies

        MCP defaults: `compact` format + `focused` mode (token-optimized).
        Use `--format context` for full detail, `--mode all` for exhaustive traversal.

        ### Quick Reference

        ```bash
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph list assemblies                               # project overview
        codegraph report                                        # generate full report
        ```

        ### When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph list types --assembly <name>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "What's the overall architecture?" | `codegraph summary` (or read REPORT.md) |
        | "What does this method body do?" | `grep`/`view` source files |
        | "Find all TODO/FIXME comments" | `grep` |
        """;

    public const string OpenCodeAgentsSection = """

        ## CodeGraph — Structural Code Intelligence

        This repository has a pre-built code graph (`codegraph` CLI).
        Use it as your **primary** tool for structural questions — ~4× fewer tokens
        than reading source files.

        ### Strategy

        1. Read `.codegraph/REPORT.md` first (free architectural overview)
        2. `codegraph list assemblies` to scope relevant projects
        3. `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. Increase `--depth` or add `--kind calls|resolves-to|implements` to follow edges
        5. Only grep/view source files when you need method bodies

        ### Commands

        ```bash
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph list assemblies                               # project overview
        codegraph report                                        # full report
        ```

        ### Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`
        """;

    public const string CursorRuleMd = """
        # CodeGraph — Structural Code Intelligence

        Pre-built code graph for this C# codebase. Use `codegraph` instead of
        grepping for code structure — ~4× fewer tokens.

        ## Strategy

        1. Read `.codegraph/REPORT.md` first (free overview)
        2. `codegraph list assemblies` to scope
        3. `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. Add `--kind calls|resolves-to|implements` to filter edges
        5. Only grep/view for method bodies

        ## Commands

        ```bash
        codegraph query <symbol> --depth 1 --format compact
        codegraph query <symbol> --depth 3 --kind calls
        codegraph query I<Name> --kind resolves-to
        codegraph list assemblies
        codegraph report
        ```

        ## Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`
        """;

    public const string GenericInstructionsMd = """
        # CodeGraph — Structural Code Intelligence

        This repository has a pre-built code graph providing semantic understanding
        of all types, methods, call chains, interface implementations, and DI wiring.
        Using CodeGraph consumes ~4× fewer tokens than reading source files directly.

        ## Strategy — Follow This Order

        1. **Orient** — read `.codegraph/REPORT.md` if it exists (free architectural overview)
        2. **Scope** — `codegraph list assemblies` to identify relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. **Deepen** — increase `--depth` or add `--kind` filters to follow specific edges
        5. **Detail** — only grep/view specific source lines when you need method bodies

        ## When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph list types --assembly <name>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "What's the overall architecture?" | `codegraph summary` (or read REPORT.md) |
        | "Tell me everything about X" | `codegraph explain <symbol>` |
        | "What does this method body do?" | `grep`/`view` source files |
        | "Find all TODO/FIXME comments" | `grep` |

        ## Commands

        ```bash
        # Structural queries (use these FIRST)
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring / implementations
        codegraph query <Base> --kind inherits                 # inheritance tree

        # Navigation
        codegraph list assemblies                               # all projects
        codegraph list types --project <name>                   # types in a project
        codegraph list namespaces                               # namespace tree

        # Overview
        codegraph report                                        # full architectural report
        codegraph stats                                         # node/edge counts
        ```

        ## Key Flags

        | Flag | Purpose |
        |------|---------|
        | `--depth <n>` | BFS depth (start at 1, increase as needed) |
        | `--kind <type>` | Filter edges: `calls`, `inherits`, `implements`, `resolves-to`, `covers`, `depends-on` |
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens (MCP default) |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only high-signal relationships — calls, inheritance, DI (MCP default) |
        | `--include-external` | Include NuGet/framework dependencies |
        | `--namespace <pat>` | Filter by namespace (wildcards ok) |
        | `--project <name>` | Filter by project/assembly |

        ## Rebuilding the Graph

        If the codebase has changed significantly, rebuild:
        ```bash
        codegraph index --solution <path.sln> --output .codegraph/
        ```
        """;

    public const string ArchitectAgentMd = """
        # CodeGraph Architect

        You are **CodeGraph Architect**, a specialized agent for architecture
        exploration and structural understanding of C# codebases.

        ## Trigger Phrases

        - "explain architecture"
        - "trace flow"
        - "how are these connected?"
        - "what depends on X?"

        ## Workflow

        1. **Orient** — read `.codegraph/REPORT.md` for an architectural overview
        2. **Scope** — `codegraph list assemblies` to identify relevant projects
        3. **Query / Search** — `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. **Path / Explain** — `codegraph path --from A --to B` or `codegraph explain <symbol>` for connections
        5. **Detail** — only grep/view source files when you need method bodies

        ## Output Format

        - **Architecture summary** — concise description of the system structure
        - **Key symbols / files** — the most important types, methods, and source files
        - **Dependency paths** — how components are connected (call chains, inheritance, DI)
        - **Open questions** — areas that need further investigation

        ## Tool Preferences

        Use MCP/CodeGraph tools **first** for all structural questions:
        ```bash
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph path --from A --to B                         # connectivity
        codegraph explain <symbol>                             # full context
        codegraph summary                                      # architecture overview
        ```

        Fall back to grep/view **only** for implementation detail (method bodies,
        comments, string literals).
        """;

    public const string ReviewerAgentMd = """
        # CodeGraph Reviewer

        You are **CodeGraph Reviewer**, a specialized agent for PR impact analysis
        and blast-radius assessment in C# codebases.

        ## Trigger Phrases

        - "what tests should I run?"
        - "what might break?"
        - "review blast radius"

        ## Workflow

        1. **Identify changed symbols** — determine which types/methods were modified
        2. **Query / Impact** — `codegraph impact <symbol>` or `codegraph query <symbol> --depth 2` for affected callers and dependents
        3. **Diff** — use `git diff` when available for file-level change context
        4. **Test coverage** — `codegraph query <symbol> --kind covers` to find related tests

        ## Output Format

        - **Changed surface** — list of modified types, methods, and their signatures
        - **Affected callers / dependents** — upstream consumers that may be impacted
        - **Test coverage signals** — tests that cover the changed symbols
        - **Risk level** — low / medium / high with rationale

        ## Tool Preferences

        Use MCP/CodeGraph tools **first** for impact analysis:
        ```bash
        codegraph impact <symbol>                              # blast radius
        codegraph query <symbol> --depth 2 --kind calls        # callers
        codegraph query <symbol> --kind covers                 # test coverage
        codegraph query <symbol> --kind depends-on             # dependencies
        ```

        Then use `git diff` for file-level change context. Fall back to grep/view
        only for implementation detail.
        """;

    /// <summary>
    /// Marker text used to detect if a CodeGraph section has already been appended
    /// to an existing file (Copilot instructions, AGENTS.md).
    /// </summary>
    public const string AppendMarker = "## CodeGraph — Structural Code Intelligence";
}
