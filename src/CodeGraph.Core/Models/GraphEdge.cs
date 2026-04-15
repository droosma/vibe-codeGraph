namespace CodeGraph.Core.Models;

public record GraphEdge
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public EdgeType Type { get; init; }
    public bool IsExternal { get; init; }
    public string? PackageSource { get; init; }
    public string? SourceLink { get; init; }
    public string? Resolution { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum EdgeType
{
    Contains,
    Calls,
    Inherits,
    Implements,
    DependsOn,
    ResolvesTo,
    Covers,
    CoveredBy,
    References,
    Overrides
}
