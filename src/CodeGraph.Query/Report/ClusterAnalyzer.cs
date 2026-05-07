using CodeGraph.Core.Models;

namespace CodeGraph.Query.Report;

public static class ClusterAnalyzer
{
    public record ClusterInfo(string Assembly, int NodeCount, int InternalEdges, int ExternalEdges);

    public static List<ClusterInfo> Analyze(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var nodeAssembly = nodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AssemblyName);

        return nodes.Values
            .GroupBy(n => n.AssemblyName)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var assemblyNodeIds = new HashSet<string>(g.Select(n => n.Id));
                var internalEdges = edges.Count(e =>
                    assemblyNodeIds.Contains(e.FromId) && assemblyNodeIds.Contains(e.ToId));
                var externalEdges = edges.Count(e =>
                    (assemblyNodeIds.Contains(e.FromId) && !assemblyNodeIds.Contains(e.ToId)) ||
                    (!assemblyNodeIds.Contains(e.FromId) && assemblyNodeIds.Contains(e.ToId)));

                return new ClusterInfo(g.Key, g.Count(), internalEdges, externalEdges);
            })
            .OrderByDescending(c => c.NodeCount)
            .ToList();
    }
}
