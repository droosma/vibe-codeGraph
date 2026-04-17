using CodeGraph.Core.Models;

namespace CodeGraph.Query.Filters;

public static class EdgeTypeFilter
{
    private static readonly Dictionary<string, EdgeType?> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calls-to"] = EdgeType.Calls,
        ["calls"] = EdgeType.Calls,
        ["calls-from"] = EdgeType.Calls,
        ["inherits"] = EdgeType.Inherits,
        ["implements"] = EdgeType.Implements,
        ["depends-on"] = EdgeType.DependsOn,
        ["resolves-to"] = EdgeType.ResolvesTo,
        ["covers"] = EdgeType.Covers,
        ["covered-by"] = EdgeType.CoveredBy,
        ["references"] = EdgeType.References,
        ["overrides"] = EdgeType.Overrides,
        ["contains"] = EdgeType.Contains,
        ["all"] = null
    };

    public static EdgeType? Parse(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        if (Mappings.TryGetValue(kind, out var edgeType))
            return edgeType;

        if (Enum.TryParse<EdgeType>(kind, ignoreCase: true, out var parsed))
            return parsed;

        throw new ArgumentException($"Unknown edge type '{kind}'. Valid values: {string.Join(", ", Mappings.Keys)}");
    }

    public static List<GraphEdge> Apply(List<GraphEdge> edges, EdgeType? filter)
    {
        if (filter is null)
            return edges;

        return edges.Where(e => e.Type == filter.Value).ToList();
    }
}
