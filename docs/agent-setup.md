# Agent Setup Guide

CodeGraph integrates with AI coding agents via **MCP** (Model Context Protocol), registering `codegraph_query` as a native tool.

## Prerequisites

1. **Install CodeGraph**:
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Initialize config, MCP registration, and agent skill files**:
   ```bash
   codegraph init
   # → Creates codegraph.json, .vscode/mcp.json, .mcp.json, apm.yml
   # → Auto-detects your AI agents and writes skill files for each
   ```

3. **Index your codebase**:
   ```bash
   codegraph index --solution YourApp.sln
   ```

4. **Verify the graph works**:
   ```bash
   codegraph query "YourMainType" --depth 1
   ```

---

## MCP Integration

MCP registers `codegraph_query` as a **native tool** in the agent's tool list — alongside grep, read_file, etc. The agent uses it naturally without any prompt engineering.

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

### Build failures during indexing

CodeGraph runs `dotnet build` before indexing. If your solution doesn't build:

```bash
# Fix build errors first
dotnet build YourApp.sln

# Or skip the build if you've already built
codegraph index --solution YourApp.sln --skip-build
```

### Large solutions are slow

- Use `--projects` to filter which projects get indexed:
  ```bash
  codegraph index --solution YourApp.sln --projects "MyApp.*"
  ```
- Add benchmarks or generated projects to `excludeProjects` in `codegraph.json`.
- Use `--skip-build` if you've already built the solution.
