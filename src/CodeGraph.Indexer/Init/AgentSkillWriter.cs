namespace CodeGraph.Indexer.Init;

/// <summary>
/// Result of a single file write operation during agent skill scaffolding.
/// </summary>
internal sealed record WriteResult(string RelativePath, WriteAction Action);

internal enum WriteAction
{
    Created,
    Appended,
    Skipped,
    AlreadyPresent
}

/// <summary>
/// Writes agent skill/instruction files into a repository directory.
/// </summary>
internal static class AgentSkillWriter
{
    /// <summary>
    /// Writes skill files for the specified agent kinds. Returns a list of actions taken.
    /// </summary>
    public static async Task<List<WriteResult>> WriteAsync(
        string repoRoot, IEnumerable<AgentKind> agents, bool force)
    {
        var results = new List<WriteResult>();

        foreach (var agent in agents.Distinct())
        {
            switch (agent)
            {
                case AgentKind.Claude:
                    results.AddRange(await WriteClaudeFilesAsync(repoRoot, force));
                    break;
                case AgentKind.Copilot:
                    results.Add(await AppendCopilotInstructionsAsync(repoRoot, force));
                    break;
                case AgentKind.OpenCode:
                    results.Add(await AppendOpenCodeAgentsAsync(repoRoot, force));
                    break;
                case AgentKind.Cursor:
                    results.Add(await WriteCursorRuleAsync(repoRoot, force));
                    break;
            }
        }

        // Always write generic instructions
        results.Add(await WriteGenericInstructionsAsync(repoRoot, force));

        return results;
    }

    /// <summary>
    /// Ensures .codegraph/ is listed in .gitignore. Returns the write result.
    /// </summary>
    public static async Task<WriteResult> EnsureGitignoreEntryAsync(string repoRoot)
    {
        const string entry = ".codegraph/";
        var gitignorePath = Path.Combine(repoRoot, ".gitignore");

        if (File.Exists(gitignorePath))
        {
            var content = await ReadAllTextAsync(gitignorePath);
            if (content.Contains(entry))
                return new WriteResult(".gitignore", WriteAction.AlreadyPresent);

            var newline = content.Length > 0 && !content.EndsWith("\n") ? "\n" : "";
            await AppendAllTextAsync(gitignorePath, $"{newline}{entry}\n");
            return new WriteResult(".gitignore", WriteAction.Appended);
        }

        await WriteAllTextAsync(gitignorePath, $"{entry}\n");
        return new WriteResult(".gitignore", WriteAction.Created);
    }

    private static async Task<List<WriteResult>> WriteClaudeFilesAsync(string repoRoot, bool force)
    {
        var results = new List<WriteResult>();

        // SKILL.md
        var skillDir = Path.Combine(repoRoot, ".claude", "skills", "codegraph");
        var skillPath = Path.Combine(skillDir, "SKILL.md");
        results.Add(await WriteFileAsync(skillPath, AgentTemplates.ClaudeSkillMd, ".claude/skills/codegraph/SKILL.md", force));

        // query-wrapper.sh
        var scriptsDir = Path.Combine(skillDir, "scripts");
        var wrapperPath = Path.Combine(scriptsDir, "query-wrapper.sh");
        results.Add(await WriteFileAsync(wrapperPath, AgentTemplates.ClaudeQueryWrapperSh, ".claude/skills/codegraph/scripts/query-wrapper.sh", force));

        return results;
    }

    private static async Task<WriteResult> AppendCopilotInstructionsAsync(string repoRoot, bool force)
    {
        var dir = Path.Combine(repoRoot, ".github");
        var path = Path.Combine(dir, "copilot-instructions.md");

        if (File.Exists(path))
        {
            var content = await ReadAllTextAsync(path);
            if (content.Contains(AgentTemplates.AppendMarker))
                return new WriteResult(".github/copilot-instructions.md", WriteAction.AlreadyPresent);

            if (!force)
            {
                var newline = content.Length > 0 && !content.EndsWith("\n") ? "\n" : "";
                await AppendAllTextAsync(path, newline + AgentTemplates.CopilotInstructionsSection);
                return new WriteResult(".github/copilot-instructions.md", WriteAction.Appended);
            }
        }

        Directory.CreateDirectory(dir);
        if (!File.Exists(path))
        {
            await WriteAllTextAsync(path, AgentTemplates.CopilotInstructionsSection.TrimStart());
            return new WriteResult(".github/copilot-instructions.md", WriteAction.Created);
        }

        // force + exists but no marker — append
        var existing = await ReadAllTextAsync(path);
        var nl = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : "";
        await AppendAllTextAsync(path, nl + AgentTemplates.CopilotInstructionsSection);
        return new WriteResult(".github/copilot-instructions.md", WriteAction.Appended);
    }

    private static async Task<WriteResult> AppendOpenCodeAgentsAsync(string repoRoot, bool force)
    {
        var path = Path.Combine(repoRoot, "AGENTS.md");

        if (File.Exists(path))
        {
            var content = await ReadAllTextAsync(path);
            if (content.Contains(AgentTemplates.AppendMarker))
                return new WriteResult("AGENTS.md", WriteAction.AlreadyPresent);

            var newline = content.Length > 0 && !content.EndsWith("\n") ? "\n" : "";
            await AppendAllTextAsync(path, newline + AgentTemplates.OpenCodeAgentsSection);
            return new WriteResult("AGENTS.md", WriteAction.Appended);
        }

        await WriteAllTextAsync(path, AgentTemplates.OpenCodeAgentsSection.TrimStart());
        return new WriteResult("AGENTS.md", WriteAction.Created);
    }

    private static async Task<WriteResult> WriteCursorRuleAsync(string repoRoot, bool force)
    {
        var dir = Path.Combine(repoRoot, ".cursor", "rules");
        var path = Path.Combine(dir, "codegraph.md");
        return await WriteFileAsync(path, AgentTemplates.CursorRuleMd, ".cursor/rules/codegraph.md", force);
    }

    private static async Task<WriteResult> WriteGenericInstructionsAsync(string repoRoot, bool force)
    {
        var dir = Path.Combine(repoRoot, ".codegraph");
        var path = Path.Combine(dir, "INSTRUCTIONS.md");
        return await WriteFileAsync(path, AgentTemplates.GenericInstructionsMd, ".codegraph/INSTRUCTIONS.md", force);
    }

    private static async Task<WriteResult> WriteFileAsync(
        string fullPath, string content, string relativePath, bool force)
    {
        if (File.Exists(fullPath) && !force)
            return new WriteResult(relativePath, WriteAction.Skipped);

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await WriteAllTextAsync(fullPath, content);
        return new WriteResult(relativePath, WriteAction.Created);
    }

    // Polyfills for netstandard2.0 / net8.0 compatibility
    private static async Task<string> ReadAllTextAsync(string path)
    {
#if NETSTANDARD2_0
        return File.ReadAllText(path);
#else
        return await File.ReadAllTextAsync(path);
#endif
    }

    private static async Task WriteAllTextAsync(string path, string content)
    {
#if NETSTANDARD2_0
        File.WriteAllText(path, content);
        await Task.CompletedTask;
#else
        await File.WriteAllTextAsync(path, content);
#endif
    }

    private static async Task AppendAllTextAsync(string path, string content)
    {
#if NETSTANDARD2_0
        File.AppendAllText(path, content);
        await Task.CompletedTask;
#else
        await File.AppendAllTextAsync(path, content);
#endif
    }
}
