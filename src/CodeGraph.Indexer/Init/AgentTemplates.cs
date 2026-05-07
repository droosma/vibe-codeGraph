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
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only direct relationships (fewer results, higher relevance) |
        | `--include-external` | Include NuGet/framework dependencies |

        ## When to Use CodeGraph vs Grep

        | Question Type | Use CodeGraph | Use Grep |
        |--------------|---------------|----------|
        | "What calls X?" | ✅ `--kind calls` | ❌ |
        | "What implements IFoo?" | ✅ `--kind resolves-to` | ❌ |
        | "How is X wired in DI?" | ✅ `--kind resolves-to` | ❌ |
        | "What does the method body do?" | ❌ | ✅ view specific lines |
        | "Find all TODO comments" | ❌ | ✅ grep |
        | "What's the architecture?" | ✅ `list` + `report` | ❌ |
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

        ### Quick Reference

        ```bash
        codegraph query <symbol> --depth 1 --format compact   # relationships
        codegraph query <symbol> --depth 3 --kind calls        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph list assemblies                               # project overview
        codegraph report                                        # generate full report
        ```

        ### When to Use CodeGraph vs Grep

        | Question | Tool |
        |----------|------|
        | What calls X? / What does X depend on? | `codegraph query` |
        | What implements interface Y? | `codegraph query Y --kind resolves-to` |
        | What's the project architecture? | `codegraph list` + `codegraph report` |
        | What does the method body do? | `grep` / `view` (read source) |
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
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only direct relationships (fewer results, higher relevance) |
        | `--include-external` | Include NuGet/framework dependencies |
        | `--namespace <pat>` | Filter by namespace (wildcards ok) |
        | `--project <name>` | Filter by project/assembly |

        ## When to Use CodeGraph vs Grep

        | Question Type | Use CodeGraph | Use Grep/View |
        |--------------|---------------|---------------|
        | What calls method X? | ✅ `--kind calls` | ❌ |
        | What implements IFoo? | ✅ `--kind resolves-to` | ❌ |
        | How is X wired in DI? | ✅ `--kind resolves-to` | ❌ |
        | What's the architecture? | ✅ `list` + `report` | ❌ |
        | Type hierarchy of X? | ✅ `--kind inherits` | ❌ |
        | What does the method body do? | ❌ | ✅ read source |
        | Find string literals / comments | ❌ | ✅ grep |

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
