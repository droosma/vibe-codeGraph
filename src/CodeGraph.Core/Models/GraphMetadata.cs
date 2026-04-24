namespace CodeGraph.Core.Models;

public record GraphMetadata
{
    public int SchemaVersion { get; init; } = GraphSchema.CurrentVersion;
    public string CommitHash { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public string IndexerVersion { get; init; } = string.Empty;
    public string Solution { get; init; } = string.Empty;
    public string SolutionName { get; init; } = string.Empty;
    public string[] ProjectsIndexed { get; init; } = Array.Empty<string>();
    public Dictionary<string, int> Stats { get; init; } = new();
}
