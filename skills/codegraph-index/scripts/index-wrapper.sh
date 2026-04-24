#!/bin/bash
set -e

# CodeGraph Index Wrapper
# Indexes a C# codebase to build its semantic graph.
#
# Usage: bash index-wrapper.sh [solution-path] [options...]
# Example: bash index-wrapper.sh MyApp.sln --changed-only

# Check prerequisites
if ! command -v codegraph &>/dev/null; then
  echo "Error: codegraph is not installed." >&2
  echo "Install with: dotnet tool install -g CodeGraph" >&2
  exit 1
fi

if ! command -v dotnet &>/dev/null; then
  echo "Error: .NET SDK is not installed." >&2
  echo "Install from: https://dot.net/download" >&2
  exit 1
fi

# Auto-detect solution if not provided
SOLUTION="${1:-}"
if [ -n "$SOLUTION" ] && [[ "$SOLUTION" == *.sln || "$SOLUTION" == *.slnx ]]; then
  shift
else
  SOLUTION=$(find . -maxdepth 2 \( -name "*.sln" -o -name "*.slnx" \) -print -quit 2>/dev/null)
  if [ -z "$SOLUTION" ]; then
    echo "Error: No .sln or .slnx file found. Provide the path: index-wrapper.sh <path.sln>" >&2
    exit 1
  fi
  echo "Auto-detected solution: $SOLUTION" >&2
fi

# Determine if we should do incremental or full index
GRAPH_DIR=".codegraph"
if [ -d "$GRAPH_DIR" ] && [ -f "$GRAPH_DIR/meta.json" ]; then
  echo "Existing graph found. Running incremental index (--changed-only)..." >&2
  exec codegraph index --solution "$SOLUTION" --changed-only "$@"
else
  echo "No existing graph. Running full index..." >&2
  exec codegraph index --solution "$SOLUTION" "$@"
fi
