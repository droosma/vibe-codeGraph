using CodeGraph.Indexer.Init;

namespace CodeGraph.Indexer.Tests.Init;

/// <summary>
/// Additional mutation-killing tests for AgentSkillWriter.
/// Targets: force logic for Copilot (existing + force + no marker),
/// newline insertion logic, OpenCode AlreadyPresent, Cursor skip/force,
/// generic instructions skip/force, EnsureGitignore without trailing newline.
/// </summary>
public class AgentSkillWriterMutationTests : IDisposable
{
    private readonly string _testDir;

    public AgentSkillWriterMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-mut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    // ── Copilot: force=true + existing file without marker → appends ──

    [Fact]
    public async Task WriteAsync_Copilot_ForceWithExistingNoMarker_Appends()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), "# Existing");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: true);

        Assert.Contains(results, r =>
            r.RelativePath == ".github/copilot-instructions.md" && r.Action == WriteAction.Appended);

        var content = File.ReadAllText(Path.Combine(dir, "copilot-instructions.md"));
        Assert.StartsWith("# Existing", content);
        Assert.Contains(AgentTemplates.AppendMarker, content);
    }

    // ── Copilot: force=true + no existing file → creates ──

    [Fact]
    public async Task WriteAsync_Copilot_ForceNoExisting_Creates()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: true);

        Assert.Contains(results, r =>
            r.RelativePath == ".github/copilot-instructions.md" && r.Action == WriteAction.Created);

        var path = Path.Combine(_testDir, ".github", "copilot-instructions.md");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains(AgentTemplates.AppendMarker, content);
    }

    // ── Copilot: existing file without trailing newline gets newline before append ──

    [Fact]
    public async Task WriteAsync_Copilot_ExistingWithoutTrailingNewline_InsertsNewline()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        // No trailing newline
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), "# Header\nContent");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: false);

        var content = File.ReadAllText(Path.Combine(dir, "copilot-instructions.md"));
        // Should have newline between existing content and appended section
        Assert.Contains("Content\n", content);
    }

    // ── Copilot: existing file with trailing newline doesn't double newline ──

    [Fact]
    public async Task WriteAsync_Copilot_ExistingWithTrailingNewline_NoDoubleNewline()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), "# Header\n");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: false);

        var content = File.ReadAllText(Path.Combine(dir, "copilot-instructions.md"));
        // Should not have double newline
        Assert.DoesNotContain("\n\n\n", content);
    }

    // ── OpenCode: existing file already has marker → AlreadyPresent ──

    [Fact]
    public async Task WriteAsync_OpenCode_AlreadyHasMarker_ReportsAlreadyPresent()
    {
        File.WriteAllText(Path.Combine(_testDir, "AGENTS.md"),
            $"# Agents\n{AgentTemplates.AppendMarker}\n");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.OpenCode }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == "AGENTS.md" && r.Action == WriteAction.AlreadyPresent);
    }

    // ── OpenCode: existing without trailing newline ──

    [Fact]
    public async Task WriteAsync_OpenCode_ExistingNoTrailingNewline_InsertsNewline()
    {
        File.WriteAllText(Path.Combine(_testDir, "AGENTS.md"), "# My Agents");

        await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.OpenCode }, force: false);

        var content = File.ReadAllText(Path.Combine(_testDir, "AGENTS.md"));
        Assert.StartsWith("# My Agents\n", content);
    }

    // ── Cursor: existing file without force → Skipped ──

    [Fact]
    public async Task WriteAsync_Cursor_ExistingWithoutForce_Skipped()
    {
        var dir = Path.Combine(_testDir, ".cursor", "rules");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "codegraph.md"), "custom");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Cursor }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".cursor/rules/codegraph.md" && r.Action == WriteAction.Skipped);

        var content = File.ReadAllText(Path.Combine(dir, "codegraph.md"));
        Assert.Equal("custom", content);
    }

    // ── Cursor: existing file with force → Created (overwritten) ──

    [Fact]
    public async Task WriteAsync_Cursor_ExistingWithForce_Overwrites()
    {
        var dir = Path.Combine(_testDir, ".cursor", "rules");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "codegraph.md"), "custom");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Cursor }, force: true);

        Assert.Contains(results, r =>
            r.RelativePath == ".cursor/rules/codegraph.md" && r.Action == WriteAction.Created);

        var content = File.ReadAllText(Path.Combine(dir, "codegraph.md"));
        Assert.NotEqual("custom", content);
        Assert.Contains("codegraph", content.ToLower());
    }

    // ── Generic instructions: existing without force → Skipped ──

    [Fact]
    public async Task WriteAsync_GenericInstructions_ExistingWithoutForce_Skipped()
    {
        var dir = Path.Combine(_testDir, ".codegraph");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "INSTRUCTIONS.md"), "old content");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, Array.Empty<AgentKind>(), force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".codegraph/INSTRUCTIONS.md" && r.Action == WriteAction.Skipped);
    }

    // ── Generic instructions: existing with force → Created (overwritten) ──

    [Fact]
    public async Task WriteAsync_GenericInstructions_ExistingWithForce_Overwrites()
    {
        var dir = Path.Combine(_testDir, ".codegraph");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "INSTRUCTIONS.md"), "old content");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, Array.Empty<AgentKind>(), force: true);

        Assert.Contains(results, r =>
            r.RelativePath == ".codegraph/INSTRUCTIONS.md" && r.Action == WriteAction.Created);

        var content = File.ReadAllText(Path.Combine(dir, "INSTRUCTIONS.md"));
        Assert.NotEqual("old content", content);
    }

    // ── EnsureGitignoreEntryAsync: existing without trailing newline ──

    [Fact]
    public async Task EnsureGitignoreEntryAsync_ExistingNoTrailingNewline_InsertsNewlineBeforeEntry()
    {
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "bin/\nobj/");

        await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        var content = File.ReadAllText(Path.Combine(_testDir, ".gitignore"));
        // The .codegraph/ entry should be on its own line
        Assert.Contains("obj/\n.codegraph/\n", content);
    }

    // ── EnsureGitignoreEntryAsync: existing with trailing newline ──

    [Fact]
    public async Task EnsureGitignoreEntryAsync_ExistingWithTrailingNewline_NoDoubleNewline()
    {
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "bin/\n");

        await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        var content = File.ReadAllText(Path.Combine(_testDir, ".gitignore"));
        Assert.DoesNotContain("\n\n.codegraph/", content);
        Assert.Contains(".codegraph/", content);
    }

    // ── EnsureGitignoreEntryAsync: empty existing file ──

    [Fact]
    public async Task EnsureGitignoreEntryAsync_EmptyExistingFile_AppendsEntry()
    {
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "");

        var result = await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        Assert.Equal(WriteAction.Appended, result.Action);
        var content = File.ReadAllText(Path.Combine(_testDir, ".gitignore"));
        Assert.Contains(".codegraph/", content);
    }

    // ── EnsureGitignoreEntryAsync: returns correct RelativePath ──

    [Fact]
    public async Task EnsureGitignoreEntryAsync_ReturnsCorrectRelativePath()
    {
        var result = await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);
        Assert.Equal(".gitignore", result.RelativePath);
    }

    // ── Claude: wrapper script path is correct ──

    [Fact]
    public async Task WriteAsync_Claude_WrapperScriptInCorrectPath()
    {
        await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Claude }, force: false);

        var wrapperPath = Path.Combine(_testDir, ".claude", "skills", "codegraph", "scripts", "query-wrapper.sh");
        Assert.True(File.Exists(wrapperPath));
        var content = File.ReadAllText(wrapperPath);
        Assert.Contains("codegraph", content.ToLower());
    }

    // ── WriteResult record properties ──

    [Fact]
    public async Task WriteResult_HasCorrectActionValues()
    {
        // Test that Created, Skipped, AlreadyPresent are distinct
        var dir = Path.Combine(_testDir, ".claude", "skills", "codegraph");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "existing");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Claude }, force: false);

        var skillResult = results.Single(r => r.RelativePath == ".claude/skills/codegraph/SKILL.md");
        Assert.Equal(WriteAction.Skipped, skillResult.Action);
        Assert.NotEqual(WriteAction.Created, skillResult.Action);
        Assert.NotEqual(WriteAction.Appended, skillResult.Action);
        Assert.NotEqual(WriteAction.AlreadyPresent, skillResult.Action);
    }

    [Fact]
    public async Task WriteAsync_Copilot_ForceWithMarker_StillReportsAlreadyPresent()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), AgentTemplates.AppendMarker + "\nExisting");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: true);

        var result = results.Single(r => r.RelativePath == ".github/copilot-instructions.md");
        Assert.Equal(WriteAction.AlreadyPresent, result.Action);
        Assert.Equal(AgentTemplates.AppendMarker + "\nExisting", File.ReadAllText(Path.Combine(dir, "copilot-instructions.md")));
    }

    [Fact]
    public async Task WriteAsync_NewFiles_AreTrimmedWithoutLeadingBlankLine()
    {
        await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot, AgentKind.OpenCode }, force: false);

        var copilot = File.ReadAllText(Path.Combine(_testDir, ".github", "copilot-instructions.md"));
        var openCode = File.ReadAllText(Path.Combine(_testDir, "AGENTS.md"));

        Assert.StartsWith("## CodeGraph — Structural Code Intelligence", copilot);
        Assert.StartsWith("## CodeGraph — Structural Code Intelligence", openCode);
    }

    [Fact]
    public async Task WriteAsync_EmptyAgentSelection_ReturnsGenericFilesInStableOrder()
    {
        var results = await AgentSkillWriter.WriteAsync(_testDir, Array.Empty<AgentKind>(), force: false);

        Assert.Equal(
            [
                ".codegraph/INSTRUCTIONS.md",
                ".codegraph/agents/codegraph-architecture.md",
                ".codegraph/agents/codegraph-impact.md",
                ".codegraph/agents/codegraph-review.md"
            ],
            results.Select(r => r.RelativePath).ToArray());
        Assert.All(results, r => Assert.Equal(WriteAction.Created, r.Action));
    }

    [Fact]
    public async Task EnsureGitignoreEntryAsync_CreateWritesExactFileContent()
    {
        await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        Assert.Equal(".codegraph/\n", File.ReadAllText(Path.Combine(_testDir, ".gitignore")).Replace("\r\n", "\n"));
    }
}
