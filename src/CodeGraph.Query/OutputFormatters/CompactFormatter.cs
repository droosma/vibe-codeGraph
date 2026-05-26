using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class CompactFormatter
{
    public static string Format(QueryResult result, bool includeSource = false, bool includeDocs = true, int sourceMaxLines = 20)
    {
        var sb = new StringBuilder();
        var prefix = DetectCommonPrefix(result.Nodes.Keys);

        AppendHeader(sb, result, prefix);

        var targetNodeIds = GetTargetNodeIds(result);
        var targetIds = new HashSet<string>(targetNodeIds, StringComparer.Ordinal);
        var documentationNodeId = GetDocumentationNodeId(result);

        AppendTargetSections(sb, result, targetNodeIds, targetIds, prefix, includeSource, includeDocs, documentationNodeId, sourceMaxLines);
        AppendRelatedNodesSection(sb, result, targetIds, prefix);
        AppendTruncationWarning(sb, result);
        AppendSuggestions(sb, result.Suggestions);

        return sb.ToString().TrimEnd();
    }

    private static void AppendHeader(StringBuilder sb, QueryResult result, string prefix)
    {
        var targetName = result.TargetNode?.Id ?? result.MatchedNodes.FirstOrDefault()?.Id ?? "query";
        sb.AppendLine($"# {StripPrefix(targetName, prefix)}");
        sb.AppendLine();
    }

    private static IReadOnlyList<string> GetTargetNodeIds(QueryResult result)
    {
        if (result.TargetNode is not null)
            return [result.TargetNode.Id];

        return result.MatchedNodes.Select(node => node.Id).ToList();
    }

    private static string? GetDocumentationNodeId(QueryResult result)
    {
        return result.TargetNode?.Id ?? result.MatchedNodes.FirstOrDefault()?.Id;
    }

    private static void AppendTargetSections(
        StringBuilder sb,
        QueryResult result,
        IReadOnlyList<string> targetNodeIds,
        HashSet<string> targetIds,
        string prefix,
        bool includeSource,
        bool includeDocs,
        string? documentationNodeId,
        int sourceMaxLines)
    {
        foreach (var targetNodeId in targetNodeIds)
        {
            if (!result.Nodes.TryGetValue(targetNodeId, out var targetNode))
                continue;

            AppendNodeBlock(
                sb,
                targetNode,
                targetIds,
                result,
                prefix,
                includeSource,
                includeDocs && string.Equals(targetNode.Id, documentationNodeId, StringComparison.Ordinal),
                sourceMaxLines);
        }
    }

    private static void AppendRelatedNodesSection(StringBuilder sb, QueryResult result, HashSet<string> targetIds, string prefix)
    {
        var remainingNodes = result.Nodes.Values
            .Where(node => !targetIds.Contains(node.Id))
            .OrderBy(node => node.Kind)
            .ThenBy(node => node.Id);

        var wroteHeader = false;
        foreach (var node in remainingNodes)
        {
            if (!wroteHeader)
            {
                sb.AppendLine("## Related");
                wroteHeader = true;
            }

            AppendCompactNode(sb, node, prefix);
        }
    }

    private static void AppendTruncationWarning(StringBuilder sb, QueryResult result)
    {
        if (result.WasTruncated)
            sb.AppendLine($"\n⚠ Truncated ({result.TotalMatchCount} total matches)");
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

    private static void AppendNodeBlock(
        StringBuilder sb,
        GraphNode node,
        HashSet<string> targetIds,
        QueryResult result,
        string prefix,
        bool includeSource,
        bool showDocComment,
        int sourceMaxLines)
    {
        var shortId = StripPrefix(node.Id, prefix);
        var fileInfo = !string.IsNullOrEmpty(node.FilePath)
            ? $", {node.FilePath}:{node.StartLine}-{node.EndLine}"
            : string.Empty;
        var summary = showDocComment ? ExtractSummary(node.DocComment) : string.Empty;
        var inlineComment = !string.IsNullOrEmpty(summary) ? $" // {summary}" : string.Empty;

        sb.AppendLine($"## {shortId} [{node.Kind.ToString().ToLowerInvariant()}{fileInfo}]{inlineComment}");

        if (includeSource)
            AppendSourceSnippet(sb, node, sourceMaxLines);

        var outgoingGroups = result.Edges
            .Where(edge => edge.FromId == node.Id)
            .GroupBy(edge => edge.Type)
            .OrderBy(group => group.Key);

        AppendEdgeGroups(sb, outgoingGroups, "→", edge => edge.ToId, prefix);

        var incomingGroups = result.Edges
            .Where(edge => edge.ToId == node.Id && !targetIds.Contains(edge.FromId))
            .GroupBy(edge => edge.Type)
            .OrderBy(group => group.Key);

        AppendEdgeGroups(sb, incomingGroups, "←", edge => edge.FromId, prefix);
        sb.AppendLine();
    }

    private static void AppendEdgeGroups(
        StringBuilder sb,
        IEnumerable<IGrouping<EdgeType, GraphEdge>> edgeGroups,
        string arrow,
        Func<GraphEdge, string> getConnectedNodeId,
        string prefix)
    {
        foreach (var group in edgeGroups)
        {
            var connectedNodes = group
                .Select(edge => FormatConnectedNode(edge, getConnectedNodeId(edge), prefix))
                .ToList();

            sb.AppendLine($"  {arrow} {FormatEdgeType(group.Key)}: {string.Join(", ", connectedNodes)}");
        }
    }

    private static string FormatConnectedNode(GraphEdge edge, string connectedNodeId, string prefix)
    {
        var shortId = StripPrefix(connectedNodeId, prefix);
        return edge.Confidence != EdgeConfidence.Verified
            ? $"{shortId} [{edge.Confidence.ToString().ToLowerInvariant()}]"
            : shortId;
    }

    private static void AppendSourceSnippet(StringBuilder sb, GraphNode node, int sourceMaxLines)
    {
        if (string.IsNullOrEmpty(node.FilePath) || node.StartLine <= 0 || node.EndLine <= 0)
        {
            sb.AppendLine("  ⚠ source unavailable (no file path or line range)");
            return;
        }

        try
        {
            if (!File.Exists(node.FilePath))
            {
                sb.AppendLine($"  ⚠ source unavailable ({node.FilePath} not found)");
                return;
            }

            var lines = File.ReadLines(node.FilePath)
                .Skip(node.StartLine - 1)
                .Take(node.EndLine - node.StartLine + 1)
                .ToList();

            if (lines.Count == 0)
                return;

            var maxLines = Math.Max(1, sourceMaxLines);
            sb.AppendLine($"  ```csharp  // {node.FilePath}:{node.StartLine}-{node.EndLine}");

            if (lines.Count <= maxLines)
            {
                foreach (var line in lines)
                    sb.AppendLine($"  {line}");
            }
            else
            {
                foreach (var line in lines.Take(maxLines))
                    sb.AppendLine($"  {line}");

                sb.AppendLine($"  // ... truncated ({lines.Count - maxLines} more lines) — read {node.FilePath}:{node.StartLine + maxLines}-{node.EndLine} for full source");
            }

            sb.AppendLine("  ```");
        }
        catch
        {
            sb.AppendLine($"  ⚠ source unavailable (could not read {node.FilePath})");
        }
    }

    private static void AppendCompactNode(StringBuilder sb, GraphNode node, string prefix)
    {
        var shortId = StripPrefix(node.Id, prefix);
        sb.AppendLine($"- {shortId} [{node.Kind.ToString().ToLowerInvariant()}]");
    }

    private static string FormatEdgeType(EdgeType type) => type switch
    {
        EdgeType.Calls => "calls",
        EdgeType.Inherits => "inherits",
        EdgeType.Implements => "implements",
        EdgeType.DependsOn => "depends-on",
        EdgeType.ResolvesTo => "resolves-to",
        EdgeType.Covers => "covers",
        EdgeType.CoveredBy => "covered-by",
        EdgeType.References => "references",
        EdgeType.Contains => "contains",
        EdgeType.Overrides => "overrides",
        EdgeType.HandlesRoute => "handles-route",
        EdgeType.BindsConfiguration => "binds-configuration",
        EdgeType.UsesMiddleware => "uses-middleware",
        EdgeType.MapsToTable => "maps-to-table",
        EdgeType.NavigatesTo => "navigates-to",
        EdgeType.ConfiguredBy => "configured-by",
        _ => type.ToString().ToLowerInvariant()
    };

    internal static string DetectCommonPrefix(IEnumerable<string> ids)
    {
        var idList = ids.ToList();
        if (idList.Count <= 1)
            return string.Empty;

        var first = idList[0];
        var prefixLength = first.Length;

        foreach (var id in idList.Skip(1))
        {
            prefixLength = Math.Min(prefixLength, id.Length);
            for (var i = 0; i < prefixLength; i++)
            {
                if (id[i] != first[i])
                {
                    prefixLength = i;
                    break;
                }
            }
        }

        var prefix = first[..prefixLength];
        var lastDot = prefix.LastIndexOf('.');
        return lastDot > 0 ? prefix[..(lastDot + 1)] : string.Empty;
    }

    internal static string StripPrefix(string id, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return id;

        return id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : id;
    }

    internal static string ExtractSummary(string? docComment)
    {
        if (string.IsNullOrWhiteSpace(docComment))
            return string.Empty;

        var text = TryExtractXmlSummary(docComment, out var summaryText)
            ? summaryText
            : docComment;

        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const int maxLength = 120;
        if (text.Length > maxLength)
            text = string.Concat(text.AsSpan(0, maxLength), "\u2026");

        return text;
    }

    private static bool TryExtractXmlSummary(string docComment, out string summary)
    {
        summary = string.Empty;
        if (!docComment.Contains('<'))
            return false;

        try
        {
            var wrapped = $"<root>{docComment}</root>";
            var doc = XDocument.Parse(wrapped);
            var summaryElement = doc.Root?.Element("summary");
            if (summaryElement is null)
                return true;

            summary = summaryElement.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
