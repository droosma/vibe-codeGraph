namespace CodeGraph.Query.Metrics;

/// <summary>
/// Token compression metrics for a CodeGraph query result.
/// </summary>
public record QueryMetrics
{
    /// <summary>Estimated tokens in the formatted output.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Estimated tokens if the same files were read raw.</summary>
    public int RawFileTokens { get; init; }

    /// <summary>Compression ratio (raw / output). Higher = better.</summary>
    public double CompressionRatio => RawFileTokens > 0 ? (double)RawFileTokens / OutputTokens : 0;

    /// <summary>Number of unique source files referenced in the result.</summary>
    public int FilesReferenced { get; init; }

    /// <summary>Number of nodes in the result.</summary>
    public int NodeCount { get; init; }

    /// <summary>Number of edges in the result.</summary>
    public int EdgeCount { get; init; }
}
