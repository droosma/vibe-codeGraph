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

        AppendHeader(sb, result, queryDescription);
        AppendTargetSection(sb, result, includeSource, sourceMaxLines);

        var targetNodeIds = GetTargetNodeIds(result);
        var targetIds = new HashSet<string>(targetNodeIds, StringComparer.Ordinal);

        AppendEdgeSections(sb, result, targetIds, includeSource, sourceMaxLines, outgoing: true);
        AppendEdgeSections(sb, result, targetIds, includeSource, sourceMaxLines, outgoing: false);
        AppendTruncationWarning(sb, result);
        AppendSuggestions(sb, result.Suggestions);

        return sb.ToString().TrimEnd();
    }

    private static void AppendHeader(StringBuilder sb, QueryResult result, string? queryDescription)
    {
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
    }

    private static void AppendTargetSection(StringBuilder sb, QueryResult result, bool includeSource, int sourceMaxLines)
    {
        if (result.TargetNode is not null)
        {
            sb.AppendLine("### Target");
            AppendNodeDetail(sb, result.TargetNode, includeSource: includeSource, sourceMaxLines: sourceMaxLines);
            sb.AppendLine();
            return;
        }

        if (result.MatchedNodes.Count == 0)
            return;

        sb.AppendLine($"### Matched Nodes ({result.MatchedNodes.Count})");
        foreach (var node in result.MatchedNodes)
            AppendNodeDetail(sb, node, includeSource: includeSource, sourceMaxLines: sourceMaxLines);

        sb.AppendLine();
    }

    private static IReadOnlyList<string> GetTargetNodeIds(QueryResult result)
    {
        if (result.TargetNode is not null)
            return [result.TargetNode.Id];

        return result.MatchedNodes.Select(node => node.Id).ToList();
    }

    private static void AppendEdgeSections(
        StringBuilder sb,
        QueryResult result,
        HashSet<string> targetIds,
        bool includeSource,
        int sourceMaxLines,
        bool outgoing)
    {
        var edgeGroups = outgoing
            ? result.Edges
                .Where(edge => targetIds.Contains(edge.FromId))
                .GroupBy(edge => edge.Type)
                .OrderBy(group => group.Key)
            : result.Edges
                .Where(edge => targetIds.Contains(edge.ToId) && !targetIds.Contains(edge.FromId))
                .GroupBy(edge => edge.Type)
                .OrderBy(group => group.Key);

        foreach (var group in edgeGroups)
        {
            var header = ResolveHeader(group.Key, outgoing);
            sb.AppendLine($"### {header}");

            foreach (var edge in group)
            {
                var nodeId = outgoing ? edge.ToId : edge.FromId;
                if (result.Nodes.TryGetValue(nodeId, out var node))
                    AppendNodeDetail(sb, node, edge, includeSource, sourceMaxLines);
                else
                    sb.AppendLine($"- {nodeId}");
            }

            sb.AppendLine();
        }
    }

    private static string ResolveHeader(EdgeType edgeType, bool outgoing)
    {
        if (outgoing)
            return OutgoingHeaders.TryGetValue(edgeType, out var header) ? header : edgeType.ToString();

        return IncomingHeaders.TryGetValue(edgeType, out var incomingHeader)
            ? incomingHeader
            : $"{edgeType} (incoming)";
    }

    private static void AppendTruncationWarning(StringBuilder sb, QueryResult result)
    {
        if (result.WasTruncated)
            sb.AppendLine($"⚠ Results truncated. Showing subset of {result.TotalMatchCount} total matches.");
    }

    private static void AppendSuggestions(StringBuilder sb, IReadOnlyList<string> suggestions)
    {
        if (suggestions.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Did you mean:");
        foreach (var suggestion in suggestions)
            sb.AppendLine($"  - {suggestion}");
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
