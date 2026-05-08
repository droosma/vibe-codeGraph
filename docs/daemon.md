# How to Use `codegraph daemon`

`codegraph daemon` starts a persistent background process that loads the graph once and answers queries over a named pipe. This eliminates the ~700 ms cold-start penalty (process spawn + graph load) that affects every CLI invocation.

Use the daemon when query wall time matters — e.g., in tight agent loops, IDE integrations, or benchmarking scenarios where many queries run in quick succession.

---

## Quick Start

```bash
# Index the codebase first
codegraph index --solution MyApp.sln

# Start the daemon (blocks; run in background or a separate terminal)
codegraph daemon start &

# Check that it's running
codegraph daemon status

# Stop it
codegraph daemon stop
```

---

## CLI Reference

```
codegraph daemon <start|stop|status> [--graph-dir <dir>]
```

| Sub-command | Description |
|-------------|-------------|
| `start` | Start the daemon for the given graph directory |
| `stop` | Stop the running daemon (sends SIGTERM) |
| `status` | Print whether the daemon is running and its PID |

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory the daemon loads | `.codegraph` |

---

## How the Daemon Works

- On `start`, the daemon loads the graph from `--graph-dir` into memory and listens on a **named pipe** (`codegraph-<hash>.pipe` derived from the graph directory path).
- It writes a PID file (`.codegraph/daemon.pid` or equivalent) so `stop` and `status` can locate it.
- On `stop`, the PID file is read, the process is killed, and the PID file is removed.
- On `status`, the PID file is checked and the process is verified to still be alive.

The daemon does **not** auto-reload when the graph changes. After `codegraph index`, restart the daemon to pick up the new graph.

---

## Named Pipe

On `start`, the pipe name is printed:

```
Starting daemon for /home/user/myapp/.codegraph...
Pipe: codegraph-a3f7b291
```

Agent integrations and scripts can connect to this pipe directly for low-latency queries. The MCP server (`codegraph mcp`) will automatically use the daemon pipe when the daemon is running for the same graph directory.

---

## Typical Workflow

```bash
# Start once at the beginning of a coding session
codegraph daemon start --graph-dir .codegraph &

# Run many queries — no cold start
codegraph query OrderService --depth 2
codegraph query IOrderService --kind implements
codegraph test-impact PaymentService

# After re-indexing, restart the daemon
codegraph index --solution MyApp.sln
codegraph daemon stop
codegraph daemon start &
```

---

## Checking Status

```bash
codegraph daemon status
# Daemon is running (PID 12345).
# Pipe: codegraph-a3f7b291
```

```bash
codegraph daemon status
# No daemon is running.
```

---

## Process Management

The daemon runs as a normal foreground process in the terminal where `daemon start` was invoked. To run it persistently:

```bash
# Background with nohup
nohup codegraph daemon start > .codegraph/daemon.log 2>&1 &
```

The daemon exits cleanly when `codegraph daemon stop` is called, or when the process is sent SIGTERM/SIGINT.

---

## When to Use vs. MCP Mode

| Scenario | Recommendation |
|----------|----------------|
| Agent connected via MCP | Use MCP mode (`codegraph mcp`) — it's already persistent |
| Many CLI queries in a script | Use `daemon start` |
| Benchmarking query performance | Use `daemon start` to isolate graph-load overhead |
| One-off queries | No daemon needed — cold-start cost is acceptable |

---

## See Also

- [`codegraph mcp`](mcp.md) — MCP server mode (already persistent, preferred for agents)
- [`codegraph benchmark`](benchmark.md) — measure query performance with or without the daemon
- [`codegraph index`](../README.md#codegraph-index) — (re)generate the graph; restart daemon afterward
