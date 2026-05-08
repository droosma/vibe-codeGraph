using CodeGraph.Core.Models;

namespace CodeGraph.Query;

/// <summary>
/// Generates contextual "suggested next query" hints based on query results.
/// Helps AI agents navigate the graph efficiently by suggesting the logical next step.
/// </summary>
public static class QuerySuggestionGenerator
{
    /// <summary>
    /// Generates follow-up query suggestions (non-session-aware overload).
    /// </summary>
    public static List<string> Generate(QueryResult result, QueryOptions options, int maxSuggestions = 3)
    {
        return Generate(result, options, session: null, maxSuggestions);
    }

    /// <summary>
    /// Generates up to <paramref name="maxSuggestions"/> follow-up query suggestions,
    /// optionally session-aware to avoid repeating already-explored patterns.
    /// </summary>
    public static List<string> Generate(QueryResult result, QueryOptions options, QuerySessionTracker? session, int maxSuggestions = 3)
    {
        var suggestions = new List<string>();

        // After 3+ queries, suggest report mode
        if (session is not null && session.QueryCount >= 3)
        {
            suggestions.Add("codegraph report --format compact");
            return suggestions.Take(maxSuggestions).ToList();
        }

        if (result.Nodes.Count == 0)
            return suggestions;

        var targetNode = result.TargetNode ?? (result.MatchedNodes.Count > 0 ? result.MatchedNodes[0] : null);
        if (targetNode is null)
            return suggestions;

        var edgeTypes = result.Edges
            .Select(e => e.Type)
            .Distinct()
            .ToHashSet();

        var hasInterfaces = result.Nodes.Values.Any(n =>
            n.Kind == NodeKind.Type && n.Name.StartsWith("I") && n.Name.Length > 1 && char.IsUpper(n.Name[1]));

        var hasImplementations = edgeTypes.Contains(EdgeType.Implements) || edgeTypes.Contains(EdgeType.ResolvesTo);
        var hasCalls = edgeTypes.Contains(EdgeType.Calls);
        var hasInheritance = edgeTypes.Contains(EdgeType.Inherits);

        // Suggest deeper traversal if at depth 1, unless already queried at depth 2+
        if (options.Depth <= 1 && result.Edges.Count > 0)
        {
            if (session is null || !session.WasQueriedAtDepth(targetNode.Name, 2))
            {
                suggestions.Add($"codegraph query {Quote(targetNode.Name)} --depth 2 --format compact");
            }
        }

        // Suggest call chain exploration
        if (hasCalls && options.EdgeTypeFilter != EdgeType.Calls)
        {
            if (session is null || !session.WasQueried(targetNode.Name + " --kind calls"))
            {
                suggestions.Add($"codegraph query {Quote(targetNode.Name)} --depth 3 --kind calls --format compact");
            }
        }

        // Suggest DI wiring for interfaces
        if (hasInterfaces && !hasImplementations)
        {
            var iface = result.Nodes.Values.FirstOrDefault(n =>
                n.Kind == NodeKind.Type && n.Name.StartsWith("I") && n.Name.Length > 1 && char.IsUpper(n.Name[1]));
            if (iface is not null)
            {
                suggestions.Add($"codegraph query {Quote(iface.Name)} --kind resolves-to --format compact");
            }
        }

        // Suggest implementation exploration for interfaces found in results
        if (hasImplementations && options.EdgeTypeFilter != EdgeType.Implements)
        {
            var implementedTypes = result.Edges
                .Where(e => e.Type == EdgeType.Implements || e.Type == EdgeType.ResolvesTo)
                .Select(e => result.Nodes.TryGetValue(e.ToId, out var n) ? n : null)
                .Where(n => n is not null)
                .Select(n => n!.Name)
                .Distinct()
                .Take(1);

            foreach (var typeName in implementedTypes)
            {
                suggestions.Add($"codegraph query {Quote(typeName)} --kind implements --format compact");
            }
        }

        // Suggest inheritance exploration
        if (hasInheritance && options.EdgeTypeFilter != EdgeType.Inherits)
        {
            suggestions.Add($"codegraph query {Quote(targetNode.Name)} --kind inherits --depth 3 --format compact");
        }

        // Suggest unexplored neighbors from the result
        if (session is not null && result.Nodes.Count > 1)
        {
            var unexplored = result.Nodes.Values
                .Where(n => n.Kind == NodeKind.Type && n.Id != targetNode.Id && !session.WasQueried(n.Name))
                .Take(2);
            foreach (var n in unexplored)
            {
                suggestions.Add($"codegraph query {Quote(n.Name)} --depth 1 --format compact");
            }
        }

        // Suggest cross-project exploration if result spans multiple assemblies
        var assemblies = result.Nodes.Values
            .Select(n => n.AssemblyName)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct()
            .ToList();

        if (assemblies.Count > 1 && options.ProjectFilter is null)
        {
            var otherAssembly = assemblies.FirstOrDefault(a => a != targetNode.AssemblyName);
            if (otherAssembly is not null)
            {
                suggestions.Add($"codegraph query {Quote(targetNode.Name)} --project {Quote(otherAssembly)} --format compact");
            }
        }

        return suggestions.Take(maxSuggestions).ToList();
    }

    /// <summary>
    /// Formats suggestions as a hint block for CLI output.
    /// </summary>
    public static string FormatHints(List<string> suggestions)
    {
        if (suggestions.Count == 0)
            return string.Empty;

        var lines = new List<string> { "", "💡 Suggested next queries:" };
        foreach (var s in suggestions)
            lines.Add($"   {s}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Quote(string value)
        => value.Contains(' ') || value.Contains('*') ? $"\"{value}\"" : value;
}
