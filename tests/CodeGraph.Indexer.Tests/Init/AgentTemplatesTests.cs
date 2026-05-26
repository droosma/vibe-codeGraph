using CodeGraph.Indexer.Init;

namespace CodeGraph.Indexer.Tests.Init;

public class AgentTemplatesTests
{
    [Fact]
    public void ClaudeSkillMd_ContainsQueryInstructions()
    {
        Assert.Contains("codegraph query", AgentTemplates.ClaudeSkillMd);
        Assert.Contains("--depth", AgentTemplates.ClaudeSkillMd);
        Assert.Contains("--kind", AgentTemplates.ClaudeSkillMd);
        Assert.Contains("--include-source", AgentTemplates.ClaudeSkillMd);
        Assert.Contains("codegraph_file", AgentTemplates.ClaudeSkillMd);
    }

    [Fact]
    public void ClaudeQueryWrapperSh_IsValidShellScript()
    {
        Assert.StartsWith("#!/usr/bin/env bash", AgentTemplates.ClaudeQueryWrapperSh);
        Assert.Contains("codegraph query", AgentTemplates.ClaudeQueryWrapperSh);
    }

    [Fact]
    public void CopilotInstructionsSection_ContainsMarker()
    {
        Assert.Contains(AgentTemplates.AppendMarker, AgentTemplates.CopilotInstructionsSection);
    }

    [Fact]
    public void OpenCodeAgentsSection_ContainsMarker()
    {
        Assert.Contains(AgentTemplates.AppendMarker, AgentTemplates.OpenCodeAgentsSection);
    }

    [Fact]
    public void CursorRuleMd_ContainsQueryInstructions()
    {
        Assert.Contains("codegraph query", AgentTemplates.CursorRuleMd);
    }

    [Fact]
    public void GenericInstructionsMd_ContainsComprehensiveGuide()
    {
        Assert.Contains("codegraph query", AgentTemplates.GenericInstructionsMd);
        Assert.Contains("--depth", AgentTemplates.GenericInstructionsMd);
        Assert.Contains("--kind", AgentTemplates.GenericInstructionsMd);
        Assert.Contains("--format", AgentTemplates.GenericInstructionsMd);
    }

    [Fact]
    public void AllTemplates_AreNonEmpty()
    {
        Assert.NotEmpty(AgentTemplates.ClaudeSkillMd);
        Assert.NotEmpty(AgentTemplates.ClaudeQueryWrapperSh);
        Assert.NotEmpty(AgentTemplates.CopilotInstructionsSection);
        Assert.NotEmpty(AgentTemplates.OpenCodeAgentsSection);
        Assert.NotEmpty(AgentTemplates.CursorRuleMd);
        Assert.NotEmpty(AgentTemplates.GenericInstructionsMd);
        Assert.NotEmpty(AgentTemplates.AppendMarker);
        Assert.NotEmpty(AgentTemplates.ArchitectureAgentMd);
        Assert.NotEmpty(AgentTemplates.ImpactAgentMd);
        Assert.NotEmpty(AgentTemplates.CodeReviewAgentMd);
    }

