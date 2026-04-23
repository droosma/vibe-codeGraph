using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class GraphDiffContextFormatter
{
    public static string Format(GraphDiffResult result)
    {
        var sb = new StringBuilder();
        var baseCommit = ShortCommit(result.BaseMetadata.CommitHash);
        var headCommit = ShortCommit(result.HeadMetadata.CommitHash);

        sb.AppendLine($"# Graph Diff: {baseCommit}..{headCommit}");
        sb.AppendLine();

        sb.AppendLine($"## Added Nodes ({result.AddedNodes.Count})");
        AppendNodes(sb, result.AddedNodes);
        sb.AppendLine();

        sb.AppendLine($"## Removed Nodes ({result.RemovedNodes.Count})");
        AppendNodes(sb, result.RemovedNodes);
        sb.AppendLine();

        sb.AppendLine($"## Changed Signatures ({result.SignatureChangedNodes.Count})");
        if (result.SignatureChangedNodes.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var change in result.SignatureChangedNodes)
            {
                sb.AppendLine($"- {change.Current.Id}");
                sb.AppendLine($"  - Was: {change.Previous.Signature}");
                sb.AppendLine($"  + Now: {change.Current.Signature}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"## New Edges ({result.AddedEdges.Count})");
        AppendEdges(sb, result.AddedEdges);
        sb.AppendLine();

        sb.AppendLine($"## Removed Edges ({result.RemovedEdges.Count})");
        AppendEdges(sb, result.RemovedEdges);

        return sb.ToString().TrimEnd();
    }

    private static void AppendNodes(StringBuilder sb, List<GraphNode> nodes)
    {
        if (nodes.Count == 0)
        {
            sb.AppendLine("- None");
            return;
        }

        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.FilePath))
            {
                if (node.StartLine > 0 && node.EndLine > 0)
                    sb.AppendLine($"- {node.Id} ({node.Kind}) — {node.FilePath}:{node.StartLine}-{node.EndLine}");
                else
                    sb.AppendLine($"- {node.Id} ({node.Kind}) — {node.FilePath}");
            }
            else
            {
                sb.AppendLine($"- {node.Id} ({node.Kind})");
            }
        }
    }

    private static void AppendEdges(StringBuilder sb, List<GraphEdge> edges)
    {
        if (edges.Count == 0)
        {
            sb.AppendLine("- None");
            return;
        }

        foreach (var edge in edges)
        {
            var suffix = edge.Resolution is null ? string.Empty : $", {edge.Resolution}";
            sb.AppendLine($"- {edge.FromId} → {edge.ToId} ({edge.Type}{suffix})");
        }
    }

    private static string ShortCommit(string commitHash)
    {
        if (string.IsNullOrEmpty(commitHash))
            return "unknown";

        return commitHash.Length > 7 ? commitHash[..7] : commitHash;
    }
}
