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

        1. **Orient** — read `.codegraph/BRIEF.md` or `.codegraph/REPORT.md` (free overview, zero tool calls)
        2. **Scope** — `codegraph list assemblies` to find relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. **Deepen** — increase `--depth` or add `--kind` filters to follow specific edges
        5. **Detail** — use `codegraph query <symbol> --include-source --source-max-lines 12` for a small inline snippet after narrowing to one symbol; use `codegraph_file` / grep-view only when you need file-level context

        CLI defaults are optimized for token efficiency:
        - Format defaults to `compact` when output is piped (~3-5× fewer tokens than `context`)
        - Mode defaults to `focused` (high-signal edges only)
        - Use `--format context` when you need full signatures and metadata
        - Use `--mode all` only for exhaustive analysis
        - Use `--include-source` only AFTER narrowing to a specific method or type
        - Keep `--source-max-lines` low (default: 20) to stay token-safe
        - Use `--json` on any command for machine-readable output

        This strategy uses ~4× fewer tokens than reading source files directly.

        > **Use CLI commands** — they have zero schema overhead. MCP is available
        > as an alternative for IDE-only agents that cannot run shell commands.

        ## Commands

        ```bash
        codegraph brief                                          # generate BRIEF.md orientation
        codegraph query <symbol> --depth 1 --format compact      # relationships
        codegraph query <symbol> --depth 3 --kind calls          # call chains
        codegraph query I<Name> --kind resolves-to               # DI wiring
        codegraph query <symbol> --kind implements               # implementations
        codegraph list assemblies                                 # project overview
        codegraph list types --project <name>                     # types in a project
        codegraph search <term>                                   # fuzzy symbol search
        codegraph explain <symbol>                                # full symbol deep-dive
        codegraph impact <symbol>                                 # blast radius analysis
        codegraph path --from A --to B                            # shortest dependency path
        codegraph test-impact <symbol>                            # test coverage analysis
        codegraph report                                          # generate full report
        codegraph stats                                           # node/edge counts
        ```

        ## Key Flags

        | Flag | Purpose |
        |------|---------|
        | `--depth <n>` | BFS depth (start at 1, increase as needed) |
        | `--kind <type>` | Filter: `calls`, `inherits`, `implements`, `resolves-to`, `covers`, `depends-on` |
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens |
        | `--json` | Machine-readable JSON output |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only high-signal relationships — calls, inheritance, DI (default) |
        | `--include-external` | Include NuGet/framework dependencies |

        ## When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What's the overall architecture?" | Read `.codegraph/BRIEF.md` or `codegraph summary` |
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph search <term>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "Tell me everything about X" | `codegraph explain <symbol>` |
        | "What tests cover X?" | `codegraph test-impact <symbol>` |
        | "What does this method body do?" | `grep`/`view` source files |

        ## Exit Codes

        - `0` — success with results
        - `1` — error (bad args, missing graph, etc.)
        - `2` — success but no results found
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

        1. **Orient** — read `.codegraph/BRIEF.md` or `.codegraph/REPORT.md` (free overview)
        2. **Scope** — `codegraph list assemblies` to find relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact`
        4. **Deepen** — increase `--depth` or add `--kind` to follow edges
        5. **Detail** — use `codegraph query <symbol> --include-source --source-max-lines 12` for a small inline snippet after narrowing to one symbol; use `codegraph_file` or grep/view when you need file-level context

        CLI defaults are token-optimized: `compact` format + `focused` mode when piped.
        Use `--json` for machine-readable output. Use `--format context` for full detail.
        Keep `--source-max-lines` low (default: 20) when you inline source.

        > **Use CLI commands** — they have zero schema overhead. MCP is available
        > as an alternative for IDE-only agents that cannot run shell commands.

        ### Quick Reference

        ```bash
        codegraph brief                                          # generate BRIEF.md orientation
        codegraph query <symbol> --depth 1 --format compact      # relationships
        codegraph query <symbol> --depth 3 --kind calls          # call chains
        codegraph query I<Name> --kind resolves-to               # DI wiring
        codegraph list assemblies                                 # project overview
        codegraph search <term>                                   # fuzzy symbol search
        codegraph explain <symbol>                                # full deep-dive
        codegraph impact <symbol>                                 # blast radius
        codegraph test-impact <symbol>                            # test coverage
        codegraph report                                          # generate full report
        ```

        ### When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What's the overall architecture?" | Read `.codegraph/BRIEF.md` or `codegraph summary` |
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph search <term>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "What tests cover X?" | `codegraph test-impact <symbol>` |
        | "What does this method body do?" | `grep`/`view` source files |

        ### Exit Codes

        `0` success | `1` error | `2` no results found

        ### Delegatable CodeGraph agents

        `codegraph init` also writes reusable agent definitions to `.codegraph/agents/`:

        - `codegraph-architecture.md` — architecture analysis, starts with `.codegraph/REPORT.md`
        - `codegraph-impact.md` — blast radius analysis with `codegraph impact` and `codegraph diff`
        - `codegraph-review.md` — code review analysis using `codegraph_query` / `codegraph query`

        If your Copilot surface supports custom agents, point it at those markdown files.
        Otherwise, follow the same workflows manually.
        """;

    public const string OpenCodeAgentsSection = """

        ## CodeGraph — Structural Code Intelligence

        This repository has a pre-built code graph (`codegraph` CLI).
        Use it as your **primary** tool for structural questions — ~4× fewer tokens
        than reading source files.

        > **Use CLI commands** — they have zero schema overhead and work with any agent.
        > MCP is available as an alternative for IDE-only agents.

        ### Strategy

        1. Read `.codegraph/BRIEF.md` or `.codegraph/REPORT.md` first (free architectural overview)
        2. `codegraph list assemblies` to scope relevant projects
        3. `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. Increase `--depth` or add `--kind calls|resolves-to|implements` to follow edges
        5. Use `codegraph query <symbol> --include-source --source-max-lines 12` for a small inline snippet after narrowing to one symbol; use `codegraph_file` or grep/view when you need file-level context

        ### Commands

        ```bash
        codegraph brief                                          # generate BRIEF.md orientation
        codegraph query <symbol> --depth 1 --format compact      # relationships
        codegraph query <symbol> --depth 3 --kind calls          # call chains
        codegraph query I<Name> --kind resolves-to               # DI wiring
        codegraph list assemblies                                 # project overview
        codegraph search <term>                                   # fuzzy symbol search
        codegraph explain <symbol>                                # full deep-dive
        codegraph impact <symbol>                                 # blast radius
        codegraph test-impact <symbol>                            # test coverage
        codegraph report                                          # full report
        ```

        ### Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`

        ### Exit Codes

        `0` success | `1` error | `2` no results found

        ### Machine-Readable Output

        Add `--json` to any command for JSON output.

        ### Delegatable CodeGraph agents

        `codegraph init` also writes reusable agent definitions to `.codegraph/agents/`:

        - `codegraph-architecture.md` — architecture analysis, starts with `.codegraph/REPORT.md`
        - `codegraph-impact.md` — blast radius analysis with `codegraph impact` and `codegraph diff`
        - `codegraph-review.md` — code review analysis using `codegraph_query` / `codegraph query`

        Load or reference those markdown definitions when your OpenCode / Codex setup supports sub-agents.
        """;

    public const string CursorRuleMd = """
        # CodeGraph — Structural Code Intelligence

        Pre-built code graph for this C# codebase. Use `codegraph` instead of
        grepping for code structure — ~4× fewer tokens.

        > **Use CLI commands** — they have zero schema overhead and work with any agent.
        > MCP is available as an alternative for IDE-only contexts.

        ## Strategy

        1. Read `.codegraph/BRIEF.md` or `.codegraph/REPORT.md` first (free overview)
        2. `codegraph list assemblies` to scope
        3. `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. Add `--kind calls|resolves-to|implements` to filter edges
        5. Only grep/view for method bodies

        ## Commands

        ```bash
        codegraph brief                                          # generate BRIEF.md orientation
        codegraph query <symbol> --depth 1 --format compact      # relationships
        codegraph query <symbol> --depth 3 --kind calls          # call chains
        codegraph query I<Name> --kind resolves-to               # DI wiring
        codegraph list assemblies                                 # project overview
        codegraph search <term>                                   # fuzzy symbol search
        codegraph explain <symbol>                                # full deep-dive
        codegraph impact <symbol>                                 # blast radius
        codegraph test-impact <symbol>                            # test coverage
        codegraph report                                          # full report
        ```

        ## Edge Kinds

        `calls`, `inherits`, `implements`, `depends-on`, `resolves-to`,
        `covers`, `covered-by`, `references`, `overrides`, `contains`

        ## Key Flags

        `--json` machine-readable output | `--depth <n>` BFS depth |
        `--kind <type>` edge filter | `--format compact` minimal tokens |
        `--budget <n>` token cap | `--mode focused` high-signal only

        ## Exit Codes

        `0` success | `1` error | `2` no results found
        """;

    public const string GenericInstructionsMd = """
        # CodeGraph — Structural Code Intelligence

        This repository has a pre-built code graph providing semantic understanding
        of all types, methods, call chains, interface implementations, and DI wiring.
        Using CodeGraph consumes ~4× fewer tokens than reading source files directly.

        > **Use CLI commands** — they have zero schema overhead and work with any agent.
        > MCP is available as an alternative for IDE-only agents that cannot run shell commands.

        ## Strategy — Follow This Order

        1. **Orient** — read `.codegraph/BRIEF.md` or `.codegraph/REPORT.md` (free overview, zero tool calls)
        2. **Scope** — `codegraph list assemblies` to identify relevant projects
        3. **Query** — `codegraph query <symbol> --depth 1 --format compact` for relationships
        4. **Deepen** — increase `--depth` or add `--kind` filters to follow specific edges
        5. **Detail** — only grep/view specific source lines when you need method bodies

        ## When to Use Each Tool

        | Question pattern | Use |
        |-----------------|-----|
        | "What's the overall architecture?" | Read `.codegraph/BRIEF.md` or `codegraph summary` |
        | "What calls/implements/depends on X?" | `codegraph query` |
        | "Find things related to <domain>" | `codegraph search <term>` |
        | "How are A and B connected?" | `codegraph path --from A --to B` |
        | "What breaks if I change X?" | `codegraph impact <symbol>` |
        | "Tell me everything about X" | `codegraph explain <symbol>` |
        | "What tests cover X?" | `codegraph test-impact <symbol>` |
        | "What does this method body do?" | `grep`/`view` source files |

        ## Commands

        ```bash
        # Orientation (start here)
        codegraph brief                                          # generate BRIEF.md overview
        codegraph report                                         # generate full REPORT.md

        # Structural queries
        codegraph query <symbol> --depth 1 --format compact      # relationships
        codegraph query <symbol> --depth 3 --kind calls          # call chains
        codegraph query I<Name> --kind resolves-to               # DI wiring / implementations
        codegraph query <Base> --kind inherits                   # inheritance tree

        # Discovery
        codegraph search <term>                                  # fuzzy symbol search
        codegraph list assemblies                                 # all projects
        codegraph list types --project <name>                     # types in a project
        codegraph list namespaces                                 # namespace tree

        # Analysis
        codegraph explain <symbol>                               # full symbol deep-dive
        codegraph impact <symbol>                                # blast radius
        codegraph test-impact <symbol>                           # test coverage
        codegraph path --from A --to B                           # shortest path
        codegraph stats                                          # node/edge counts
        ```

        ## Key Flags

        | Flag | Purpose |
        |------|---------|
        | `--depth <n>` | BFS depth (start at 1, increase as needed) |
        | `--kind <type>` | Filter edges: `calls`, `inherits`, `implements`, `resolves-to`, `covers`, `depends-on` |
        | `--format compact` | Minimal output — signatures + edges only, ~3-5× fewer tokens |
        | `--json` | Machine-readable JSON output |
        | `--budget <tokens>` | Hard cap on output token count |
        | `--mode focused` | Only high-signal relationships — calls, inheritance, DI (default) |
        | `--include-external` | Include NuGet/framework dependencies |
        | `--namespace <pat>` | Filter by namespace (wildcards ok) |
        | `--project <name>` | Filter by project/assembly |

        ## Exit Codes

        - `0` — success with results
        - `1` — error (bad args, missing graph, etc.)
        - `2` — success but no results found

        ## Rebuilding the Graph

        If the codebase has changed significantly, rebuild:
        ```bash
        codegraph index --solution <path.sln> --output .codegraph/
        codegraph brief   # regenerate orientation
        ```

        ## Delegatable Agent Definitions

        `codegraph init` writes reusable markdown agent definitions to `.codegraph/agents/`:

        - `codegraph-architecture.md`
        - `codegraph-impact.md`
        - `codegraph-review.md`

        Platform-specific integrations can load or mirror those definitions.
        """;

    public const string ArchitectureAgentMd = """
        # CodeGraph Architecture Analyst

        You are **CodeGraph Architecture Analyst**, a delegatable agent focused on
        architecture analysis and structural exploration of C# codebases.

        ## Use For

        - "explain this codebase architecture"
        - "trace the flow for feature X"
        - "How are these modules connected?"
        - "What depends on this service?"

        ## Workflow

        1. **Orient first** — read `.codegraph/REPORT.md` if present. If it is missing, use `.codegraph/BRIEF.md` or run `codegraph report`.
        2. **Scope the system** — run `codegraph list assemblies` to identify the relevant projects and namespaces.
        3. **Use structural queries** — `codegraph search <term>` and `codegraph query <symbol> --depth 1 --format context` to map the important types and entry points.
        4. **Trace connections** — use `codegraph path --from A --to B` and `codegraph explain <symbol>` to explain dependency and call paths.
        5. **Read source last** — grep/view source files only after the graph has narrowed the investigation.

        ## Output Contract

        - **Architecture summary** — short description of the major layers, services, and boundaries
        - **Key symbols / files** — important types, methods, and source files worth reading next
        - **Dependency / call paths** — the paths that explain how the parts connect
        - **Open questions / confidence notes** — unknowns, assumptions, or follow-up checks

        ## Tool Preferences

        Use CodeGraph structure-first commands before reading code:
        ```bash
        codegraph list assemblies                              # scope projects
        codegraph search <term>                                # broad discovery
        codegraph query <symbol> --depth 1 --format context    # relationships
        codegraph query <symbol> --kind calls --depth 3        # call chains
        codegraph query I<Name> --kind resolves-to             # DI wiring
        codegraph explain <symbol>                             # full symbol context
        codegraph path --from A --to B                         # shortest structural path
        ```
        """;

    public const string ImpactAgentMd = """
        # CodeGraph Impact Analyst

        You are **CodeGraph Impact Analyst**, a delegatable agent focused on
        blast-radius analysis and change-risk assessment.

        ## Use For

        - "what might break if I change X?"
        - "review blast radius"
        - "find the impact of this refactor"
        - "which tests should I run?"

        ## Workflow

        1. **Identify the changed surface** — collect changed files, symbols, or PR context from `git diff` or the task description.
        2. **Run blast-radius analysis** — use `codegraph impact <symbol>` for direct and transitive dependents.
        3. **Compare structural snapshots** — use `codegraph diff` when base/head graphs or snapshots are available.
        4. **Check callers and dependents** — use `codegraph query <symbol>` with `calls`, `depends-on`, `implements`, and `covered-by` style relationships.
        5. **Estimate test scope** — use `codegraph test-impact <symbol>` when coverage detail is needed.

        ## Output Contract

        - **Changed structural surface** — key symbols and boundaries affected by the change
        - **Affected callers / dependents** — direct and important transitive consumers
        - **Test / coverage signals** — relevant tests and obvious gaps
        - **Risk level** — low / medium / high with concrete reasoning

        ## Tool Preferences

        Prefer these commands in order:
        ```bash
        git diff --name-only                                   # changed files
        codegraph impact <symbol>                              # blast radius
        codegraph diff --base <graph-dir> --head <graph-dir>   # structural graph diff
        codegraph query <symbol> --kind calls --depth 2        # callers / callees
        codegraph query <symbol> --kind depends-on --depth 2   # dependencies
        codegraph test-impact <symbol>                         # related tests
        ```
        """;

    public const string CodeReviewAgentMd = """
        # CodeGraph Code Review Specialist

        You are **CodeGraph Code Review Specialist**, a delegatable agent for
        structural code review findings, dependent analysis, and test-coverage checks.

        ## Use For

        - "code review"
        - "check dependents"
        - "check test coverage"
        - "review structural risks"

        ## Workflow

        1. **Start from the changed files or symbols** supplied by the orchestrator.
        2. **Use `codegraph_query` first** (or `codegraph query` in CLI mode) to inspect dependents, callers, implementations, and DI consumers.
        3. **Check test coverage** with `codegraph_query` / `codegraph query` using `covered-by`, and use `codegraph test-impact` for broader test selection guidance.
        4. **Escalate only if needed** — use `codegraph impact` or `codegraph diff` when a reviewer needs broader blast-radius evidence.
        5. **Read source selectively** — inspect code only for findings that already have structural evidence.

        ## Output Contract

        - **Meaningful structural findings** — only issues that matter to the review
        - **Direct dependents** — callers, consumers, implementors, or DI registrations to revisit
        - **Test coverage signals** — covered-by results, missing tests, and suggested follow-up tests
        - **Confidence / follow-up notes** — anything the reviewer should verify manually

        ## Tool Preferences

        Prefer MCP or CLI structural queries before manual review:
        ```bash
        codegraph_query <symbol>                               # MCP structural query
        codegraph query <symbol> --depth 1 --format context    # CLI structural query
        codegraph query <symbol> --kind covered-by             # tests covering symbol
        codegraph query <symbol> --kind depends-on             # direct dependents
        codegraph test-impact <symbol>                         # broader test guidance
        ```
        """;

    /// <summary>
    /// Marker text used to detect if a CodeGraph section has already been appended
    /// to an existing file (Copilot instructions, AGENTS.md).
    /// </summary>
    public const string AppendMarker = "## CodeGraph — Structural Code Intelligence";
}
