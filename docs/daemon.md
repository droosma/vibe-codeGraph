# How to Use `codegraph daemon`

`codegraph daemon` runs a persistent background process that keeps your code graph loaded in memory, serving queries over a named pipe. It eliminates the graph-loading latency on every query — particularly valuable in interactive agent sessions where a developer or AI assistant fires many queries in rapid succession.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Start the daemon
codegraph daemon start

# The daemon is now running in the background.
# Subsequent codegraph query/search/list commands connect to it automatically.

# Check daemon status
codegraph daemon status

# Stop the daemon when done
codegraph daemon stop
```

---

## CLI Reference

```
codegraph daemon <start|stop|status> [options]
```

### Sub-commands

| Sub-command | Description |
|-------------|-------------|
| `start` | Start the background daemon and keep it running |
| `stop` | Stop the running daemon |
| `status` | Print daemon status (running / not running, PID, pipe name) |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory the daemon watches | `.codegraph` |
| `--help`, `-h` | Show help | |

### Examples

```bash
codegraph daemon start                             # Start for .codegraph/
codegraph daemon start --graph-dir .codegraph/Api # Start for a sub-graph
codegraph daemon status                            # Is it running?
codegraph daemon stop                              # Shut it down
```

---

## Understanding the Output

### `daemon start`

```
Starting daemon for /path/to/project/.codegraph...
Pipe: \\.\pipe\codegraph-<hash>
```

The process stays in the foreground until stopped. Run it in a dedicated terminal or as a background job (`codegraph daemon start &` on Linux/macOS).

### `daemon status`

```
Daemon is running (PID 12345).
Pipe: \\.\pipe\codegraph-<hash>
```

or

```
No daemon is running.
```

### `daemon stop`

```
Daemon (PID 12345) stopped.
```

---

## How It Works

The daemon loads the graph from `.codegraph/` into memory on startup and listens on a named pipe for query requests. When a client (another `codegraph` invocation) sends a request, the daemon responds directly without re-reading disk files. This makes subsequent queries significantly faster than cold-start invocations.

A PID file is written to the graph directory on start and deleted on stop, allowing `daemon status` and `daemon stop` to locate the running process.

---

## Common Workflows

### Interactive Agent Session

```bash
# Start the daemon at the beginning of your session
codegraph daemon start &

# Your AI agent can now fire many rapid queries without load overhead
codegraph query OrderService --depth 2
codegraph search PaymentGateway
codegraph list types --assembly MyApp.Core

# End of session
codegraph daemon stop
```

### VS Code / Editor Integration

Add a task to your `.vscode/tasks.json` to start the daemon when opening the workspace:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Start CodeGraph daemon",
      "type": "shell",
      "command": "codegraph daemon start",
      "isBackground": true,
      "problemMatcher": []
    }
  ]
}
```

---

## See Also

- [`codegraph query`](../README.md#codegraph-query) — query the graph (connects to daemon automatically when running)
- [`codegraph mcp`](mcp.md) — MCP server mode for AI agent integration
- [`codegraph index`](../README.md#codegraph-index) — rebuild the graph (restart the daemon afterward to pick up changes)
