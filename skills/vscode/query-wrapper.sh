#!/bin/bash
# CodeGraph Query Wrapper for VS Code Copilot
# Detects codegraph installation and graph location, runs query
# Designed to be run from the VS Code integrated terminal

set -euo pipefail

GRAPH_DIR="${CODEGRAPH_DIR:-.codegraph}"

# Check if codegraph is installed
if ! command -v codegraph &> /dev/null; then
    echo "ERROR: codegraph is not installed."
    echo "Install with: dotnet tool install -g CodeGraph"
    exit 1
fi

# Check if graph exists
if [ ! -d "$GRAPH_DIR" ] || [ ! -f "$GRAPH_DIR/meta.json" ]; then
    echo "WARNING: No code graph found at $GRAPH_DIR"
    echo "Run 'codegraph index --solution <path.sln> --output $GRAPH_DIR' to generate."
    exit 1
fi

# Check graph staleness
if command -v git &> /dev/null && [ -d .git ]; then
    HEAD_SHA=$(git rev-parse HEAD 2>/dev/null || true)
    if [ -n "$HEAD_SHA" ] && [ -f "$GRAPH_DIR/meta.json" ]; then
        GRAPH_SHA=$(grep -o '"commitSha"[[:space:]]*:[[:space:]]*"[^"]*"' "$GRAPH_DIR/meta.json" 2>/dev/null | head -1 | sed 's/.*"commitSha"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' || true)
        if [ -n "$GRAPH_SHA" ] && [ "$HEAD_SHA" != "$GRAPH_SHA" ]; then
            echo "NOTE: Graph indexed at ${GRAPH_SHA:0:8}, HEAD is ${HEAD_SHA:0:8}. Results may be stale."
            echo "---"
        fi
    fi
fi

# Pass all arguments through to codegraph query
codegraph query --graph-dir "$GRAPH_DIR" "$@"
