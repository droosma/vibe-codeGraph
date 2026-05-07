using CodeGraph.Core.Models;

namespace CodeGraph.Query.Report;

public static class CoverageAnalyzer
{
    public record CoverageInfo(string Assembly, int TotalTypes, int CoveredTypes, double CoveragePercent);

    public static List<CoverageInfo> Analyze(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var coveredIds = new HashSet<string>(
            edges.Where(e => e.Type == EdgeType.CoveredBy || e.Type == EdgeType.Covers)
                 .SelectMany(e => new[] { e.FromId, e.ToId }));

        return nodes.Values
            .Where(n => n.Kind == NodeKind.Type)
            .GroupBy(n => n.AssemblyName)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var total = g.Count();
                var covered = g.Count(n => coveredIds.Contains(n.Id));
                return new CoverageInfo(g.Key, total, covered, total > 0 ? (double)covered / total * 100 : 0);
            })
            .OrderBy(c => c.CoveragePercent)
            .ToList();
    }
}
