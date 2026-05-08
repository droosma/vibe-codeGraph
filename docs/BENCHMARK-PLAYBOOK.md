# CodeGraph A/B Benchmark Playbook

## Overview

This document describes the A/B benchmark methodology used to validate CodeGraph's effectiveness. The test pits two AI agents against each other — one with CodeGraph available, one without — asking the exact same question about a real codebase, then comparing precision, recall, token cost, and wall time.

This playbook is designed so that any agent (or human) can reproduce the benchmark.

---

## The Mechanism

### Architecture

```
┌──────────────────────────────┐
│        Orchestrator          │
│  (your main agent session)   │
│                              │
│  1. Install & index          │
│  2. Launch Agent A (CG)      │
│  3. Launch Agent B (grep)    │
│  4. Wait for both            │
│  5. Compare results          │
└──────┬───────────┬───────────┘
       │           │
       ▼           ▼
┌─────────────┐ ┌──────────────┐
│  Agent A    │ │  Agent B     │
│  WITH       │ │  WITHOUT     │
│  CodeGraph  │ │  CodeGraph   │
│             │ │              │
│ Tools:      │ │ Tools:       │
│ - codegraph │ │ - grep       │
│ - grep      │ │ - glob       │
│ - glob      │ │ - view       │
│ - view      │ │              │
│             │ │ No codegraph │
│             │ │ access       │
└─────────────┘ └──────────────┘
```

### Key Properties

- **Context isolation**: each agent runs in a separate sub-agent (separate context window) so neither can see the other's work
- **Same question**: both agents receive the **exact same prompt** — only the available tools differ
- **Same codebase**: both point at the same directory with the same indexed graph
- **No coaching**: neither agent is told "use CodeGraph first" or "prefer grep" — they pick their own strategy

---

## Step-by-Step Procedure

### Step 1: Prerequisites

```bash
# Install CodeGraph globally (latest version)
dotnet tool update -g CodeGraph

# Verify installation
codegraph --help
```

### Step 2: Index the Target Codebase

```bash
# Navigate to the target codebase
cd <path-to-codebase>

# Index the solution (this takes 2-10 minutes depending on size)
codegraph index --solution <path-to-sln>

# Verify the graph was created
ls .codegraph/
# Should contain: meta.json, *.json (per assembly), or SQLite db
```

**Important**: Record the indexing time — it's part of the "setup cost" metric. Also record graph size from the indexing output (e.g., "103,265 nodes, 225,538 edges").

### Step 3: Choose the Benchmark Question

The question must be:
- **Answerable by both approaches** — not something that only structural queries can answer, and not pure text search
- **Broad enough to exercise discovery** — the agent must find things, not just look up a known symbol
- **Specific enough to compare results** — should have a finite, comparable answer

#### Tested Questions (from our sessions)

| Question | Type | What it tests |
|----------|------|---------------|
| "Where in this codebase is Office interop used?" | Discovery | Tracing framework usage across layers |
| "What are the differences in implementation between Exact and the other financial systems?" | Comparative | Broad architectural exploration + detail extraction |

#### Recommended Question Categories

- **Discovery**: "Where is X used?" / "What depends on X?"
- **Comparative**: "What are the differences between A and B?"
- **Structural**: "What is the call chain from controller to database for feature X?"
- **Impact**: "If I change X, what would break?"

**Note**: Our benchmarks showed that CodeGraph performs best on **structural** questions and worst on **broad exploratory** questions where grep's raw text search naturally covers more ground. Test multiple question types for a complete picture.

### Step 4: Launch Both Agents

Launch two `general-purpose` sub-agents in **background** mode. The critical difference: Agent A's prompt mentions codegraph tools; Agent B's prompt explicitly says not to use them.

#### Agent A: WITH CodeGraph

```
Agent type: general-purpose
Mode: background
Name: with-codegraph

Prompt:
You are analyzing a .NET codebase at <CODEBASE_PATH>.

This codebase has been indexed with CodeGraph, a Roslyn-powered code graph tool.
The graph is in <CODEBASE_PATH>/.codegraph/

You have access to the `codegraph` CLI tool. Key commands:
- `codegraph query <symbol> --depth N --format compact` — structural relationships
- `codegraph search <term> --top 20` — broad discovery search
- `codegraph list types --top 50 --filter <pattern>` — browse types
- `codegraph compare <symbolA> <symbolB>` — structural comparison
- `codegraph impact <symbol>` — what would break if this changes
- `codegraph explain <symbol>` — natural language explanation
- `codegraph report` — high-level codebase overview

You also have access to grep, glob, and view for reading source files.

Answer the following question thoroughly:

<YOUR_BENCHMARK_QUESTION>

Provide a comprehensive analysis. Be thorough — explore all relevant areas of the codebase.
```

