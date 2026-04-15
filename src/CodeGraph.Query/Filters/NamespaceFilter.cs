using System.Text.RegularExpressions;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Filters;

public static class NamespaceFilter
{
    public static Dictionary<string, GraphNode> Apply(Dictionary<string, GraphNode> nodes, string? namespacePattern)
    {
        if (string.IsNullOrWhiteSpace(namespacePattern))
            return nodes;

        var regex = WildcardToRegex(namespacePattern);

        return nodes
            .Where(kvp => Matches(kvp.Value, regex))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static bool Matches(GraphNode node, Regex regex)
    {
        if (node.ContainingNamespaceId is not null && regex.IsMatch(node.ContainingNamespaceId))
            return true;

        if (node.Kind == NodeKind.Namespace && regex.IsMatch(node.Id))
            return true;

        // Also match if the node Id starts with the namespace pattern
        return regex.IsMatch(node.Id);
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
