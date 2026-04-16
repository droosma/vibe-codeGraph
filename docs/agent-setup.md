# Agent Setup Guide

CodeGraph integrates with AI coding agents via **MCP** (Model Context Protocol), registering `codegraph_query` as a native tool.

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

## MCP Integration

MCP registers `codegraph_query` as a **native tool** in the agent's tool list — alongside grep, read_file, etc. The agent uses it naturally without any prompt engineering.

`codegraph init` generates the config files automatically:

| Agent | Config File | Format |
|-------|------------|--------|
| VS Code Copilot | `.vscode/mcp.json` | `{ "servers": { "codegraph": { "type": "stdio", "command": "dotnet", "args": ["codegraph", "mcp"], "cwd": "${workspaceFolder}" } } }` |
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
