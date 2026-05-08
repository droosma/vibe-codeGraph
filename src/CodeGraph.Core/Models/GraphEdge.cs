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
    public EdgeConfidence Confidence { get; init; } = EdgeConfidence.Verified;
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
    Overrides,
    HandlesRoute,
    BindsConfiguration,
    UsesMiddleware,
    MapsToTable,
    NavigatesTo,
    ConfiguredBy
}

public enum EdgeConfidence
{
    /// <summary>Edge was confirmed by Roslyn semantic analysis</summary>
    Verified,
    /// <summary>Edge was inferred (e.g., single DI implementation)</summary>
    Inferred,
    /// <summary>Edge target could not be fully resolved</summary>
    Unresolved
}
