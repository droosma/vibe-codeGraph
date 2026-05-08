using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

/// <summary>
/// Formats a <see cref="GraphDiffResult"/> as a PR review-friendly summary
/// with changes grouped into semantic categories (type changes, signature changes,
/// call graph changes, DI changes, route changes).
/// </summary>
public static class GraphDiffGroupFormatter
{
    public static string Format(GraphDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        var baseCommit = ShortCommit(result.BaseMetadata.CommitHash);
        var headCommit = ShortCommit(result.HeadMetadata.CommitHash);

        sb.AppendLine($"# PR Review: {baseCommit}..{headCommit}");
        sb.AppendLine();

        var groups = Categorize(result);
        var totalChanges = groups.Sum(g => g.Items.Count);

        sb.AppendLine($"**{totalChanges} changes** across {groups.Count(g => g.Items.Count > 0)} categories");
        sb.AppendLine();

        foreach (var group in groups)
        {
            if (group.Items.Count == 0)
                continue;

            sb.AppendLine($"## {group.Title} ({group.Items.Count})");
            foreach (var item in group.Items)
            {
                sb.AppendLine($"- {item}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    internal static List<ChangeGroup> Categorize(GraphDiffResult result)
    {
        var typeChanges = new ChangeGroup("Type Changes");
        var signatureChanges = new ChangeGroup("Signature Changes");
        var callGraphChanges = new ChangeGroup("Call Graph Changes");
        var diChanges = new ChangeGroup("DI Changes");
        var routeChanges = new ChangeGroup("Route Changes");
        var otherChanges = new ChangeGroup("Other Changes");

        // Categorize added nodes
        foreach (var node in result.AddedNodes)
        {
            if (node.Kind == NodeKind.Type)
                typeChanges.Items.Add($"+ {node.Id} (added)");
            else
                otherChanges.Items.Add($"+ {node.Id} ({node.Kind}, added)");
        }

        // Categorize removed nodes
        foreach (var node in result.RemovedNodes)
        {
            if (node.Kind == NodeKind.Type)
                typeChanges.Items.Add($"- {node.Id} (removed)");
            else
                otherChanges.Items.Add($"- {node.Id} ({node.Kind}, removed)");
        }

        // Signature changes
        foreach (var change in result.SignatureChangedNodes)
        {
            signatureChanges.Items.Add(
                $"~ {change.Current.Id}: {change.Previous.Signature} → {change.Current.Signature}");
        }

        // Categorize added edges
        foreach (var edge in result.AddedEdges)
        {
            CategorizeEdge(edge, "+", callGraphChanges, diChanges, routeChanges, otherChanges);
        }

        // Categorize removed edges
        foreach (var edge in result.RemovedEdges)
        {
            CategorizeEdge(edge, "-", callGraphChanges, diChanges, routeChanges, otherChanges);
        }

        return new List<ChangeGroup>
        {
            typeChanges,
            signatureChanges,
            callGraphChanges,
            diChanges,
            routeChanges,
            otherChanges
        };
    }

    private static void CategorizeEdge(
        GraphEdge edge,
        string prefix,
        ChangeGroup callGraphChanges,
        ChangeGroup diChanges,
        ChangeGroup routeChanges,
        ChangeGroup otherChanges)
    {
        var label = FormatEdge(edge);

        switch (edge.Type)
        {
            case EdgeType.Calls:
            case EdgeType.References:
            case EdgeType.Overrides:
                callGraphChanges.Items.Add($"{prefix} {label}");
                break;

            case EdgeType.ResolvesTo:
            case EdgeType.DependsOn:
            case EdgeType.ConfiguredBy:
                diChanges.Items.Add($"{prefix} {label}");
                break;

            case EdgeType.HandlesRoute:
            case EdgeType.UsesMiddleware:
                routeChanges.Items.Add($"{prefix} {label}");
                break;

            default:
                otherChanges.Items.Add($"{prefix} {label}");
                break;
        }
    }

    private static string FormatEdge(GraphEdge edge)
    {
        var suffix = edge.Resolution is null ? string.Empty : $", {edge.Resolution}";
        return $"{edge.FromId} → {edge.ToId} ({edge.Type}{suffix})";
    }

    private static string ShortCommit(string commitHash)
    {
        if (string.IsNullOrEmpty(commitHash))
            return "unknown";

        return commitHash.Length > 7 ? commitHash[..7] : commitHash;
    }

    internal record ChangeGroup(string Title)
    {
        public List<string> Items { get; } = new();
    }
}
