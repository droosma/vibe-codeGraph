using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class TextFormatter
{
    public static string Format(QueryResult result)
    {
        var sb = new StringBuilder();

        if (result.TargetNode is not null)
        {
            sb.AppendLine($"Target: {result.TargetNode.Id} ({result.TargetNode.Kind})");
            sb.AppendLine($"  File: {result.TargetNode.FilePath}:{result.TargetNode.StartLine}-{result.TargetNode.EndLine}");
            if (!string.IsNullOrEmpty(result.TargetNode.Signature))
                sb.AppendLine($"  Sig:  {result.TargetNode.Signature}");
            sb.AppendLine();
        }

        if (result.MatchedNodes.Count > 0 && result.TargetNode is null)
        {
            sb.AppendLine($"Matched Nodes ({result.MatchedNodes.Count}):");
            foreach (var node in result.MatchedNodes)
            {
                sb.AppendLine($"  - {node.Id} ({node.Kind})");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Subgraph: {result.Nodes.Count} nodes, {result.Edges.Count} edges");
        sb.AppendLine();

        // Group edges by type
        var edgesByType = result.Edges.GroupBy(e => e.Type).OrderBy(g => g.Key);
        foreach (var group in edgesByType)
        {
            sb.AppendLine($"{group.Key} ({group.Count()}):");
            foreach (var edge in group)
            {
                var fromName = result.Nodes.TryGetValue(edge.FromId, out var from) ? from.Name : edge.FromId;
                var toName = result.Nodes.TryGetValue(edge.ToId, out var to) ? to.Name : edge.ToId;
                sb.AppendLine($"  {fromName} -> {toName}");
            }
            sb.AppendLine();
        }

        if (result.WasTruncated)
            sb.AppendLine($"⚠ Results truncated. Showing subset of {result.TotalMatchCount} total matches.");

        return sb.ToString().TrimEnd();
    }
}
