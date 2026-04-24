namespace CodeGraph.Indexer.Init;

/// <summary>
/// Detects which AI agent configurations exist in a repository directory.
/// </summary>
internal static class AgentDetector
{
    /// <summary>
    /// Scans the given directory for known agent configuration files and returns
    /// which agents are detected along with the matched file/directory path.
    /// </summary>
    public static List<AgentDetection> Detect(string repoRoot)
    {
        var results = new List<AgentDetection>();

        // Claude Code: .claude/ directory or CLAUDE.md
        if (Directory.Exists(Path.Combine(repoRoot, ".claude")))
            results.Add(new AgentDetection(AgentKind.Claude, ".claude/ directory"));
        else if (File.Exists(Path.Combine(repoRoot, "CLAUDE.md")))
            results.Add(new AgentDetection(AgentKind.Claude, "CLAUDE.md"));

        // GitHub Copilot: .github/copilot-instructions.md
        if (File.Exists(Path.Combine(repoRoot, ".github", "copilot-instructions.md")))
            results.Add(new AgentDetection(AgentKind.Copilot, ".github/copilot-instructions.md"));

        // OpenCode / Codex: AGENTS.md
        if (File.Exists(Path.Combine(repoRoot, "AGENTS.md")))
            results.Add(new AgentDetection(AgentKind.OpenCode, "AGENTS.md"));

        // Cursor: .cursorrules or .cursor/rules/ directory
        if (File.Exists(Path.Combine(repoRoot, ".cursorrules")))
            results.Add(new AgentDetection(AgentKind.Cursor, ".cursorrules"));
        else if (Directory.Exists(Path.Combine(repoRoot, ".cursor", "rules")))
            results.Add(new AgentDetection(AgentKind.Cursor, ".cursor/rules/ directory"));

        return results;
    }
}

/// <summary>
/// Represents a detected agent configuration in the repository.
/// </summary>
internal sealed record AgentDetection(AgentKind Agent, string MatchedPath);
