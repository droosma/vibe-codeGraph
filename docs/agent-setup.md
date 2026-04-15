# Agent Setup Guide

CodeGraph provides skill files that teach AI coding agents how to query the code graph. This guide covers setup for each supported agent.

## Prerequisites

1. **Install CodeGraph** (once published):
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Index your codebase**:
   ```bash
   codegraph index --solution YourApp.sln
   ```

3. **Verify the graph works**:
   ```bash
   codegraph query --graph-dir .codegraph/ "YourMainType" --depth 1
   ```

---

## Quick Install (All Agents)

The interactive installer copies skill files to the correct locations:

```bash
bash skills/_shared/install.sh /path/to/your/repo
```

Select which agent(s) to configure when prompted (1–5). The installer also adds `.codegraph/` to your `.gitignore`.

After running the installer:
```bash
git add skills/ .claude/ .github/ AGENTS.md
git commit -m "Add CodeGraph agent skill files"
```

> **Note:** Commit the skill files, but **not** `.codegraph/` data — it should be regenerated per-machine.

---

## Claude Code

### What Gets Installed

| File | Location |
|------|----------|
| `SKILL.md` | `.claude/skills/codegraph/SKILL.md` |
| `query-wrapper.sh` | `skills/claude/query-wrapper.sh` |

### Manual Setup

```bash
mkdir -p .claude/skills/codegraph
cp skills/claude/SKILL.md .claude/skills/codegraph/SKILL.md
mkdir -p skills/claude
cp skills/claude/query-wrapper.sh skills/claude/query-wrapper.sh
chmod +x skills/claude/query-wrapper.sh
```

### How It Works

Claude Code reads `.claude/skills/` for capability files. The `SKILL.md` teaches Claude how to use `codegraph query` to understand your codebase structure. The `query-wrapper.sh` provides a shell interface for running queries.

### Usage

Once installed, Claude will automatically use CodeGraph when exploring your codebase. You can also prompt it directly:

> "Use codegraph to show me the dependencies of OrderService"

---

## OpenCode

### What Gets Installed

| File | Location |
|------|----------|
| `AGENTS.md` | `AGENTS.md` (repo root) |
| `query-wrapper.sh` | `skills/opencode/query-wrapper.sh` |

### Manual Setup

```bash
cp skills/opencode/AGENTS.md AGENTS.md
mkdir -p skills/opencode
cp skills/opencode/query-wrapper.sh skills/opencode/query-wrapper.sh
chmod +x skills/opencode/query-wrapper.sh
```

### How It Works

OpenCode reads `AGENTS.md` at the repo root for agent instructions. The file teaches the agent how to query the code graph for structural understanding.

---

## GitHub Copilot CLI

### What Gets Installed

| File | Location |
|------|----------|
| `copilot-instructions.md` | `.github/copilot-instructions.md` |
| `query-wrapper.sh` | `skills/copilot-cli/query-wrapper.sh` |

### Manual Setup

```bash
mkdir -p .github
cp skills/copilot-cli/.github/copilot-instructions.md .github/copilot-instructions.md
mkdir -p skills/copilot-cli
cp skills/copilot-cli/query-wrapper.sh skills/copilot-cli/query-wrapper.sh
chmod +x skills/copilot-cli/query-wrapper.sh
```

### How It Works

GitHub Copilot CLI reads `.github/copilot-instructions.md` for repo-specific instructions. The file teaches Copilot to use CodeGraph queries when analyzing code structure.

---

## VS Code Copilot

### What Gets Installed

| File | Location |
|------|----------|
| `copilot-instructions.md` | `.github/copilot-instructions.md` |
| `query-wrapper.sh` | `skills/vscode/query-wrapper.sh` |

### Manual Setup

```bash
mkdir -p .github
cp skills/vscode/.github/copilot-instructions.md .github/copilot-instructions.md
mkdir -p skills/vscode
cp skills/vscode/query-wrapper.sh skills/vscode/query-wrapper.sh
chmod +x skills/vscode/query-wrapper.sh
```

### How It Works

VS Code Copilot also reads `.github/copilot-instructions.md`. If you're using both Copilot CLI and VS Code, they share the same instruction file.

---

## Shared Instruction File Conflict

Both **Copilot CLI** and **VS Code Copilot** use `.github/copilot-instructions.md`. If you install both:

- The installer warns you and asks whether to overwrite.
- In practice, the instructions are very similar — either version works for both tools.
- If you need custom per-tool instructions, you can merge both sets into a single file.

---

## Verifying It Works

After setup, ask your agent a structural question:

> "What calls the PlaceOrder method?"

The agent should run `codegraph query PlaceOrder --kind calls-to` instead of `grep -r "PlaceOrder"`.

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

### Agent doesn't seem to use CodeGraph

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
