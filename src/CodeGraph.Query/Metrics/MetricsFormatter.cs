using System.Text;

namespace CodeGraph.Query.Metrics;

/// <summary>
/// Appends a metrics footer to formatted query output.
/// </summary>
public static class MetricsFormatter
{
    public static string AppendMetrics(string output, QueryMetrics metrics)
    {
        var sb = new StringBuilder(output);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"📊 {metrics.NodeCount} nodes, {metrics.EdgeCount} edges, {metrics.FilesReferenced} files");
        sb.AppendLine($"📦 ~{metrics.OutputTokens} tokens (vs ~{metrics.RawFileTokens} raw → {metrics.CompressionRatio:F1}× compression)");
        return sb.ToString();
    }
}
