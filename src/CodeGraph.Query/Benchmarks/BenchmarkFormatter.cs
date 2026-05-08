using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeGraph.Query.Benchmarks;

/// <summary>
/// Formats benchmark results as markdown tables or JSON.
/// </summary>
public static class BenchmarkFormatter
{
    /// <summary>
    /// Format results as a markdown table with timing and size columns.
    /// </summary>
    public static string FormatMarkdown(IReadOnlyList<BenchmarkResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Benchmark Results");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Iterations | Min | Max | Median | Mean | Nodes | Edges | Tokens |");
        sb.AppendLine("|----------|------------|-----|-----|--------|------|-------|-------|--------|");

        foreach (var r in results)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |",
                r.ScenarioName,
                r.Iterations,
                FormatDuration(r.Min),
                FormatDuration(r.Max),
                FormatDuration(r.Median),
                FormatDuration(r.Mean),
                r.ResultNodeCount,
                r.ResultEdgeCount,
                r.OutputTokenEstimate));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format results as a JSON array.
    /// </summary>
    public static string FormatJson(IReadOnlyList<BenchmarkResult> results)
    {
        var items = results.Select(r => new
        {
            scenario = r.ScenarioName,
            iterations = r.Iterations,
            min_ms = Math.Round(r.Min.TotalMilliseconds, 3),
            max_ms = Math.Round(r.Max.TotalMilliseconds, 3),
            median_ms = Math.Round(r.Median.TotalMilliseconds, 3),
            mean_ms = Math.Round(r.Mean.TotalMilliseconds, 3),
            result_nodes = r.ResultNodeCount,
            result_edges = r.ResultEdgeCount,
            output_tokens = r.OutputTokenEstimate
        });

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMilliseconds < 1)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}µs", ts.TotalMicroseconds);

        if (ts.TotalSeconds < 1)
            return string.Format(CultureInfo.InvariantCulture, "{0:F2}ms", ts.TotalMilliseconds);

        return string.Format(CultureInfo.InvariantCulture, "{0:F2}s", ts.TotalSeconds);
    }
}