#### Agent B: WITHOUT CodeGraph

```
Agent type: general-purpose
Mode: background
Name: without-codegraph

Prompt:
You are analyzing a .NET codebase at <CODEBASE_PATH>.

You have access to grep, glob, and view tools for searching and reading source files.
You do NOT have access to any code graph or structural analysis tools.

Answer the following question thoroughly:

<YOUR_BENCHMARK_QUESTION>

Provide a comprehensive analysis. Be thorough — explore all relevant areas of the codebase.
```

### Step 5: Wait for Completion

Both agents run in parallel. Wait for both to complete (typically 3-8 minutes each). The orchestrator will be notified when each finishes.

### Step 6: Collect Metrics

For each agent, record:

| Metric | How to measure |
|--------|---------------|
| **Wall time** | Agent start → completion timestamp |
| **Total tool calls** | Count all tool invocations in the agent's response |
| **File reads (view)** | Count `view` tool calls specifically |
| **grep/glob calls** | Count search tool calls |
| **CodeGraph calls** | Count `codegraph` CLI invocations (Agent A only) |
| **Errors/timeouts** | Any tool failures, timeouts, or retries |
| **Response length** | Word/character count of the final answer |

### Step 7: Analyze Results

The orchestrator compares the two agents across these dimensions:

---

## Comparison Framework

### 1. Recall (Completeness)

Build a table of findings — what did each agent discover?

```markdown
| Finding | Agent A (CG) | Agent B (grep) |
|---------|:------------:|:--------------:|
| Found core interface X | ✅ | ✅ |
| Found implementation Y | ✅ | ✅ |
| Found test coverage | ❌ | ✅ |
| Found DI registration | ✅ | ❌ |
| ...                    |    |    |
```

**Key question**: Did one agent find things the other completely missed?

### 2. Precision (Signal-to-Noise)

- Did either agent return irrelevant results?
- Did either agent misidentify something?
- Did either agent make incorrect claims about the code?

### 3. Depth vs Breadth

- **Breadth**: How many distinct areas/layers of the codebase did each agent cover?
- **Depth**: How much implementation detail did each agent extract? (e.g., specific config values, error handling patterns, algorithm details)

CodeGraph typically wins on **breadth** (structural traversal discovers connected components), while grep typically wins on **depth** (reading actual source code reveals implementation details).

### 4. Token Efficiency

Estimate tokens consumed:

```
File reads: count × avg_tokens_per_file (~800 tokens per view)
CodeGraph output: count × avg_tokens_per_query (~200 tokens for compact)
grep output: count × avg_tokens_per_search (~300 tokens)
```

Calculate the ratio: `tokens_without_CG / tokens_with_CG`

### 5. Wall Time

Simple ratio: `time_with_CG / time_without_CG`

Note: Wall time is dominated by **process spawning** for CLI-mode CodeGraph. Each `codegraph` command starts a new .NET process (~200ms) and reloads the graph (~500ms). MCP mode (persistent process) would eliminate this overhead but benchmark agents tend to use CLI.

### 6. Architectural Accuracy

Did the agents correctly identify:
- Design patterns (Strategy, Factory, Repository)?
- Layer boundaries (Controller → Service → Repository)?
- Cross-cutting concerns (logging, auth, caching)?
- Framework-specific patterns (DI registration, middleware pipeline)?

---

## Expected Results by Question Type

Based on 6 benchmark runs across v0.2.0 → v0.4.0:

| Question Type | Token Winner | Wall Time Winner | Recall Winner | Best Use |
|---------------|-------------|-----------------|---------------|----------|
| Broad discovery ("where is X used?") | CG (~2×) | grep (~1.8×) | Tie or CG | CG for token-constrained |
| Comparative ("differences between A and B") | CG (~2×) | grep (~2.3×) | CG (breadth) / grep (depth) | Depends on need |
| Structural ("call chain from X to Y") | CG (expected 3×+) | CG (expected) | CG (expected) | CG strongly preferred |
| Impact ("what breaks if I change X?") | CG (expected 5×+) | CG (expected) | CG (expected) | CG is the only viable tool |

---

## Historical Benchmark Results

### Test Codebase: Exquise (~100K nodes, 95 projects)

#### v0.2.0 — Baseline
- Question: "What are the differences between Exact and Twinfield implementations?"
- CG: 469s, ~50 tools, ~30 file reads
- Grep: 271s, ~55 tools, ~55 file reads
- Token ratio: ~4.2× better with CG
- Finding: CG agent was slower and missed WPF/test layers. Grep agent found more files but with more noise.

