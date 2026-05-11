using System.Text;
using CodeGraph.Core.Models;
using CodeGraph.Query.Report;

namespace CodeGraph.Query;

public static class BriefGenerator
{
    public static string Generate(GraphMetadata metadata, Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var sb = new StringBuilder();

        // 1. Solution header
        var projectCount = nodes.Values
            .Select(n => n.AssemblyName)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct()
            .Count();
        sb.AppendLine($"# {metadata.SolutionName} — {projectCount} projects, {nodes.Count} nodes, {edges.Count} edges");
        sb.AppendLine();

        // 2. Assembly list with type/method counts
        var assemblies = nodes.Values
            .Where(n => !string.IsNullOrEmpty(n.AssemblyName))
            .GroupBy(n => n.AssemblyName)
            .OrderByDescending(g => g.Count())
            .Select(g => new
            {
                Name = g.Key,
                Types = g.Count(n => n.Kind == NodeKind.Type),
                Methods = g.Count(n => n.Kind == NodeKind.Method || n.Kind == NodeKind.Constructor)
            })
            .ToList();

        sb.AppendLine("## Assemblies");
        sb.AppendLine();
        sb.AppendLine("| Assembly | Types | Methods |");
        sb.AppendLine("|----------|------:|--------:|");
        foreach (var a in assemblies)
            sb.AppendLine($"| {a.Name} | {a.Types} | {a.Methods} |");
        sb.AppendLine();

        // 3. Hub types (top 10 most-connected)
        var hubs = HubAnalyzer.FindHubs(nodes, edges, 10);
        if (hubs.Count > 0)
        {
            sb.AppendLine("## Hub Types");
            sb.AppendLine();
            sb.AppendLine("| Type | In | Out | Total |");
            sb.AppendLine("|------|---:|----:|------:|");
            foreach (var h in hubs)
                sb.AppendLine($"| {h.Name} | {h.InDegree} | {h.OutDegree} | {h.TotalDegree} |");
            sb.AppendLine();
        }

        // 4. Key interfaces + implementations (top 10 by impl count)
        var interfaces = FindTopInterfaces(nodes, edges, 10);
        if (interfaces.Count > 0)
        {
            sb.AppendLine("## Key Interfaces");
            sb.AppendLine();
            sb.AppendLine("| Interface | Implementations |");
            sb.AppendLine("|-----------|----------------:|");
            foreach (var (name, count) in interfaces)
                sb.AppendLine($"| {name} | {count} |");
            sb.AppendLine();
        }

        // 5. Domain clusters
        var clusters = DomainClusterAnalyzer.Detect(nodes, edges);
        if (clusters.Count > 1)
        {
            sb.AppendLine("## Domain Clusters");
            sb.AppendLine();
            sb.AppendLine("| Domain | Types | Key Types |");
            sb.AppendLine("|--------|------:|-----------|");
            foreach (var c in clusters)
            {
                var keyTypes = c.KeyTypes.Count > 0 ? string.Join(", ", c.KeyTypes) : "—";
                sb.AppendLine($"| {c.Name} | {c.TypeCount} | {keyTypes} |");
            }
            sb.AppendLine();
        }

        // 6. Entry points
        var entryPoints = FindEntryPoints(nodes);
        if (entryPoints.Count > 0)
        {
            sb.AppendLine("## Entry Points");
            sb.AppendLine();
            foreach (var ep in entryPoints)
                sb.AppendLine($"- {ep}");
            sb.AppendLine();
        }

        // 7. Test coverage summary
        var coverage = CoverageAnalyzer.Analyze(nodes, edges);
        var withCoverage = coverage.Where(c => c.CoveredTypes > 0).ToList();
        if (withCoverage.Count > 0)
        {
            sb.AppendLine("## Test Coverage");
            sb.AppendLine();
            sb.AppendLine("| Assembly | Types | Covered | Coverage |");
            sb.AppendLine("|----------|------:|--------:|---------:|");
            foreach (var c in withCoverage)
                sb.AppendLine($"| {c.Assembly} | {c.TotalTypes} | {c.CoveredTypes} | {c.CoveragePercent:F1}% |");
            sb.AppendLine();
        }

        // 8. Suggested first queries
        sb.AppendLine("## Suggested Queries");
        sb.AppendLine();
        var suggestions = GenerateSuggestions(hubs, assemblies.Select(a => a.Name).ToList());
        foreach (var s in suggestions)
            sb.AppendLine($"- `{s}`");

        return sb.ToString().TrimEnd();
    }

    internal static List<(string Name, int Count)> FindTopInterfaces(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        int topN)
    {
        var implementsEdges = edges.Where(e => e.Type == EdgeType.Implements).ToList();

        return implementsEdges
            .GroupBy(e => e.ToId)
            .Where(g => nodes.ContainsKey(g.Key))
            .Select(g => (Name: nodes[g.Key].Name, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .ToList();
    }

    internal static List<string> FindEntryPoints(Dictionary<string, GraphNode> nodes)
    {
        var results = new List<string>();

        foreach (var node in nodes.Values)
        {
            if (node.Kind == NodeKind.Type)
            {
                if (node.Name.EndsWith("Controller", StringComparison.Ordinal))
                    results.Add($"Controller: {node.Name} ({node.AssemblyName})");
                else if (node.Name == "Program")
                    results.Add($"Program: {node.AssemblyName}");
                else if (node.Name.EndsWith("HostedService", StringComparison.Ordinal) ||
                         node.Metadata.ContainsKey("IsHostedService"))
                    results.Add($"HostedService: {node.Name} ({node.AssemblyName})");
            }
        }

        return results.OrderBy(r => r, StringComparer.Ordinal).ToList();
    }

    private static List<string> GenerateSuggestions(List<HubAnalyzer.HubNode> hubs, List<string> assemblies)
    {
        var suggestions = new List<string>();

        if (hubs.Count > 0)
        {
            suggestions.Add($"codegraph query {hubs[0].Name} --depth 2 --mode focused");
            suggestions.Add($"codegraph query {hubs[0].Name} --depth 1 --kind calls");
        }

        suggestions.Add("codegraph list interfaces");

        if (assemblies.Count > 0)
            suggestions.Add($"codegraph list types --assembly {assemblies[0]}");

        suggestions.Add("codegraph stats");

        return suggestions.Take(5).ToList();
    }
}
