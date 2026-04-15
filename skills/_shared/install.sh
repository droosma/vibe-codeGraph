#!/bin/bash
# CodeGraph Skill File Installer
# Copies agent instruction files to the correct locations in a target repo

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILLS_DIR="$(dirname "$SCRIPT_DIR")"
REPO_ROOT="${1:-.}"

# Resolve to absolute path
REPO_ROOT="$(cd "$REPO_ROOT" && pwd)"

echo "CodeGraph Skill Installer"
echo "========================="
echo ""
echo "Target repository: $REPO_ROOT"
echo ""
echo "Which agent(s) do you want to configure?"
echo "  1) Claude (SKILL.md for .claude/ directory)"
echo "  2) OpenCode (AGENTS.md)"
echo "  3) Copilot CLI (.github/copilot-instructions.md)"
echo "  4) VS Code Copilot (.github/copilot-instructions.md)"
echo "  5) All of the above"
echo ""
read -p "Select (1-5): " choice

install_claude() {
    echo "Installing Claude skill files..."
    local dest="$REPO_ROOT/.claude/skills/codegraph"
    mkdir -p "$dest"
    cp "$SKILLS_DIR/claude/SKILL.md" "$dest/SKILL.md"
    cp "$SKILLS_DIR/claude/query-wrapper.sh" "$REPO_ROOT/skills/claude/query-wrapper.sh" 2>/dev/null || {
        mkdir -p "$REPO_ROOT/skills/claude"
        cp "$SKILLS_DIR/claude/query-wrapper.sh" "$REPO_ROOT/skills/claude/query-wrapper.sh"
    }
    chmod +x "$REPO_ROOT/skills/claude/query-wrapper.sh"
    echo "  ✓ Copied SKILL.md to $dest/"
    echo "  ✓ Copied query-wrapper.sh to skills/claude/"
}

install_opencode() {
    echo "Installing OpenCode skill files..."
    local dest="$REPO_ROOT"
    cp "$SKILLS_DIR/opencode/AGENTS.md" "$dest/AGENTS.md"
    mkdir -p "$REPO_ROOT/skills/opencode"
    cp "$SKILLS_DIR/opencode/query-wrapper.sh" "$REPO_ROOT/skills/opencode/query-wrapper.sh"
    chmod +x "$REPO_ROOT/skills/opencode/query-wrapper.sh"
    echo "  ✓ Copied AGENTS.md to $dest/"
    echo "  ✓ Copied query-wrapper.sh to skills/opencode/"
}

install_copilot_cli() {
    echo "Installing Copilot CLI skill files..."
    local dest="$REPO_ROOT/.github"
    mkdir -p "$dest"
    if [ -f "$dest/copilot-instructions.md" ]; then
        echo ""
        echo "  WARNING: $dest/copilot-instructions.md already exists."
        read -p "  Overwrite? (y/n): " overwrite
        if [ "$overwrite" != "y" ]; then
            echo "  Skipped copilot-instructions.md"
            return
        fi
    fi
    cp "$SKILLS_DIR/copilot-cli/.github/copilot-instructions.md" "$dest/copilot-instructions.md"
    mkdir -p "$REPO_ROOT/skills/copilot-cli"
    cp "$SKILLS_DIR/copilot-cli/query-wrapper.sh" "$REPO_ROOT/skills/copilot-cli/query-wrapper.sh"
    chmod +x "$REPO_ROOT/skills/copilot-cli/query-wrapper.sh"
    echo "  ✓ Copied copilot-instructions.md to $dest/"
    echo "  ✓ Copied query-wrapper.sh to skills/copilot-cli/"
}

install_vscode() {
    echo "Installing VS Code Copilot skill files..."
    local dest="$REPO_ROOT/.github"
    mkdir -p "$dest"
    # VS Code can use .github/copilot-instructions.md — check for conflict with Copilot CLI
    local target="$dest/copilot-instructions.md"
    if [ -f "$target" ]; then
        echo ""
        echo "  NOTE: $target already exists (possibly from Copilot CLI install)."
        echo "  VS Code also reads .github/copilot-instructions.md."
        read -p "  Overwrite with VS Code version? (y/n): " overwrite
        if [ "$overwrite" != "y" ]; then
            echo "  Skipped copilot-instructions.md (existing file preserved)"
            # Still copy the wrapper
            mkdir -p "$REPO_ROOT/skills/vscode"
            cp "$SKILLS_DIR/vscode/query-wrapper.sh" "$REPO_ROOT/skills/vscode/query-wrapper.sh"
            chmod +x "$REPO_ROOT/skills/vscode/query-wrapper.sh"
            echo "  ✓ Copied query-wrapper.sh to skills/vscode/"
            return
        fi
    fi
    cp "$SKILLS_DIR/vscode/.github/copilot-instructions.md" "$target"
    mkdir -p "$REPO_ROOT/skills/vscode"
    cp "$SKILLS_DIR/vscode/query-wrapper.sh" "$REPO_ROOT/skills/vscode/query-wrapper.sh"
    chmod +x "$REPO_ROOT/skills/vscode/query-wrapper.sh"
    echo "  ✓ Copied copilot-instructions.md to $dest/"
    echo "  ✓ Copied query-wrapper.sh to skills/vscode/"
}

setup_gitignore() {
    local gitignore="$REPO_ROOT/.gitignore"
    if [ -f "$gitignore" ]; then
        if ! grep -q "^\.codegraph/" "$gitignore" 2>/dev/null; then
            echo "" >> "$gitignore"
            echo "# CodeGraph index data" >> "$gitignore"
            echo ".codegraph/" >> "$gitignore"
            echo "  ✓ Added .codegraph/ to .gitignore"
        else
            echo "  ✓ .codegraph/ already in .gitignore"
        fi
    else
        echo "# CodeGraph index data" > "$gitignore"
        echo ".codegraph/" >> "$gitignore"
        echo "  ✓ Created .gitignore with .codegraph/"
    fi
}

case "$choice" in
    1) install_claude ;;
    2) install_opencode ;;
    3) install_copilot_cli ;;
    4) install_vscode ;;
    5)
        install_claude
        echo ""
        install_opencode
        echo ""
        install_copilot_cli
        echo ""
        install_vscode
        ;;
    *)
        echo "Invalid selection. Exiting."
        exit 1
        ;;
esac

echo ""
setup_gitignore

echo ""
echo "========================="
echo "Installation complete!"
echo ""
echo "Next steps:"
echo "  1. Generate the code graph:"
echo "     codegraph index --solution <your-solution.sln> --output .codegraph/"
echo ""
echo "  2. Verify the graph:"
echo "     codegraph query --graph-dir .codegraph/ '<any-type-name>' --depth 1"
echo ""
echo "  3. Commit the skill files (but NOT .codegraph/ data):"
echo "     git add skills/ .claude/ .github/ AGENTS.md"
echo "     git commit -m 'Add CodeGraph agent skill files'"
echo ""
