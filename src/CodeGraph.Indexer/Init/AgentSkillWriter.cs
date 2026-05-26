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
    public static async Task<List<WriteResult>> WriteAsync(
        string repoRoot,
        IEnumerable<AgentKind> agents,
        bool force)
    {
        var selectedAgents = agents.Distinct().ToList();
        var results = new List<WriteResult>();

        foreach (var agent in selectedAgents)
            results.AddRange(await WriteAgentFilesAsync(repoRoot, agent, force).ConfigureAwait(false));

        results.Add(await WriteGenericInstructionsAsync(repoRoot, force).ConfigureAwait(false));
        results.AddRange(await WriteAgentDefinitionsAsync(repoRoot, force).ConfigureAwait(false));

        if (selectedAgents.Contains(AgentKind.Claude))
            results.AddRange(await WriteClaudeAgentDefinitionsAsync(repoRoot, force).ConfigureAwait(false));

        return results;
    }

    public static async Task<WriteResult> EnsureGitignoreEntryAsync(string repoRoot)
    {
        const string entry = ".codegraph/";
        var gitignorePath = Path.Combine(repoRoot, ".gitignore");

        if (!File.Exists(gitignorePath))
        {
            await WriteAllTextAsync(gitignorePath, $"{entry}\n").ConfigureAwait(false);
            return new WriteResult(".gitignore", WriteAction.Created);
        }

        var content = await ReadAllTextAsync(gitignorePath).ConfigureAwait(false);
        if (content.Contains(entry, StringComparison.Ordinal))
            return new WriteResult(".gitignore", WriteAction.AlreadyPresent);

        await AppendAllTextAsync(gitignorePath, BuildAppendContent(content, $"{entry}\n")).ConfigureAwait(false);
        return new WriteResult(".gitignore", WriteAction.Appended);
    }

    private static Task<List<WriteResult>> WriteAgentFilesAsync(string repoRoot, AgentKind agent, bool force)
    {
        return agent switch
        {
            AgentKind.Claude => WriteClaudeFilesAsync(repoRoot, force),
            AgentKind.Copilot => WriteSingleResultAsync(AppendCopilotInstructionsAsync(repoRoot)),
            AgentKind.OpenCode => WriteSingleResultAsync(AppendOpenCodeAgentsAsync(repoRoot)),
            AgentKind.Cursor => WriteSingleResultAsync(WriteCursorRuleAsync(repoRoot, force)),
            _ => Task.FromResult(new List<WriteResult>())
        };
    }

    private static async Task<List<WriteResult>> WriteSingleResultAsync(Task<WriteResult> resultTask)
    {
        return new List<WriteResult>
        {
            await resultTask.ConfigureAwait(false)
        };
    }

    private static Task<List<WriteResult>> WriteClaudeFilesAsync(string repoRoot, bool force)
    {
        return WriteTemplateFilesAsync(repoRoot, AgentTemplates.ClaudeSkillFiles, force);
    }

    private static Task<WriteResult> AppendCopilotInstructionsAsync(string repoRoot)
    {
        return AppendSectionAsync(
            repoRoot,
            ".github/copilot-instructions.md",
            AgentTemplates.CopilotInstructionsSection);
    }

    private static Task<WriteResult> AppendOpenCodeAgentsAsync(string repoRoot)
    {
        return AppendSectionAsync(
            repoRoot,
            "AGENTS.md",
            AgentTemplates.OpenCodeAgentsSection);
    }

    private static Task<WriteResult> WriteCursorRuleAsync(string repoRoot, bool force)
    {
        return WriteTemplateAsync(
            repoRoot,
            ".cursor/rules/codegraph.md",
            AgentTemplates.CursorRuleMd,
            force);
    }

    private static Task<WriteResult> WriteGenericInstructionsAsync(string repoRoot, bool force)
    {
        return WriteTemplateAsync(
            repoRoot,
            ".codegraph/INSTRUCTIONS.md",
            AgentTemplates.GenericInstructionsMd,
            force);
    }

    private static Task<List<WriteResult>> WriteAgentDefinitionsAsync(string repoRoot, bool force)
    {
        return WriteAgentDefinitionSetAsync(repoRoot, ".codegraph/agents", force);
    }

    private static Task<List<WriteResult>> WriteClaudeAgentDefinitionsAsync(string repoRoot, bool force)
    {
        return WriteAgentDefinitionSetAsync(repoRoot, ".claude/agents", force);
    }

    private static async Task<List<WriteResult>> WriteAgentDefinitionSetAsync(
        string repoRoot,
        string relativeDirectory,
        bool force)
    {
        var results = new List<WriteResult>();

        foreach (var (fileName, content) in AgentTemplates.AgentDefinitionFiles)
        {
            var relativePath = $"{relativeDirectory}/{fileName}";
            results.Add(await WriteTemplateAsync(repoRoot, relativePath, content, force).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<List<WriteResult>> WriteTemplateFilesAsync(
        string repoRoot,
        IReadOnlyList<(string RelativePath, string Content)> templates,
        bool force)
    {
        var results = new List<WriteResult>(templates.Count);

        foreach (var (relativePath, content) in templates)
            results.Add(await WriteTemplateAsync(repoRoot, relativePath, content, force).ConfigureAwait(false));

        return results;
    }

    private static async Task<WriteResult> AppendSectionAsync(
        string repoRoot,
        string relativePath,
        string sectionContent)
    {
        var fullPath = ToFullPath(repoRoot, relativePath);
        EnsureDirectoryExists(fullPath);

        if (!File.Exists(fullPath))
        {
            await WriteAllTextAsync(fullPath, sectionContent.TrimStart()).ConfigureAwait(false);
            return new WriteResult(relativePath, WriteAction.Created);
        }

        var existingContent = await ReadAllTextAsync(fullPath).ConfigureAwait(false);
        if (existingContent.Contains(AgentTemplates.AppendMarker, StringComparison.Ordinal))
            return new WriteResult(relativePath, WriteAction.AlreadyPresent);

        await AppendAllTextAsync(fullPath, BuildAppendContent(existingContent, sectionContent)).ConfigureAwait(false);
        return new WriteResult(relativePath, WriteAction.Appended);
    }

    private static async Task<WriteResult> WriteTemplateAsync(
        string repoRoot,
        string relativePath,
        string content,
        bool force)
    {
        var fullPath = ToFullPath(repoRoot, relativePath);
        if (File.Exists(fullPath) && !force)
            return new WriteResult(relativePath, WriteAction.Skipped);

        EnsureDirectoryExists(fullPath);
        await WriteAllTextAsync(fullPath, content).ConfigureAwait(false);
        return new WriteResult(relativePath, WriteAction.Created);
    }

    private static string BuildAppendContent(string existingContent, string appendedContent)
    {
        var newline = existingContent.Length > 0 && !existingContent.EndsWith("\n", StringComparison.Ordinal)
            ? "\n"
            : string.Empty;

        return newline + appendedContent;
    }

    private static string ToFullPath(string repoRoot, string relativePath)
    {
        var segments = relativePath.Split('/');
        return Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
    }

    private static void EnsureDirectoryExists(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static async Task<string> ReadAllTextAsync(string path)
    {
#if NETSTANDARD2_0
        return File.ReadAllText(path);
#else
        return await File.ReadAllTextAsync(path).ConfigureAwait(false);
#endif
    }

    private static async Task WriteAllTextAsync(string path, string content)
    {
#if NETSTANDARD2_0
        File.WriteAllText(path, content);
        await Task.CompletedTask;
#else
        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
#endif
    }

    private static async Task AppendAllTextAsync(string path, string content)
    {
#if NETSTANDARD2_0
        File.AppendAllText(path, content);
        await Task.CompletedTask;
#else
        await File.AppendAllTextAsync(path, content).ConfigureAwait(false);
#endif
    }
}
