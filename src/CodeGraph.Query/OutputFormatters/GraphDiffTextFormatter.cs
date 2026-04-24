using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class GraphDiffTextFormatter
{
    public static string Format(GraphDiffResult result)
    {
        var sb = new StringBuilder();
        var baseCommit = ShortCommit(result.BaseMetadata.CommitHash);
        var headCommit = ShortCommit(result.HeadMetadata.CommitHash);

        sb.AppendLine($"Graph Diff {baseCommit}..{headCommit}");
        sb.AppendLine($"Added nodes: {result.AddedNodes.Count}");
        sb.AppendLine($"Removed nodes: {result.RemovedNodes.Count}");
        sb.AppendLine($"Signature changes: {result.SignatureChangedNodes.Count}");
        sb.AppendLine($"Added edges: {result.AddedEdges.Count}");
        sb.AppendLine($"Removed edges: {result.RemovedEdges.Count}");

        return sb.ToString().TrimEnd();
    }

    private static string ShortCommit(string commitHash)
    {
        if (string.IsNullOrEmpty(commitHash))
            return "unknown";

        return commitHash.Length > 7 ? commitHash[..7] : commitHash;
    }
}
