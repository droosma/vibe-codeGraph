using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class ContextFormatter
{
    private static readonly Dictionary<EdgeType, string> OutgoingHeaders = new()
    {
        [EdgeType.Calls] = "Calls (outgoing)",
        [EdgeType.Inherits] = "Inherits",
        [EdgeType.Implements] = "Implements",
        [EdgeType.DependsOn] = "Depends on",
        [EdgeType.ResolvesTo] = "Resolves via IOC",
        [EdgeType.Covers] = "Covered by tests",
        [EdgeType.References] = "References (outgoing)",
        [EdgeType.Contains] = "Contains",
        [EdgeType.Overrides] = "Overrides",
        [EdgeType.HandlesRoute] = "Handled by",
        [EdgeType.BindsConfiguration] = "Binds configuration",
        [EdgeType.UsesMiddleware] = "Uses middleware",
        [EdgeType.MapsToTable] = "Maps to table",
        [EdgeType.NavigatesTo] = "Navigates to",
        [EdgeType.ConfiguredBy] = "Configured by"
    };

    private static readonly Dictionary<EdgeType, string> IncomingHeaders = new()
    {
        [EdgeType.Calls] = "Called by (incoming)",
        [EdgeType.Inherits] = "Inherited by",
        [EdgeType.Implements] = "Implemented by",
        [EdgeType.DependsOn] = "Depended on by",
        [EdgeType.ResolvesTo] = "Resolved from",
        [EdgeType.Covers] = "Covers",
        [EdgeType.References] = "Referenced by (incoming)",
        [EdgeType.Contains] = "Contained in",
        [EdgeType.Overrides] = "Overridden by",
        [EdgeType.HandlesRoute] = "Handles route",
        [EdgeType.BindsConfiguration] = "Configuration bound by",
        [EdgeType.UsesMiddleware] = "Middleware used by",
        [EdgeType.MapsToTable] = "Table mapped from",
        [EdgeType.NavigatesTo] = "Navigated from",
        [EdgeType.ConfiguredBy] = "Configures"
    };

    public static string Format(QueryResult result, string? queryDescription = null, bool includeSource = false, int sourceMaxLines = 20)
    {
        var sb = new StringBuilder();

        // Header
        var targetName = result.TargetNode?.Id
            ?? (result.MatchedNodes.Count > 0 ? result.MatchedNodes[0].Id : "query");
        sb.AppendLine($"# Subgraph for {targetName}");

        if (result.Metadata is not null)
        {
            var commitShort = result.Metadata.CommitHash.Length > 7
                ? result.Metadata.CommitHash[..7]
                : result.Metadata.CommitHash;
            var branch = result.Metadata.Branch;
            var date = result.Metadata.GeneratedAt.ToString("yyyy-MM-dd");
            sb.AppendLine($"## Commit: {commitShort} ({branch}, {date})");
        }

        if (queryDescription is not null)
            sb.AppendLine($"## Query: {queryDescription}");

        sb.AppendLine();

        // Target section
        if (result.TargetNode is not null)
        {
            sb.AppendLine("### Target");
            AppendNodeDetail(sb, result.TargetNode, includeSource: includeSource, sourceMaxLines: sourceMaxLines);
            sb.AppendLine();
        }
        else if (result.MatchedNodes.Count > 0)
        {
            sb.AppendLine($"### Matched Nodes ({result.MatchedNodes.Count})");
            foreach (var node in result.MatchedNodes)
            {
                AppendNodeDetail(sb, node, includeSource: includeSource, sourceMaxLines: sourceMaxLines);
            }
            sb.AppendLine();
        }

        // Determine the target IDs for edge grouping
        var targetIds = new HashSet<string>();
        if (result.TargetNode is not null)
            targetIds.Add(result.TargetNode.Id);
        else
            foreach (var n in result.MatchedNodes)
                targetIds.Add(n.Id);

        // Group outgoing edges by type
        var outgoing = result.Edges
            .Where(e => targetIds.Contains(e.FromId))
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in outgoing)
        {
            var header = OutgoingHeaders.TryGetValue(group.Key, out var h) ? h : group.Key.ToString();
            sb.AppendLine($"### {header}");
            foreach (var edge in group)
            {
                if (result.Nodes.TryGetValue(edge.ToId, out var node))
                    AppendNodeDetail(sb, node, edge, includeSource, sourceMaxLines);
                else
                    sb.AppendLine($"- {edge.ToId}");
            }
            sb.AppendLine();
        }

        // Group incoming edges by type
        var incoming = result.Edges
            .Where(e => targetIds.Contains(e.ToId) && !targetIds.Contains(e.FromId))
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in incoming)
        {
            var header = IncomingHeaders.TryGetValue(group.Key, out var h) ? h : $"{group.Key} (incoming)";
            sb.AppendLine($"### {header}");
            foreach (var edge in group)
            {
                if (result.Nodes.TryGetValue(edge.FromId, out var node))
                    AppendNodeDetail(sb, node, edge, includeSource, sourceMaxLines);
                else
                    sb.AppendLine($"- {edge.FromId}");
            }
            sb.AppendLine();
        }

        if (result.WasTruncated)
            sb.AppendLine($"⚠ Results truncated. Showing subset of {result.TotalMatchCount} total matches.");

        if (result.Suggestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Did you mean:");
            foreach (var suggestion in result.Suggestions)
                sb.AppendLine($"  - {suggestion}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendNodeDetail(StringBuilder sb, GraphNode node, GraphEdge? edge = null, bool includeSource = false, int sourceMaxLines = 20)
    {
        sb.AppendLine($"- {node.Id}");

        if (!string.IsNullOrEmpty(node.FilePath))
            sb.AppendLine($"  File: {node.FilePath}:{node.StartLine}-{node.EndLine}");

        if (!string.IsNullOrEmpty(node.Signature))
            sb.AppendLine($"  Sig:  {node.Signature}");

        if (!string.IsNullOrEmpty(node.DocComment))
            sb.AppendLine($"  Doc:  {node.DocComment}");

        if (edge?.Resolution is not null)
            sb.AppendLine($"  Resolution: {edge.Resolution}");

        if (edge?.Confidence is not null and not EdgeConfidence.Verified)
            sb.AppendLine($"  Confidence: {edge.Confidence}");

        if (includeSource)
            AppendSourceSnippet(sb, node, sourceMaxLines);
    }

    private static void AppendSourceSnippet(StringBuilder sb, GraphNode node, int sourceMaxLines)
    {
        if (string.IsNullOrEmpty(node.FilePath) || node.StartLine <= 0 || node.EndLine <= 0)
            return;

        try
        {
            if (!File.Exists(node.FilePath))
                return;

            var lines = File.ReadLines(node.FilePath)
                .Skip(node.StartLine - 1)
                .Take(node.EndLine - node.StartLine + 1)
                .ToList();

            if (lines.Count == 0)
                return;

            var maxLines = Math.Max(1, sourceMaxLines);
            sb.AppendLine("  ```csharp");
            if (lines.Count <= maxLines)
            {
                foreach (var line in lines)
                    sb.AppendLine($"  {line}");
            }
            else
            {
                foreach (var line in lines.Take(maxLines))
                    sb.AppendLine($"  {line}");
                sb.AppendLine($"  // ... ({lines.Count - maxLines} more lines)");
            }
            sb.AppendLine("  ```");
        }
        catch
        {
            // File read failure is non-fatal
        }
    }
}