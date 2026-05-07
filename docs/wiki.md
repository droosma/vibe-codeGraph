# How to Use `codegraph wiki`

`codegraph wiki` generates a set of navigable Markdown pages from your indexed code graph. The output is self-contained and can be published to GitHub Wiki, a docs site (e.g., MkDocs, Docusaurus), or committed alongside your code as living architecture documentation.

---

## Quick Start

```bash
# Index your solution first (if you haven't already)
codegraph index --solution MyApp.sln

# Generate wiki pages
codegraph wiki
```

Pages are written to `.codegraph/wiki/` by default.

---

## CLI Reference

```
codegraph wiki [options]
```

| Flag | Description | Default |
|------|-------------|---------|
| `--graph-dir <dir>` | Directory containing graph data | `.codegraph` |
| `--output`, `-o <dir>` | Output directory for wiki pages | `.codegraph/wiki` |
| `--help`, `-h` | Show help | |

### Examples

```bash
# Default — generate .codegraph/wiki/
codegraph wiki

# Publish to a docs directory for MkDocs / Docusaurus
codegraph wiki -o docs/wiki

# Generate a wiki from a previous snapshot
codegraph wiki --graph-dir .codegraph-prev -o wiki-prev/
```

---

## Generated Files

The wiki generator creates the following files:

| File | Content |
|------|---------|
| `INDEX.md` | Graph summary: solution name, commit, total nodes/edges, assembly list, top hub types |
| `assemblies/<AssemblyName>.md` | Per-assembly page: types, methods, DI registrations, test coverage |
| `INTERFACES.md` | All interfaces with their implementing types |
| `DI-WIRING.md` | Dependency-injection registrations and the concrete types they resolve to |

### `INDEX.md`

The index page provides a high-level overview. It links to each assembly page and to the cross-cutting pages (`INTERFACES.md`, `DI-WIRING.md`).

### `assemblies/<name>.md`

Each assembly gets its own page listing:

- All public types (classes, structs, enums, delegates, interfaces)
- Methods, properties, and constructors for each type
- Which types the assembly depends on (cross-boundary edges)
- Which tests cover types in this assembly (`Covers` edges)

### `INTERFACES.md`

A single page listing every interface in the graph and all the classes that implement it, making it easy to understand abstraction boundaries and extension points.

### `DI-WIRING.md`

A table of `ResolvesTo` edges — the DI registrations detected by the DI Pass. Shows which interface maps to which concrete type, and the registration lifetime (`Scoped`, `Transient`, `Singleton`).

---

## Publishing to GitHub Wiki

GitHub Wikis are a git repository. You can push the generated files directly:

```bash
# Clone your wiki repo
git clone https://github.com/<owner>/<repo>.wiki.git wiki-repo

# Generate into the wiki directory
codegraph wiki -o wiki-repo/

# Commit and push
cd wiki-repo
git add .
git commit -m "chore: update architecture wiki"
git push
```

Or automate this in CI:

```yaml
- name: Update GitHub Wiki
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln
    git clone https://github.com/${{ github.repository }}.wiki.git wiki-repo
    codegraph wiki -o wiki-repo/
    cd wiki-repo
    git config user.email "github-actions@github.com"
    git config user.name "github-actions"
    git add .
    git diff --cached --quiet || git commit -m "chore: update architecture wiki [skip ci]"
    git push
```

---

## Publishing to MkDocs or Docusaurus

The generated Markdown files are compatible with most static site generators. Place them in your docs source directory:

```bash
codegraph wiki -o docs/reference/
```

Then reference them in your `mkdocs.yml` or `sidebars.js` as needed.

---

## Multi-Solution Setup

In a [multi-solution setup](configuration.md#multi-solution-configuration), each solution's graph lives in a subdirectory. Generate a wiki for each:

```bash
codegraph wiki --graph-dir .codegraph/Api -o docs/wiki/api
codegraph wiki --graph-dir .codegraph/Workers -o docs/wiki/workers
```

---

## Regenerating Automatically in CI

```yaml
- name: Generate wiki
  run: |
    dotnet tool install -g CodeGraph
    codegraph index --solution MyApp.sln
    codegraph wiki -o artifacts/wiki

- uses: actions/upload-artifact@v4
  with:
    name: codegraph-wiki
    path: artifacts/wiki/
```
