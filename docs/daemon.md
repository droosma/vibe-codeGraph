# How to Use `codegraph daemon`

`codegraph daemon` runs a **persistent background server** that keeps the graph loaded in memory, enabling near-instant responses to repeated queries. Without the daemon, each `codegraph query` invocation loads the graph from disk — the daemon eliminates that cold-start overhead.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Start the daemon
codegraph daemon start

# Check whether it's running
codegraph daemon status

# Stop the daemon
codegraph daemon stop
```

---

## CLI Reference

```
codegraph daemon <subcommand> [options]
```

### Subcommands

| Subcommand | Description |
|-----------|-------------|
| `start` | Start the daemon and keep it running in the foreground |
| `stop` | Stop a running daemon by sending a kill signal |
| `status` | Report whether the daemon is running and its PID |

### Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <path>` | Graph directory to serve | `.codegraph` |
| `--help`, `-h` | Show help | |

---

## How It Works

The daemon:

1. Loads the graph from `graph.db` into memory on startup
2. Listens on a named pipe (platform-specific, derived from the graph directory path)
3. Handles query requests from other `codegraph` invocations via the pipe
4. Writes a PID file to the graph directory so `daemon stop` and `daemon status` can find it

The pipe name is printed to stdout on startup:

```
Starting daemon for /home/user/myapp/.codegraph...
Pipe: codegraph-abc123
```

---

## Running the Daemon in the Background

`codegraph daemon start` runs in the **foreground** by design so it can be managed by your process supervisor. To run it in the background:

```bash
# Unix/macOS — detach with nohup
nohup codegraph daemon start > /tmp/codegraph-daemon.log 2>&1 &

# Or use your process manager (systemd, launchd, etc.)
```

### systemd example

```ini
[Unit]
Description=CodeGraph Daemon
After=network.target

[Service]
ExecStart=/usr/local/bin/codegraph daemon start --graph-dir /srv/myapp/.codegraph
Restart=on-failure
WorkingDirectory=/srv/myapp

[Install]
WantedBy=multi-user.target
```

---

## Common Workflows

### Keep the daemon alive during a dev session

```bash
# In one terminal
codegraph daemon start

# In another terminal — queries are served from in-memory graph
codegraph query OrderService --depth 2
codegraph search Repository
codegraph impact PaymentGateway
```

### Check status and restart if down

```bash
codegraph daemon status || codegraph daemon start &
```

### Multi-solution setup

Each graph directory runs its own independent daemon instance.

```bash
codegraph daemon start --graph-dir .codegraph/MyApp.Services
codegraph daemon start --graph-dir .codegraph/MyApp.Api
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Subcommand completed successfully |
| `1` | Error (e.g., no daemon running when `stop`/`status` called) |
