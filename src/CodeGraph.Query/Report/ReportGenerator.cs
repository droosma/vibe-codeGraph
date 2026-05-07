using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Report;

public static class ReportGenerator
{
    public static string Generate(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges, GraphMetadata metadata)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# CodeGraph Report");
        sb.AppendLine();
        sb.AppendLine($"**Solution:** {metadata.SolutionName}");
        sb.AppendLine($"**Generated:** {metadata.GeneratedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"**Commit:** {metadata.CommitHash}");
        sb.AppendLine($"**Nodes:** {nodes.Count:N0} | **Edges:** {edges.Count:N0}");
        sb.AppendLine();

        var hubs = HubAnalyzer.FindHubs(nodes, edges);
        sb.AppendLine("## Hub Types (highest connectivity)");
        sb.AppendLine();
        sb.AppendLine("| Type | In | Out | Total |");
        sb.AppendLine("|------|---:|----:|------:|");
        foreach (var hub in hubs)
            sb.AppendLine($"| {hub.Name} | {hub.InDegree} | {hub.OutDegree} | {hub.TotalDegree} |");
        sb.AppendLine();

        var clusters = ClusterAnalyzer.Analyze(nodes, edges);
        sb.AppendLine("## Assemblies");
        sb.AppendLine();
        sb.AppendLine("| Assembly | Nodes | Internal Edges | Cross-boundary Edges |");
        sb.AppendLine("|----------|------:|---------------:|--------------------:|");
        foreach (var c in clusters)
            sb.AppendLine($"| {c.Assembly} | {c.NodeCount} | {c.InternalEdges} | {c.ExternalEdges} |");
        sb.AppendLine();

        var coverage = CoverageAnalyzer.Analyze(nodes, edges);
        if (coverage.Any(c => c.CoveredTypes > 0))
        {
            sb.AppendLine("## Test Coverage (by assembly)");
            sb.AppendLine();
            sb.AppendLine("| Assembly | Types | Covered | Coverage |");
            sb.AppendLine("|----------|------:|--------:|---------:|");
            foreach (var c in coverage)
                sb.AppendLine($"| {c.Assembly} | {c.TotalTypes} | {c.CoveredTypes} | {c.CoveragePercent:F1}% |");
            sb.AppendLine();
        }

        var suggestions = QuerySuggester.Suggest(hubs, clusters);
        sb.AppendLine("## Suggested Queries");
        sb.AppendLine();
        foreach (var s in suggestions)
            sb.AppendLine($"```\n{s}\n```");

        return sb.ToString().TrimEnd();
    }
}
