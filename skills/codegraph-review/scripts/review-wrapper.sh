#!/bin/bash
set -e

# CodeGraph Review Wrapper
# Performs impact analysis on changed code using the semantic graph.
#
# Usage: bash review-wrapper.sh [base-ref]
# Example: bash review-wrapper.sh main
#          bash review-wrapper.sh HEAD~1

BASE_REF="${1:-HEAD~1}"

# Check prerequisites
if ! command -v codegraph &>/dev/null; then
  echo "Error: codegraph is not installed." >&2
  echo "Install with: dotnet tool install -g CodeGraph" >&2
  exit 1
fi

GRAPH_DIR=".codegraph"
if [ ! -d "$GRAPH_DIR" ] || [ -z "$(ls -A "$GRAPH_DIR"/*.json 2>/dev/null)" ]; then
  echo "Error: No graph found in $GRAPH_DIR/. Run codegraph index first." >&2
  exit 1
fi

# Find changed C# files
CHANGED_FILES=$(git diff --name-only "$BASE_REF" -- '*.cs' 2>/dev/null)
if [ -z "$CHANGED_FILES" ]; then
  echo "No C# files changed since $BASE_REF." >&2
  exit 0
fi

echo "=== CodeGraph Impact Analysis ===" >&2
echo "Base: $BASE_REF" >&2
echo "Changed files:" >&2
echo "$CHANGED_FILES" | while read -r f; do echo "  - $f" >&2; done
echo "" >&2

# Extract type names from changed files (heuristic: filename without path/extension)
echo "$CHANGED_FILES" | while read -r file; do
  SYMBOL=$(basename "$file" .cs)
  echo "--- Analyzing: $SYMBOL ---" >&2

  # Query dependents
  echo "## Dependents of $SYMBOL"
  codegraph query "$SYMBOL" --depth 1 --format context --max-nodes 30 2>/dev/null || echo "(no results for $SYMBOL)"
  echo ""

  # Query test coverage
  echo "## Test Coverage for $SYMBOL"
  codegraph query "$SYMBOL" --kind covered-by --format context --max-nodes 20 2>/dev/null || echo "(no test coverage found for $SYMBOL)"
  echo ""
done
