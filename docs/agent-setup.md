# Agent Setup Guide

CodeGraph integrates with AI coding agents via a **three-tier approach**, designed to minimize token overhead while supporting every agent type:

1. **Static files** (works everywhere) — `.codegraph/BRIEF.md` and `.codegraph/REPORT.md` provide free orientation with zero tool calls
2. **CLI commands** (recommended) — `codegraph query`, `codegraph list`, etc. have zero schema overhead and work with any terminal-capable agent
3. **MCP** (IDE-only fallback) — for agents like VS Code Copilot chat that cannot run shell commands

> **Prefer CLI over MCP.** CLI saves ~3,500 tokens per session in schema overhead. MCP wraps the same library code — there is no difference in capability.

## Prerequisites

1. **Install CodeGraph**:
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Initialize config and agent skill files**:
   ```bash
   codegraph init
   # → Auto-detects your AI agents and writes skill files for each
   # → Creates codegraph.json, .vscode/mcp.json, .mcp.json (for IDE agents)
   ```

3. **Index your codebase**:
   ```bash
   codegraph index --solution YourApp.sln
   ```

4. **Generate orientation files**:
   ```bash
   codegraph brief    # → .codegraph/BRIEF.md (compact LLM orientation)
   codegraph report   # → .codegraph/REPORT.md (full architectural report)
   ```

5. **Verify the graph works**:
   ```bash
   codegraph query "YourMainType" --depth 1
   ```

---

## CLI Commands (Recommended)

