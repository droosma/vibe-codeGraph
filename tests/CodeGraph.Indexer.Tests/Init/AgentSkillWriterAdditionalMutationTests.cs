using CodeGraph.Indexer.Init;

namespace CodeGraph.Indexer.Tests.Init;

public sealed class AgentSkillWriterAdditionalMutationTests : IDisposable
{
    private readonly string _testDir;

    public AgentSkillWriterAdditionalMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "agent-writer-more-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task WriteAsync_InvalidAgentKind_IsIgnoredWhileGenericFilesAreWritten()
    {
        var results = await AgentSkillWriter.WriteAsync(_testDir, new[] { (AgentKind)999 }, force: false);

        Assert.Equal(
            [
                ".codegraph/INSTRUCTIONS.md",
                ".codegraph/agents/codegraph-architecture.md",
                ".codegraph/agents/codegraph-impact.md",
                ".codegraph/agents/codegraph-review.md"
            ],
            results.Select(result => result.RelativePath).ToArray());
    }

    [Fact]
    public async Task WriteAsync_ClaudeForce_OverwritesExistingClaudeAgentDefinition()
    {
        var agentsDir = Path.Combine(_testDir, ".claude", "agents");
        Directory.CreateDirectory(agentsDir);
        var architecturePath = Path.Combine(agentsDir, "codegraph-architecture.md");
        File.WriteAllText(architecturePath, "custom content");

        var results = await AgentSkillWriter.WriteAsync(_testDir, new[] { AgentKind.Claude }, force: true);
        var result = Assert.Single(results.Where(write => write.RelativePath == ".claude/agents/codegraph-architecture.md"));

        Assert.Equal(WriteAction.Created, result.Action);
        Assert.Contains("CodeGraph Architecture Analyst", File.ReadAllText(architecturePath));
    }

    [Fact]
    public async Task WriteAsync_WithoutForce_SkipsExistingGenericAgentDefinition()
    {
        var agentsDir = Path.Combine(_testDir, ".codegraph", "agents");
        Directory.CreateDirectory(agentsDir);
        var impactPath = Path.Combine(agentsDir, "codegraph-impact.md");
        File.WriteAllText(impactPath, "custom impact");

        var results = await AgentSkillWriter.WriteAsync(_testDir, Array.Empty<AgentKind>(), force: false);
        var result = Assert.Single(results.Where(write => write.RelativePath == ".codegraph/agents/codegraph-impact.md"));

        Assert.Equal(WriteAction.Skipped, result.Action);
        Assert.Equal("custom impact", File.ReadAllText(impactPath));
    }
}

public sealed class AgentDetectorAdditionalMutationTests : IDisposable
{
    private readonly string _testDir;

    public AgentDetectorAdditionalMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "agent-detector-more-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Detect_MultipleSignalsForClaudeAndCursor_DoNotDuplicatePreferredEntries()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".claude"));
        File.WriteAllText(Path.Combine(_testDir, "CLAUDE.md"), "# Claude");
        File.WriteAllText(Path.Combine(_testDir, ".cursorrules"), "{}");
        Directory.CreateDirectory(Path.Combine(_testDir, ".cursor", "rules"));

        var detections = AgentDetector.Detect(_testDir);

        Assert.Equal(2, detections.Count);
        Assert.Contains(detections, detection => detection.Agent == AgentKind.Claude && detection.MatchedPath == ".claude/ directory");
        Assert.Contains(detections, detection => detection.Agent == AgentKind.Cursor && detection.MatchedPath == ".cursorrules");
    }
}
