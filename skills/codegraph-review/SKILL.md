---
name: codegraph-review
description: Use CodeGraph's semantic graph for code review and impact analysis. Use when asked to "review changes", "check impact", "what will this break", or "analyze dependencies of changed code".
metadata:
  author: codegraph-contributors
  version: "1.0.0"
  argument-hint: <symbol-or-file-pattern>
---

# CodeGraph Review

Use the semantic graph to perform impact analysis during code review — find what depends on changed code, trace blast radius, and identify affected tests.

## When to Use

Use this skill when:
- Reviewing a PR and need to understand impact of changes
- Checking what other code depends on a modified type or method
- Finding which tests cover changed code
- Assessing the blast radius of a refactor
- Verifying that a breaking change is safe
- Understanding if a change affects DI wiring

**Trigger phrases:** "review this change", "what will this break", "check impact", "blast radius", "affected tests", "who depends on this", "is this change safe"

## Prerequisites

1. **Install CodeGraph** (one-time):
   ```bash
   dotnet tool install -g CodeGraph
   ```

2. **Index the codebase** (must be up-to-date):
   ```bash
   codegraph index --solution YourApp.sln --changed-only
   ```

3. **Graph must be current** — re-index before review if the graph is stale.

## How It Works

1. Identify changed symbols (types, methods) from the diff
2. Query the graph for each changed symbol to find dependents
3. Check test coverage of changed code
4. Report the impact analysis

## Review Workflow

### Step 1: Identify Changed Symbols

From a git diff or PR, extract the changed type and method names:

```bash
# List changed C# files
git diff --name-only HEAD~1 -- '*.cs'

# Or for a PR
git diff --name-only main...HEAD -- '*.cs'
```

### Step 2: Find Dependents of Changed Code

For each changed type or method, query incoming relationships:

```bash
# What calls the changed method?
codegraph query PlaceOrder --kind calls-from --depth 1

# What depends on the changed type?
codegraph query OrderService --kind depends-on --depth 1

# What implements the changed interface?
codegraph query IOrderService --kind implements --depth 1

# What resolves to the changed type via DI?
codegraph query OrderService --kind resolves-to --depth 1
```

### Step 3: Check Test Coverage

```bash
# Find tests that cover the changed type
codegraph query OrderService --kind covered-by

# Find tests that cover a specific method
codegraph query PlaceOrder --kind covered-by
```

If no tests cover the changed code, flag this in the review.

### Step 4: Assess Blast Radius

For broader impact analysis, increase depth:

```bash
# Two levels of dependents (direct + transitive)
codegraph query OrderService --depth 2 --max-nodes 30

# Full dependency chain (use sparingly)
codegraph query OrderService --depth 3 --max-nodes 50
```

### Step 5: Check DI Impact

If the change involves an interface or service registration:

```bash
# What resolves to this interface?
codegraph query IOrderService --kind resolves-to

# What depends on this interface?
codegraph query IOrderService --kind depends-on --depth 1
```

## Review Checklist

When performing impact analysis, report:

1. **Changed symbols** — list of types/methods that were modified
2. **Direct dependents** — code that directly calls or depends on changed symbols
3. **Test coverage** — tests that cover the changed code (and gaps)
4. **DI impact** — if interfaces or registrations changed, what's affected
5. **Blast radius** — estimated scope of impact (low / medium / high)
6. **Recommendations** — suggest additional tests or review areas

## Output Format

Use `--format context` for human-readable impact reports:

```bash
codegraph query OrderService --depth 1 --format context
```

Use `--format json` when you need to process multiple queries programmatically:

```bash
codegraph query OrderService --depth 1 --format json
```

## Fallback Behavior

1. **Graph is stale** → Re-index: `codegraph index --solution <path.sln|path.slnx> --changed-only`
2. **Changed symbol not in graph** → It may be new code; review manually
3. **No test coverage found** → Flag as a review finding — suggest adding tests
4. **Too many dependents** → Focus on direct dependents (`--depth 1`) and critical paths
5. **CodeGraph not installed** → Run `dotnet tool install -g CodeGraph`

## Best Practices

1. **Always re-index before review** — run `--changed-only` to ensure the graph reflects current code
2. **Focus on public API changes** — internal changes have smaller blast radius
3. **Check interface changes carefully** — they affect all implementors and DI consumers
4. **Flag untested changes** — use `--kind covered-by` to verify test coverage
5. **Keep queries focused** — depth 1-2 is usually sufficient for review
6. **Compare before/after** — if the graph was indexed before the change, query both states