All agents with terminal access should use CLI commands directly. No schema injection needed — the agent instructions teach the commands.

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
codegraph path --from A --to B                            # shortest path
codegraph report                                          # full report
codegraph stats                                           # node/edge counts
```

### Key Flags

| Flag | Purpose |
|------|---------|
| `--depth <n>` | BFS depth (start at 1, increase as needed) |
| `--kind <type>` | Edge filter: `calls`, `inherits`, `implements`, `resolves-to`, `covers`, `depends-on` |
| `--format compact` | Minimal tokens (default when piped) |
| `--json` | Machine-readable JSON output |
| `--budget <tokens>` | Hard cap on output token count |
| `--mode focused` | High-signal edges only (default) |

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success with results |
| `1` | Error (bad args, missing graph, etc.) |
| `2` | Success but no results found |

---

## MCP Integration (IDE-only Agents)

MCP registers `codegraph_query` as a **native tool** in the agent's tool list. Use MCP only for IDE-only agents (VS Code Copilot chat, Cursor inline) that cannot run shell commands directly.

> **Tradeoff:** MCP injects ~3,500 tokens of tool schemas into the agent's context window at session start. CLI commands have zero schema overhead.

`codegraph init` generates the config files automatically:

| Agent | Config File | Format |
|-------|------------|--------|
| VS Code Copilot | `.vscode/mcp.json` | `{ "servers": { "codegraph": { "type": "stdio", "command": "dotnet", "args": ["codegraph", "mcp"], "cwd": "${workspaceFolder}" } } }` |
| Cursor | `.vscode/mcp.json` | Same as VS Code |
| Claude Code | `.mcp.json` | `{ "mcpServers": { "codegraph": { "command": "dotnet", "args": ["codegraph", "mcp"] } } }` |
| APM | `apm.yml` | MCP server declared as a dependency (see below) |

### How It Works

1. Agent starts → reads MCP config → spawns `dotnet codegraph mcp` via stdio
2. `codegraph_query` appears as a tool with typed parameters (`symbol`, `kind`, `depth`, etc.)
3. Agent calls the tool like any other — no shell commands needed
4. Process stays alive for the session, exits when the agent disconnects

### After Setup

For **VS Code Copilot**: restart VS Code (or reload window). You may need to trust the MCP server on first use. Verify by opening the Chat view and checking **Configure Tools** — `codegraph_query` should be listed.

For **Claude Code**: the server is auto-discovered from `.mcp.json`. No restart needed.

For **APM**: run `apm install` to wire the MCP server into all detected clients.

### MCP Tool Reference

The MCP server exposes thirteen tools. Agents call them like any other native tool.

#### `codegraph_query`

Query the graph by symbol pattern.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `symbol` | `string` | ✅ | — | Symbol name or pattern. Supports wildcards (`Order*`, `*Service`) and kind prefix (`type:OrderService`, `method:PlaceOrder`). |
| `depth` | `integer` | | `1` | BFS traversal depth from matched nodes. `0` = matched node only; `1` = direct neighbors. |
| `kind` | `string` | | all edges | Edge type filter. See values below. |
| `mode` | `string` | | `all` | Traversal mode: `focused` (high-signal edges), `structural` (includes containment), `all` (everything). |
| `namespace` | `string` | | all | Namespace filter (wildcards OK, e.g. `MyApp.Services*`). |
| `project` | `string` | | all | Project/assembly filter. |
| `format` | `string` | | `"context"` | Output format: `"context"` (Markdown), `"compact"` (prefix-stripped, 3–5× smaller), `"json"`, or `"text"`. |
| `max_nodes` | `integer` | | `50` | Maximum nodes to return. |
| `include_external` | `boolean` | | `false` | Include external (NuGet) dependency nodes. |
| `confidence` | `string` | | all | Minimum edge confidence: `verified`, `inferred`, or `unresolved` (default: all). |
| `budget` | `integer` | | (none) | Maximum token budget. Output is truncated with a hint when exceeded. |
| `solution` | `string` | | all solutions | Scope query to a specific solution name (multi-solution support). |

**`kind` values:**

| Value | Traverses |
|-------|-----------|
| `calls-to` | Outgoing call edges from the matched symbol |
| `calls-from` | Incoming call edges to the matched symbol |
| `inherits` | Inheritance hierarchy |
| `implements` | Interface implementations |
| `depends-on` | Type-level dependencies |
| `resolves-to` | DI container wiring |
| `covers` | Test → production coverage edges |
| `covered-by` | Production → test coverage edges |
| `references` | Cross-symbol references |
| `overrides` | Method override edges |
| `contains` | Parent/child containment edges |
| `all` | No filter — all edge types |

#### `codegraph_list`

Browse the graph hierarchy. Use before querying to orient yourself in an unfamiliar codebase.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `scope` | `string` | | `"assemblies"` | What to list: `assemblies`, `types`, `interfaces`, or `namespaces`. |
| `assembly` | `string` | | all | Filter by assembly name. |
| `top` | `integer` | | `20` | Max items to return. |

#### `codegraph_summary`

Generate an overview report of the code graph: hub types, assembly boundaries, test coverage, and suggested queries. Takes no parameters. Use to get a structural overview before diving into specific symbols.

#### `codegraph_path`

Find the shortest dependency path between two symbols through calls, inheritance, or other relationships.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `from` | `string` | ✅ | — | Source symbol name or pattern. |
| `to` | `string` | ✅ | — | Target symbol name or pattern. |
| `maxDepth` | `integer` | | `10` | Maximum search depth. |

#### `codegraph_impact`

Reverse-dependency analysis. Find all symbols that depend on a given symbol to assess the blast radius of a change.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `symbol` | `string` | ✅ | — | Symbol name or pattern to analyze. |
| `depth` | `integer` | | `3` | Reverse traversal depth. |

#### `codegraph_explain`

Get a comprehensive view of a single symbol: its type, file location, signature, members, all incoming/outgoing edges, and test coverage. Use to fully understand a symbol before making changes.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `symbol` | `string` | ✅ | — | Symbol name or pattern to explain. |

#### `codegraph_file`

Find all symbols defined in a source file by file path. Use when you know the file but not the individual symbol names — for example, after receiving a list of changed files from `git diff`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `path` | `string` | ✅ | — | File path or partial path. Partial matches are accepted (e.g., `OrderService.cs`). Cross-platform: backslashes and forward slashes both work. |
| `kind` | `string` | | `"type"` | Filter returned symbols by node kind: `type`, `method`, or `all`. |

Output lists each symbol with its kind, fully-qualified ID, signature, and line range.

#### `codegraph_batch`

Query multiple symbols in one call and receive combined, deduplicated results. More efficient than issuing separate `codegraph_query` calls when you need context for several symbols at once.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `symbols` | `string[]` | ✅ | — | List of symbol names or patterns to query. |
| `depth` | `integer` | | `1` | BFS traversal depth applied to every symbol. |
| `format` | `string` | | `"compact"` | Output format: `compact`, `context`, `json`, or `text`. Defaults to `compact` to minimise token usage across the merged result. |
| `mode` | `string` | | `"focused"` | Traversal mode: `focused`, `structural`, or `all`. |

Results from all symbols are merged: nodes and edges are deduplicated so shared types appear only once.

---

## Agent Skill Scaffolding

`codegraph init` goes beyond MCP config files — it also writes **agent skill files** that teach each agent how to use CodeGraph effectively. Skill files are plain Markdown (or shell scripts) committed to your repo so every developer and every agent instance gets the same instructions automatically.

### Auto-Detection

When you run `codegraph init` without `--agent`, it scans the repo root for known agent configuration markers:

| Agent | Detected by |
|-------|-------------|
| Claude Code | `.claude/` directory or `CLAUDE.md` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| OpenCode / Codex | `AGENTS.md` |
| Cursor | `.cursorrules` or `.cursor/rules/` directory |

For each detected agent, the matching skill files are written. The generic `.codegraph/INSTRUCTIONS.md` is **always** written regardless of what agents are detected.

### Files Written Per Agent

| Agent | File(s) | Behavior |
|-------|---------|---------|
| Claude Code | `.claude/skills/codegraph/SKILL.md` | Created (skipped if exists without `--force`) |
| Claude Code | `.claude/skills/codegraph/scripts/query-wrapper.sh` | Created (skipped if exists without `--force`) |
| GitHub Copilot | `.github/copilot-instructions.md` | CodeGraph section appended (idempotent via marker) |
| OpenCode / Codex | `AGENTS.md` | CodeGraph section appended (idempotent via marker) |
| Cursor | `.cursor/rules/codegraph.md` | Created (skipped if exists without `--force`) |
| *(all agents)* | `.codegraph/INSTRUCTIONS.md` | Created (skipped if exists without `--force`) |

### Explicit Agent Selection

Skip auto-detection and install for a specific agent with `--agent`:

```bash
codegraph init --agent claude        # Claude Code only
codegraph init --agent copilot       # GitHub Copilot only
codegraph init --agent opencode      # OpenCode / Codex only
codegraph init --agent cursor        # Cursor only
codegraph init --agent all           # All supported agents
```

### Updating Skill Files

Skill files are versioned with the CLI binary. To update them after a CodeGraph upgrade:

```bash
codegraph init --force
```

`--force` overwrites existing skill files (except appended sections, which are skipped if the marker is already present).

### Committing Skill Files

Commit the generated skill files so everyone on the team and every CI agent gets them automatically:

```bash
git add .claude/ .github/copilot-instructions.md AGENTS.md .cursor/ .codegraph/INSTRUCTIONS.md
git commit -m "Add CodeGraph agent skills"
```

---

## APM (Agent Package Manager) Support

CodeGraph supports [Microsoft APM](https://github.com/microsoft/apm) — a dependency manager for AI agent configuration. APM lets you declare CodeGraph as an MCP dependency in `apm.yml` and have it auto-configured across all supported agent clients.

### Quick Setup with APM

If you have APM installed, add CodeGraph's MCP server to your project:

```bash
apm install --mcp codegraph -- dotnet codegraph mcp
```

Or use the `apm.yml` generated by `codegraph init`, which already includes the MCP server declaration:

```yaml
dependencies:
  mcp:
    - name: codegraph
      registry: false
      transport: stdio
      command: dotnet
      args: ["codegraph", "mcp"]
