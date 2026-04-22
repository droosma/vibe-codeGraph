using CodeGraph.Indexer.Init;

namespace CodeGraph.Indexer.Tests.Init;

public class AgentSkillWriterTests : IDisposable
{
    private readonly string _testDir;

    public AgentSkillWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task WriteAsync_Claude_CreatesSkillAndWrapper()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Claude }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".claude/skills/codegraph/SKILL.md" && r.Action == WriteAction.Created);
        Assert.Contains(results, r =>
            r.RelativePath == ".claude/skills/codegraph/scripts/query-wrapper.sh" && r.Action == WriteAction.Created);

        var skillPath = Path.Combine(_testDir, ".claude", "skills", "codegraph", "SKILL.md");
        Assert.True(File.Exists(skillPath));
        var content = File.ReadAllText(skillPath);
        Assert.Contains("CodeGraph Query Skill", content);

        var wrapperPath = Path.Combine(_testDir, ".claude", "skills", "codegraph", "scripts", "query-wrapper.sh");
        Assert.True(File.Exists(wrapperPath));
    }

    [Fact]
    public async Task WriteAsync_Copilot_CreatesNewFile()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".github/copilot-instructions.md" && r.Action == WriteAction.Created);

        var path = Path.Combine(_testDir, ".github", "copilot-instructions.md");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("CodeGraph", content);
    }

    [Fact]
    public async Task WriteAsync_Copilot_AppendsToExistingFile()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), "# Existing Instructions\n");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".github/copilot-instructions.md" && r.Action == WriteAction.Appended);

        var content = File.ReadAllText(Path.Combine(dir, "copilot-instructions.md"));
        Assert.StartsWith("# Existing Instructions", content);
        Assert.Contains("CodeGraph", content);
    }

    [Fact]
    public async Task WriteAsync_Copilot_SkipsIfAlreadyPresent()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "copilot-instructions.md"),
            $"# Instructions\n{AgentTemplates.AppendMarker}\nStuff");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Copilot }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".github/copilot-instructions.md" && r.Action == WriteAction.AlreadyPresent);
    }

    [Fact]
    public async Task WriteAsync_OpenCode_CreatesNewFile()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.OpenCode }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == "AGENTS.md" && r.Action == WriteAction.Created);

        var path = Path.Combine(_testDir, "AGENTS.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteAsync_OpenCode_AppendsToExisting()
    {
        File.WriteAllText(Path.Combine(_testDir, "AGENTS.md"), "# Agents\n");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.OpenCode }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == "AGENTS.md" && r.Action == WriteAction.Appended);

        var content = File.ReadAllText(Path.Combine(_testDir, "AGENTS.md"));
        Assert.StartsWith("# Agents", content);
        Assert.Contains("CodeGraph", content);
    }

    [Fact]
    public async Task WriteAsync_Cursor_CreatesRuleFile()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Cursor }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".cursor/rules/codegraph.md" && r.Action == WriteAction.Created);

        var path = Path.Combine(_testDir, ".cursor", "rules", "codegraph.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteAsync_AlwaysCreatesGenericInstructions()
    {
        var results = await AgentSkillWriter.WriteAsync(
            _testDir, Array.Empty<AgentKind>(), force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".codegraph/INSTRUCTIONS.md" && r.Action == WriteAction.Created);

        var path = Path.Combine(_testDir, ".codegraph", "INSTRUCTIONS.md");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("codegraph query", content);
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_SkippedWithoutForce()
    {
        var skillDir = Path.Combine(_testDir, ".claude", "skills", "codegraph");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "custom content");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Claude }, force: false);

        Assert.Contains(results, r =>
            r.RelativePath == ".claude/skills/codegraph/SKILL.md" && r.Action == WriteAction.Skipped);

        var content = File.ReadAllText(Path.Combine(skillDir, "SKILL.md"));
        Assert.Equal("custom content", content);
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_OverwrittenWithForce()
    {
        var skillDir = Path.Combine(_testDir, ".claude", "skills", "codegraph");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "custom content");

        var results = await AgentSkillWriter.WriteAsync(
            _testDir, new[] { AgentKind.Claude }, force: true);

        Assert.Contains(results, r =>
            r.RelativePath == ".claude/skills/codegraph/SKILL.md" && r.Action == WriteAction.Created);

        var content = File.ReadAllText(Path.Combine(skillDir, "SKILL.md"));
        Assert.Contains("CodeGraph Query Skill", content);
    }

    [Fact]
    public async Task WriteAsync_AllAgents_CreatesAllFiles()
    {
        var agents = new[] { AgentKind.Claude, AgentKind.Copilot, AgentKind.OpenCode, AgentKind.Cursor };
        var results = await AgentSkillWriter.WriteAsync(_testDir, agents, force: false);

        Assert.Contains(results, r => r.RelativePath.Contains("SKILL.md"));
        Assert.Contains(results, r => r.RelativePath.Contains("copilot-instructions.md"));
        Assert.Contains(results, r => r.RelativePath.Contains("AGENTS.md"));
        Assert.Contains(results, r => r.RelativePath.Contains("codegraph.md"));
        Assert.Contains(results, r => r.RelativePath.Contains("INSTRUCTIONS.md"));
    }

    [Fact]
    public async Task WriteAsync_DuplicateAgents_DeduplicatedGracefully()
    {
        var agents = new[] { AgentKind.Claude, AgentKind.Claude };
        var results = await AgentSkillWriter.WriteAsync(_testDir, agents, force: false);

        // Should only have one SKILL.md result (deduped) + wrapper + generic
        var skillResults = results.Where(r => r.RelativePath == ".claude/skills/codegraph/SKILL.md").ToList();
        Assert.Single(skillResults);
    }

    [Fact]
    public async Task EnsureGitignoreEntryAsync_NoGitignore_CreatesWithEntry()
    {
        var result = await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        Assert.Equal(WriteAction.Created, result.Action);
        var content = File.ReadAllText(Path.Combine(_testDir, ".gitignore"));
        Assert.Contains(".codegraph/", content);
    }

    [Fact]
    public async Task EnsureGitignoreEntryAsync_ExistingWithoutEntry_Appends()
    {
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "bin/\nobj/\n");

        var result = await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        Assert.Equal(WriteAction.Appended, result.Action);
        var content = File.ReadAllText(Path.Combine(_testDir, ".gitignore"));
        Assert.Contains("bin/", content);
        Assert.Contains(".codegraph/", content);
    }

    [Fact]
    public async Task EnsureGitignoreEntryAsync_AlreadyHasEntry_ReportsAlreadyPresent()
    {
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "bin/\n.codegraph/\n");

        var result = await AgentSkillWriter.EnsureGitignoreEntryAsync(_testDir);

        Assert.Equal(WriteAction.AlreadyPresent, result.Action);
    }
}
