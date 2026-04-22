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
    }
}