```

Then run:

```bash
apm install
```

APM wires the CodeGraph MCP server into every detected client (VS Code Copilot, Claude Code, Cursor, Codex, OpenCode) in one step.

### APM Package Primitives

CodeGraph ships with APM primitives in the `.apm/` directory:

| Primitive | File | Purpose |
|-----------|------|---------|
| **Instruction** | `.apm/instructions/codegraph.instructions.md` | Teaches agents when and how to use CodeGraph |
| **Skill** | `.apm/skills/code-explorer/SKILL.md` | Explore C# codebase structure via the semantic graph |

To install CodeGraph's agent primitives from the repository:

```bash
apm install <owner>/<repo>
```

---

## Verifying It Works

After setup, ask your agent a structural question:

> "What calls the PlaceOrder method?"

The agent should invoke the `codegraph_query` tool directly — you'll see a tool call in the agent's output, not a shell command.

---

## Troubleshooting

### "codegraph: command not found"

The `codegraph` tool isn't on your PATH.

```bash
# Check if it's installed
dotnet tool list -g | grep -i codegraph

# Reinstall
dotnet tool install -g CodeGraph

# Ensure .dotnet/tools is on PATH
export PATH="$HOME/.dotnet/tools:$PATH"
```

### "No graph files found in .codegraph/"

You need to index first:

```bash
codegraph index --solution YourApp.sln --output .codegraph
```

### "Graph is stale" warning

The graph was built from a different commit than your current HEAD. Re-index:

```bash
codegraph index --solution YourApp.sln
```

### Agent doesn't use CodeGraph

1. Check that `.vscode/mcp.json` (or `.mcp.json`) exists and is valid JSON.
2. Restart VS Code / reload window — MCP servers are discovered at startup.
3. In VS Code, open Chat → **Configure Tools** and verify `codegraph_query` is listed.
4. If the tool is listed but not used, try prompting explicitly: *"Use the codegraph_query tool to look up OrderService"*.

### Restore failures during indexing

CodeGraph runs `dotnet restore` before indexing. If the restore fails, CodeGraph **does not crash** — it emits a warning to stderr and continues with best-effort Roslyn-based indexing. The resulting graph may be incomplete (missing type resolutions, unresolved references), but a partial graph is still produced.

For the most accurate graph, fix the underlying restore issue first:

```bash
# Diagnose and fix restore errors
dotnet restore YourApp.sln

# Then re-index
codegraph index --solution YourApp.sln
```

Or skip the restore step if you've already restored (e.g., in CI after a prior `dotnet restore`):

```bash
codegraph index --solution YourApp.sln --skip-restore
```

### Large solutions are slow

- Use `--projects` to filter which projects get indexed:
  ```bash
  codegraph index --solution YourApp.sln --projects "MyApp.*"
  ```
- Add benchmarks or generated projects to `excludeProjects` in `codegraph.json`.
- Use `--skip-restore` if you've already restored packages.
