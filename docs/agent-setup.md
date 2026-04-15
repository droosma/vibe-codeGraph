# Agent Setup Guide

CodeGraph integrates with AI coding agents in two ways: **MCP** (recommended) and **skill files** (fallback).

## Prerequisites

1. **Install CodeGraph**:
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Initialize config + MCP registration**:
   ```bash
   codegraph init
   # → Creates codegraph.json, .vscode/mcp.json, .mcp.json
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

## MCP Integration (Recommended)

MCP registers `codegraph_query` as a **native tool** in the agent's tool list — alongside grep, read_file, etc. The agent uses it naturally without any prompt engineering.

`codegraph init` generates the config files automatically:

| Agent | Config File | Format |
|-------|------------|--------|
| VS Code Copilot | `.vscode/mcp.json` | `{ "servers": { "codegraph": { "command": "dotnet", "args": ["codegraph", "mcp"] } } }` |
| Cursor | `.vscode/mcp.json` | Same as VS Code |
| Claude Code | `.mcp.json` | `{ "mcpServers": { "codegraph": { "command": "dotnet", "args": ["codegraph", "mcp"] } } }` |

### How It Works

1. Agent starts → reads MCP config → spawns `dotnet codegraph mcp` via stdio
2. `codegraph_query` appears as a tool with typed parameters (`symbol`, `kind`, `depth`, etc.)
3. Agent calls the tool like any other — no shell commands needed
4. Process stays alive for the session, exits when the agent disconnects

### After Setup

For **VS Code Copilot**: restart VS Code (or reload window). You may need to trust the MCP server on first use. Verify by opening the Chat view and checking **Configure Tools** — `codegraph_query` should be listed.

For **Claude Code**: the server is auto-discovered from `.mcp.json`. No restart needed.

---

## Skill Files (Fallback)

For agents that don't support MCP, skill files teach the agent to run `codegraph query` via shell commands. These are text instructions that the agent reads as context.

### Quick Install

```bash
bash skills/_shared/install.sh /path/to/your/repo
```

Select which agent(s) to configure when prompted. The installer adds `.codegraph/` to `.gitignore`.

### Per-Agent Setup

#### Claude Code

| File | Location |
|------|----------|
| `SKILL.md` | `.claude/skills/codegraph/SKILL.md` |
| `query-wrapper.sh` | `skills/claude/query-wrapper.sh` |

```bash
mkdir -p .claude/skills/codegraph
cp skills/claude/SKILL.md .claude/skills/codegraph/SKILL.md
```

#### OpenCode

| File | Location |
|------|----------|
| `AGENTS.md` | `AGENTS.md` (repo root) |
| `query-wrapper.sh` | `skills/opencode/query-wrapper.sh` |

```bash
cp skills/opencode/AGENTS.md AGENTS.md
```

#### GitHub Copilot CLI / VS Code Copilot

| File | Location |
|------|----------|
| `skill.md` | `.github/skills/codegraph/skill.md` |
| `query-wrapper.sh` | `skills/copilot-cli/query-wrapper.sh` |

```bash
mkdir -p .github/skills/codegraph
cp skills/copilot-cli/.github/skills/codegraph/skill.md .github/skills/codegraph/skill.md
```

> **Note:** Use `.github/skills/codegraph/skill.md` — not `.github/copilot-instructions.md`. Skill files are scoped and avoid polluting the global instruction context.

### Commit Skill Files

```bash
git add .github/skills/ .claude/ AGENTS.md
git commit -m "Add CodeGraph agent skill files"
```

> Commit the skill files, but **not** `.codegraph/` data — it should be regenerated per-machine.

---

## Verifying It Works

After setup, ask your agent a structural question:

> "What calls the PlaceOrder method?"

**With MCP:** The agent should invoke the `codegraph_query` tool directly.

**With skill files:** The agent should run `codegraph query PlaceOrder --kind calls-to` instead of `grep -r "PlaceOrder"`.

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

### Agent doesn't use CodeGraph (MCP)

1. Check that `.vscode/mcp.json` (or `.mcp.json`) exists and is valid JSON.
2. Restart VS Code / reload window — MCP servers are discovered at startup.
3. In VS Code, open Chat → **Configure Tools** and verify `codegraph_query` is listed.
4. If the tool is listed but not used, try prompting explicitly: *"Use the codegraph_query tool to look up OrderService"*.

### Agent doesn't use CodeGraph (Skill Files)

1. Verify the skill file is in the correct location (see tables above).
2. Check that the file is committed and on the current branch.
3. Try prompting the agent explicitly: *"Use codegraph query to look up OrderService"*.
4. For Claude, ensure the `.claude/` directory is not in `.gitignore`.

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