#### v0.3.0 — After compact defaults + batch/fuzzy/cache
- Same question
- CG: 388s, ~40 tools, 16 file reads
- Grep: 200s, ~60 tools, 53 file reads
- Token ratio: ~2× better with CG
- Finding: 4 `list types` timeouts forced CG agent to fall back to grep. Compact format reduced output significantly.

#### v0.4.0 — After pagination + search + compare
- Same question
- CG: 399s, ~35 tools, 12 file reads
- Grep: 220s, ~50 tools, 49 file reads
- Token ratio: ~2× better with CG
- Finding: Zero timeouts. `search` used 20 times (the most popular tool). `compare` used. CG found AFAS gap that grep missed. Token savings stable at ~2×.

### Key Trend

```
v0.2.0 → v0.4.0 progression:
  File reads:    30 → 16 → 12  (consistently improving)
  Tool calls:    50 → 40 → 35  (consistently improving)
  Timeouts:       0 →  4 →  0  (regression fixed)
  Token savings: 4× → 2× → 2× (settled at 2×)
  Wall time:     Always ~1.8× slower (process spawning bottleneck)
```

---

## Lessons Learned

### What Affects Results

1. **Question type matters most** — structural questions favor CG, broad exploration slightly favors grep
2. **MCP defaults matter** — switching from `context` to `compact` format was the single biggest improvement (3-5× fewer tokens per query)
3. **Agent strategy matters** — CG agents that start with `codegraph search` (broad discovery) outperform those that start with specific `codegraph query` (requires knowing symbol names upfront)
4. **The `search` tool was transformative** — before it existed (v0.2.0-v0.3.0), CG agents struggled with discovery. After adding it (v0.4.0), it became the most-used tool
5. **Process spawning dominates wall time** — the graph query itself is <100ms, but each CLI invocation adds ~700ms of overhead

### Common Pitfalls

- **Don't coach the agents** — if you tell the CG agent "start with codegraph search", you're measuring the prompt, not the tool
- **Same model for both** — use the same LLM model for both agents to isolate the tool variable
- **Large enough codebase** — on small codebases (<1K types), grep is fast enough that CG has no advantage
- **Pre-index before launching agents** — don't include indexing time in the benchmark (it's a one-time cost)

### What We'd Change

1. **Use MCP mode instead of CLI** — benchmark agents defaulted to CLI commands, which adds process spawning overhead. An MCP-aware benchmark would be fairer
2. **Test structural questions** — all our tests used broad discovery/comparison questions. CG should dominate on "what calls X?" or "trace the dependency chain" questions
3. **Multiple runs** — LLM agents are non-deterministic. Running 3-5 iterations and averaging would give more reliable results

---

## Quick-Start Template

Copy-paste this to run a benchmark:

```
### BENCHMARK SETUP

Target codebase: <PATH>
Solution file: <SLN>
Graph size: <NODES> nodes, <EDGES> edges
CodeGraph version: <VERSION>
Question: "<QUESTION>"

### AGENT A: WITH CODEGRAPH

[Launch general-purpose agent with CodeGraph prompt from Step 4]

### AGENT B: WITHOUT CODEGRAPH

[Launch general-purpose agent with grep-only prompt from Step 4]

### RESULTS

| Metric | Agent A (CG) | Agent B (grep) |
|--------|-------------|----------------|
| Wall time | | |
| Total tool calls | | |
| File reads (view) | | |
| CodeGraph calls | | |
| Errors/timeouts | | |
| Findings count | | |

### FINDINGS COMPARISON

| Finding | CG | grep |
|---------|:--:|:----:|
| | | |

### TOKEN ESTIMATE

| Source | CG tokens | grep tokens |
|--------|-----------|-------------|
| File reads (N × ~800) | | |
| CG output (N × ~200) | | |
| grep output (N × ~300) | | |
| **Total** | | |
| **Ratio** | | |

### VERDICT

[Which approach was better for this question type? Why?]
```

---

## Session Reference

This benchmark methodology was developed and validated during Copilot CLI session `b5415b05-94a5-4ffb-af3a-2cafac25f193`. That session contains the full conversation history including:

- 6 benchmark runs across CodeGraph v0.2.0 → v0.4.0
- Detailed agent outputs and comparison analyses
- Feature implementation driven by benchmark findings
- All issues created (#89–#98) with implementation context

To resume or review, reference this session ID in the Copilot CLI session store.
