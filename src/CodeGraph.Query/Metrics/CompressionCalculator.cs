namespace CodeGraph.Query.Metrics;

/// <summary>
/// Calculates token compression metrics for query results.
/// </summary>
public static class CompressionCalculator
{
    private const int CharsPerToken = 4;

    /// <summary>
    /// Calculate metrics for a query result and its formatted output.
    /// </summary>
    public static QueryMetrics Calculate(QueryResult result, string formattedOutput)
    {
        var outputTokens = EstimateTokens(formattedOutput);

        var files = result.Nodes.Values
            .Where(n => !string.IsNullOrEmpty(n.FilePath))
            .Select(n => (n.FilePath, n.StartLine, n.EndLine))
            .GroupBy(f => f.FilePath)
            .ToList();

        // Estimate: each referenced source line is ~40 chars (~10 tokens)
        var totalLines = files.Sum(g =>
            g.Sum(f => Math.Max(f.EndLine - f.StartLine + 1, 1)));
        var rawFileTokens = totalLines * 10;

        return new QueryMetrics
        {
            OutputTokens = outputTokens,
            RawFileTokens = rawFileTokens,
            FilesReferenced = files.Count,
            NodeCount = result.Nodes.Count,
            EdgeCount = result.Edges.Count
        };
    }

    /// <summary>
    /// Estimates token count using ~4 chars per token heuristic.
    /// </summary>
    public static int EstimateTokens(string text) => (text.Length + CharsPerToken - 1) / CharsPerToken;
}
