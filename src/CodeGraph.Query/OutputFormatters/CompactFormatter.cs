using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class CompactFormatter
{
    public static string Format(QueryResult result, bool includeSource = false)
    {
        var sb = new StringBuilder();

        // Detect common namespace prefix for compression
        var prefix = DetectCommonPrefix(result.Nodes.Keys);

        // Header
        var targetName = result.TargetNode?.Id ?? result.MatchedNodes.FirstOrDefault()?.Id ?? "query";
        sb.AppendLine($"# {StripPrefix(targetName, prefix)}");
        sb.AppendLine();

        // Determine target IDs
        var targetIds = new HashSet<string>();
        if (result.TargetNode is not null)
            targetIds.Add(result.TargetNode.Id);
        else
            foreach (var n in result.MatchedNodes)
                targetIds.Add(n.Id);

        // Format each target node with its relationships
        foreach (var targetId in targetIds)
        {
            if (!result.Nodes.TryGetValue(targetId, out var targetNode))
                continue;

            AppendNodeBlock(sb, targetNode, targetIds, result, prefix, includeSource);
        }

        // Format remaining nodes (non-target matched)
        var remainingNodes = result.Nodes.Values
            .Where(n => !targetIds.Contains(n.Id))
            .OrderBy(n => n.Kind)
            .ThenBy(n => n.Id);

        var hasRemainingSection = false;
        foreach (var node in remainingNodes)
        {
            if (!hasRemainingSection)
            {
                sb.AppendLine("## Related");
                hasRemainingSection = true;
            }
            AppendCompactNode(sb, node, prefix);
        }

        if (result.WasTruncated)
            sb.AppendLine($"\n⚠ Truncated ({result.TotalMatchCount} total matches)");

        if (result.Suggestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Did you mean:");
            foreach (var suggestion in result.Suggestions)
                sb.AppendLine($"  - {suggestion}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendNodeBlock(StringBuilder sb, GraphNode node, HashSet<string> targetIds, QueryResult result, string prefix, bool includeSource)
    {
        // Node header: Name [kind, file:lines]
        var shortId = StripPrefix(node.Id, prefix);
        var fileInfo = !string.IsNullOrEmpty(node.FilePath)
            ? $", {node.FilePath}:{node.StartLine}-{node.EndLine}"
            : "";
        sb.AppendLine($"## {shortId} [{node.Kind.ToString().ToLowerInvariant()}{fileInfo}]");

        if (!string.IsNullOrEmpty(node.DocComment))
            sb.AppendLine($"  {node.DocComment}");

        if (includeSource)
            AppendSourceSnippet(sb, node);

        // Group outgoing edges by type
        var outgoing = result.Edges
            .Where(e => e.FromId == node.Id)
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in outgoing)
        {
            var targets = group.Select(e =>
            {
                var name = StripPrefix(e.ToId, prefix);
                return e.Confidence != EdgeConfidence.Verified
                    ? $"{name} [{e.Confidence.ToString().ToLowerInvariant()}]"
                    : name;
            }).ToList();
            sb.AppendLine($"  → {FormatEdgeType(group.Key)}: {string.Join(", ", targets)}");
        }

        // Group incoming edges by type (exclude from other targets)
        var incoming = result.Edges
            .Where(e => e.ToId == node.Id && !targetIds.Contains(e.FromId))
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in incoming)
        {
            var sources = group.Select(e =>
            {
                var name = StripPrefix(e.FromId, prefix);
                return e.Confidence != EdgeConfidence.Verified
                    ? $"{name} [{e.Confidence.ToString().ToLowerInvariant()}]"
                    : name;
            }).ToList();
            sb.AppendLine($"  ← {FormatEdgeType(group.Key)}: {string.Join(", ", sources)}");
        }

        sb.AppendLine();
    }

    private static void AppendSourceSnippet(StringBuilder sb, GraphNode node)
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

            sb.AppendLine("  ```csharp");
            const int maxLines = 20;
            const int previewLines = 15;
            if (lines.Count <= maxLines)
            {
                foreach (var line in lines)
                    sb.AppendLine($"  {line}");
            }
            else
            {
                foreach (var line in lines.Take(previewLines))
                    sb.AppendLine($"  {line}");
                sb.AppendLine($"  // ... ({lines.Count - previewLines} more lines)");
            }
            sb.AppendLine("  ```");
        }
        catch
        {
            // File read failure is non-fatal
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

        // Trim to last dot boundary (we want full namespace segments)
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
}