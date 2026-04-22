using CodeGraph.Indexer.Init;

namespace CodeGraph.Indexer.Tests.Init;

public class AgentDetectorTests : IDisposable
{
    private readonly string _testDir;

    public AgentDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-detect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Detect_EmptyDirectory_ReturnsEmpty()
    {
        var result = AgentDetector.Detect(_testDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Detect_ClaudeDirectory_DetectsClaude()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".claude"));

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.Claude, detection.Agent);
        Assert.Contains(".claude/", detection.MatchedPath);
    }

    [Fact]
    public void Detect_ClaudeMd_DetectsClaude()
    {
        File.WriteAllText(Path.Combine(_testDir, "CLAUDE.md"), "# Claude");

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.Claude, detection.Agent);
        Assert.Equal("CLAUDE.md", detection.MatchedPath);
    }

    [Fact]
    public void Detect_ClaudeDirectoryPreferredOverClaudeMd()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".claude"));
        File.WriteAllText(Path.Combine(_testDir, "CLAUDE.md"), "# Claude");

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Contains(".claude/", detection.MatchedPath);
    }

    [Fact]
    public void Detect_CopilotInstructions_DetectsCopilot()
    {
        var dir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "copilot-instructions.md"), "# Instructions");

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.Copilot, detection.Agent);
    }

    [Fact]
    public void Detect_AgentsMd_DetectsOpenCode()
    {
        File.WriteAllText(Path.Combine(_testDir, "AGENTS.md"), "# Agents");

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.OpenCode, detection.Agent);
    }

    [Fact]
    public void Detect_Cursorrules_DetectsCursor()
    {
        File.WriteAllText(Path.Combine(_testDir, ".cursorrules"), "{}");

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.Cursor, detection.Agent);
    }

    [Fact]
    public void Detect_CursorRulesDirectory_DetectsCursor()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".cursor", "rules"));

        var result = AgentDetector.Detect(_testDir);

        var detection = Assert.Single(result);
        Assert.Equal(AgentKind.Cursor, detection.Agent);
    }

    [Fact]
    public void Detect_MultipleAgents_DetectsAll()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".claude"));
        File.WriteAllText(Path.Combine(_testDir, "AGENTS.md"), "# Agents");
        var githubDir = Path.Combine(_testDir, ".github");
        Directory.CreateDirectory(githubDir);
        File.WriteAllText(Path.Combine(githubDir, "copilot-instructions.md"), "# Instructions");
        File.WriteAllText(Path.Combine(_testDir, ".cursorrules"), "{}");

        var result = AgentDetector.Detect(_testDir);

        Assert.Equal(4, result.Count);
        Assert.Contains(result, d => d.Agent == AgentKind.Claude);
        Assert.Contains(result, d => d.Agent == AgentKind.Copilot);
        Assert.Contains(result, d => d.Agent == AgentKind.OpenCode);
        Assert.Contains(result, d => d.Agent == AgentKind.Cursor);
    }
}
