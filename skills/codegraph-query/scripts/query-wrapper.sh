#!/bin/bash
set -e

# CodeGraph Query Wrapper
# Queries the semantic graph with sensible defaults for LLM agents.
#
# Usage: bash query-wrapper.sh <symbol-pattern> [options...]
# Example: bash query-wrapper.sh OrderService --depth 1 --kind calls

SYMBOL="${1:?Usage: query-wrapper.sh <symbol-pattern> [options...]}"
shift

# Check prerequisites
if ! command -v codegraph &>/dev/null; then
  echo "Error: codegraph is not installed." >&2
  echo "Install with: dotnet tool install -g CodeGraph" >&2
  exit 1
fi

GRAPH_DIR=".codegraph"

# Check if graph exists
if [ ! -d "$GRAPH_DIR" ] || [ -z "$(ls -A "$GRAPH_DIR"/*.json 2>/dev/null)" ]; then
  echo "Error: No graph found in $GRAPH_DIR/" >&2
  echo "Run: codegraph index --solution YourApp.sln" >&2
  exit 1
fi

# Check if graph is stale (older than 7 days)
META="$GRAPH_DIR/meta.json"
if [ -f "$META" ]; then
  META_AGE=$(( ($(date +%s) - $(date -r "$META" +%s)) / 86400 ))
  if [ "$META_AGE" -gt 7 ]; then
    echo "Warning: Graph is $META_AGE days old. Consider re-indexing with --changed-only." >&2
  fi
fi

# Run query with LLM-friendly defaults
exec codegraph query "$SYMBOL" --format context --max-nodes 50 "$@"
