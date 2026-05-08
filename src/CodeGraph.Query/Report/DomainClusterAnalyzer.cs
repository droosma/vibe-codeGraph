using CodeGraph.Core.Models;

namespace CodeGraph.Query.Report;

public static class DomainClusterAnalyzer
{
    public record DomainCluster(
        string Name,
        List<string> Assemblies,
        List<string> KeyTypes,
        int TypeCount,
        List<CrossDomainEdge> CrossDomainConnections);

    public record CrossDomainEdge(string TargetDomain, int EdgeCount);

    /// <summary>
    /// Detects domain clusters by grouping assemblies by their last meaningful namespace segment.
    /// Strategy: Extract the domain name from the deepest segment of the assembly name
    /// (e.g., "Vertimart.Exquise.BSExquise.Modules.Financien" → "Financien").
    /// For short assembly names (≤2 segments), use the full name as the domain.
    /// </summary>
    public static List<DomainCluster> Detect(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges,
        int topKeyTypes = 3)
    {
        // Build node→assembly lookup
        var nodeAssembly = nodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AssemblyName);

        // Group assemblies by domain name
        var assemblyDomains = nodes.Values
            .Where(n => !string.IsNullOrEmpty(n.AssemblyName))
            .Select(n => n.AssemblyName)
            .Distinct()
            .GroupBy(ExtractDomainName)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        if (assemblyDomains.Count == 0)
            return [];

        // Compute degree per type node (excluding Contains edges)
        var degree = new Dictionary<string, int>();
        foreach (var edge in edges.Where(e => e.Type != EdgeType.Contains))
        {
            degree[edge.FromId] = degree.GetValueOrDefault(edge.FromId) + 1;
            degree[edge.ToId] = degree.GetValueOrDefault(edge.ToId) + 1;
        }

        // Build assembly→domain reverse lookup
        var assemblyToDomain = new Dictionary<string, string>();
        foreach (var (domain, assemblies) in assemblyDomains)
        {
            foreach (var asm in assemblies)
                assemblyToDomain[asm] = domain;
        }

        // For each domain, find types, key types, and cross-domain edges
        var results = new List<DomainCluster>();
        foreach (var (domain, assemblies) in assemblyDomains)
        {
            var assemblySet = new HashSet<string>(assemblies);
            var domainNodes = nodes.Values
                .Where(n => n.Kind == NodeKind.Type && assemblySet.Contains(n.AssemblyName))
                .ToList();

            var typeCount = domainNodes.Count;
            if (typeCount == 0)
                continue;

            // Key types: highest degree within this cluster
            var keyTypes = domainNodes
                .OrderByDescending(n => degree.GetValueOrDefault(n.Id))
                .Take(topKeyTypes)
                .Where(n => degree.GetValueOrDefault(n.Id) > 0)
                .Select(n => n.Name)
                .ToList();

            // Cross-domain edges: count edges from this domain to other domains
            var domainNodeIds = new HashSet<string>(
                nodes.Values.Where(n => assemblySet.Contains(n.AssemblyName)).Select(n => n.Id));

            var crossEdges = edges
                .Where(e => e.Type != EdgeType.Contains)
                .Where(e =>
                    (domainNodeIds.Contains(e.FromId) && !domainNodeIds.Contains(e.ToId)) ||
                    (!domainNodeIds.Contains(e.FromId) && domainNodeIds.Contains(e.ToId)))
                .Select(e =>
                {
                    var otherId = domainNodeIds.Contains(e.FromId) ? e.ToId : e.FromId;
                    if (nodeAssembly.TryGetValue(otherId, out var otherAsm) &&
                        assemblyToDomain.TryGetValue(otherAsm, out var otherDomain))
                        return otherDomain;
                    return null;
                })
                .Where(d => d is not null && d != domain)
                .GroupBy(d => d!)
                .Select(g => new CrossDomainEdge(g.Key, g.Count()))
                .OrderByDescending(c => c.EdgeCount)
                .Take(3)
                .ToList();

            var shortAssemblies = assemblies
                .Select(ShortenAssemblyName)
                .ToList();

            results.Add(new DomainCluster(domain, shortAssemblies, keyTypes, typeCount, crossEdges));
        }

        return results
            .OrderByDescending(c => c.TypeCount)
            .ToList();
    }

    /// <summary>
    /// Extracts a domain name from an assembly name.
    /// For multi-segment names (≥3 parts), takes the last segment.
    /// For short names (≤2 segments), uses the full name.
    /// </summary>
    internal static string ExtractDomainName(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return string.Empty;

        var parts = assemblyName.Split('.');
        if (parts.Length <= 2)
            return assemblyName;

        return parts[^1];
    }

    /// <summary>
    /// Shortens an assembly name by removing common prefixes, keeping the last 2 segments.
    /// </summary>
    private static string ShortenAssemblyName(string assemblyName)
    {
        var parts = assemblyName.Split('.');
        if (parts.Length <= 2)
            return assemblyName;

        return string.Join(".", parts[^2..]);
    }
}