    [Fact]
    public void ArchitectureAgentMd_ContainsTriggerPhrasesAndWorkflow()
    {
        Assert.Contains("explain this codebase architecture", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("trace the flow", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("How are these modules connected?", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("REPORT.md", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("codegraph list assemblies", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("codegraph query", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("codegraph explain", AgentTemplates.ArchitectureAgentMd);
        Assert.Contains("codegraph path", AgentTemplates.ArchitectureAgentMd);
    }

    [Fact]
    public void ImpactAgentMd_ContainsTriggerPhrasesAndWorkflow()
    {
        Assert.Contains("blast radius", AgentTemplates.ImpactAgentMd);
        Assert.Contains("what might break if I change X?", AgentTemplates.ImpactAgentMd);
        Assert.Contains("codegraph impact", AgentTemplates.ImpactAgentMd);
        Assert.Contains("codegraph diff", AgentTemplates.ImpactAgentMd);
        Assert.Contains("git diff", AgentTemplates.ImpactAgentMd);
        Assert.Contains("Risk level", AgentTemplates.ImpactAgentMd);
    }

    [Fact]
    public void CodeReviewAgentMd_ContainsTriggerPhrasesAndWorkflow()
    {
        Assert.Contains("code review", AgentTemplates.CodeReviewAgentMd);
        Assert.Contains("check dependents", AgentTemplates.CodeReviewAgentMd);
        Assert.Contains("test coverage", AgentTemplates.CodeReviewAgentMd);
        Assert.Contains("codegraph_query", AgentTemplates.CodeReviewAgentMd);
        Assert.Contains("covered-by", AgentTemplates.CodeReviewAgentMd);
        Assert.Contains("Meaningful structural findings", AgentTemplates.CodeReviewAgentMd);
    }

    [Fact]
    public void CopilotInstructionsSection_ContainsDelegatableAgentDefinitions()
    {
        Assert.Contains("Delegatable CodeGraph agents", AgentTemplates.CopilotInstructionsSection);
        Assert.Contains("codegraph-architecture.md", AgentTemplates.CopilotInstructionsSection);
        Assert.Contains("codegraph-impact.md", AgentTemplates.CopilotInstructionsSection);
        Assert.Contains("codegraph-review.md", AgentTemplates.CopilotInstructionsSection);
    }

    [Fact]
    public void OpenCodeAgentsSection_ContainsDelegatableAgentDefinitions()
    {
        Assert.Contains("Delegatable CodeGraph agents", AgentTemplates.OpenCodeAgentsSection);
        Assert.Contains("codegraph-architecture.md", AgentTemplates.OpenCodeAgentsSection);
        Assert.Contains("codegraph-impact.md", AgentTemplates.OpenCodeAgentsSection);
        Assert.Contains("codegraph-review.md", AgentTemplates.OpenCodeAgentsSection);
    }

    [Fact]
    public void AppendMarker_IsExactSharedHeading()
    {
        Assert.Equal("## CodeGraph — Structural Code Intelligence", AgentTemplates.AppendMarker);
        Assert.Contains(AgentTemplates.AppendMarker, AgentTemplates.CopilotInstructionsSection);
        Assert.Contains(AgentTemplates.AppendMarker, AgentTemplates.OpenCodeAgentsSection);
    }

    [Fact]
    public void ClaudeQueryWrapperSh_MatchesExactWrapperScript()
    {
        Assert.Equal(
            "#!/usr/bin/env bash\n# CodeGraph query wrapper for Claude Code skill scripts\n# Usage: ./query-wrapper.sh <symbol-pattern> [options]\nset -euo pipefail\ncodegraph query \"$@\"",
            AgentTemplates.ClaudeQueryWrapperSh.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CopilotAndOpenCodeSections_StartWithMarkerWithoutLeadingWhitespaceAfterTrim()
    {
        Assert.StartsWith("## CodeGraph — Structural Code Intelligence", AgentTemplates.CopilotInstructionsSection.TrimStart());
        Assert.StartsWith("## CodeGraph — Structural Code Intelligence", AgentTemplates.OpenCodeAgentsSection.TrimStart());
    }

    [Fact]
    public void GenericInstructionsMd_ListsAllDelegatableAgentFilesExactlyOnce()
    {
        Assert.Equal(1, AgentTemplates.GenericInstructionsMd.Split("codegraph-architecture.md").Length - 1);
        Assert.Equal(1, AgentTemplates.GenericInstructionsMd.Split("codegraph-impact.md").Length - 1);
        Assert.Equal(1, AgentTemplates.GenericInstructionsMd.Split("codegraph-review.md").Length - 1);
        Assert.Contains("Platform-specific integrations can load or mirror those definitions.", AgentTemplates.GenericInstructionsMd);
    }
}
