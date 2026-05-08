using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class PackageAnalyzer
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly List<GraphEdge> _edges;

    public PackageAnalyzer(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
    }

    public IReadOnlyList<PackageUsage> AnalyzeByProject(string? projectFilter = null)
    {
        var packageEdges = _edges
            .Where(e => e.IsExternal && !string.IsNullOrEmpty(e.PackageSource))
            .ToList();

        var groups = packageEdges
            .GroupBy(e =>
            {
                var projectName = GetProjectName(e.FromId);
                var (packageId, version) = ParsePackageSource(e.PackageSource!);
                return (ProjectName: projectName, PackageId: packageId, Version: version);
            })
            .Where(g => projectFilter == null ||
                g.Key.ProjectName.Equals(projectFilter, StringComparison.OrdinalIgnoreCase));

        return BuildUsageList(groups);
    }

    public IReadOnlyList<PackageUsage> AnalyzeByPackage(string packageName)
    {
        var packageEdges = _edges
            .Where(e => e.IsExternal && !string.IsNullOrEmpty(e.PackageSource))
            .ToList();

        var groups = packageEdges
            .GroupBy(e =>
            {
                var projectName = GetProjectName(e.FromId);
                var (packageId, version) = ParsePackageSource(e.PackageSource!);
                return (ProjectName: projectName, PackageId: packageId, Version: version);
            })
            .Where(g => g.Key.PackageId.Equals(packageName, StringComparison.OrdinalIgnoreCase));

        return BuildUsageList(groups);
    }

    public IReadOnlyList<PackageConflict> FindConflicts()
    {
        var packageEdges = _edges
            .Where(e => e.IsExternal && !string.IsNullOrEmpty(e.PackageSource));

        var packageProjectVersions = new Dictionary<string, Dictionary<string, string?>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in packageEdges)
        {
            var (packageId, version) = ParsePackageSource(edge.PackageSource!);
            var projectName = GetProjectName(edge.FromId);

            if (!packageProjectVersions.TryGetValue(packageId, out var projectVersions))
            {
                projectVersions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                packageProjectVersions[packageId] = projectVersions;
            }

            if (!projectVersions.ContainsKey(projectName))
                projectVersions[projectName] = version;
        }

        var conflicts = new List<PackageConflict>();
        foreach (var (packageId, projectVersions) in packageProjectVersions)
        {
            var distinctVersions = projectVersions.Values.Distinct().ToList();
            if (distinctVersions.Count > 1)
            {
                conflicts.Add(new PackageConflict(
                    packageId,
                    new Dictionary<string, string?>(projectVersions)));
            }
        }

        return conflicts.OrderBy(c => c.PackageId).ToList();
    }

    private static IReadOnlyList<PackageUsage> BuildUsageList(
        IEnumerable<IGrouping<(string ProjectName, string PackageId, string? Version), GraphEdge>> groups)
    {
        var result = new List<PackageUsage>();

        foreach (var group in groups)
        {
            var edgesInGroup = group.ToList();
            var externalSymbols = edgesInGroup.Select(e => e.ToId).Distinct().ToList();
            var internalUsers = edgesInGroup.Select(e => e.FromId).Distinct().ToList();

            result.Add(new PackageUsage(
                PackageId: group.Key.PackageId,
                Version: group.Key.Version,
                ProjectName: group.Key.ProjectName,
                ExternalTypeCount: externalSymbols.Count,
                InternalUsageCount: internalUsers.Count,
                ExampleInternalUsers: internalUsers.Take(5).ToList(),
                ExampleExternalSymbols: externalSymbols.Take(5).ToList()));
        }

        return result.OrderBy(p => p.ProjectName).ThenBy(p => p.PackageId).ToList();
    }

    private string GetProjectName(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var node) && !string.IsNullOrEmpty(node.AssemblyName))
            return node.AssemblyName;

        var dotIndex = nodeId.IndexOf('.');
        return dotIndex > 0 ? nodeId[..dotIndex] : nodeId;
    }

    // PackageSource format: "PackageId/Version" or just "PackageId"
    internal static (string PackageId, string? Version) ParsePackageSource(string packageSource)
    {
        var slashIndex = packageSource.IndexOf('/');
        if (slashIndex > 0 && slashIndex < packageSource.Length - 1)
        {
            return (packageSource[..slashIndex], packageSource[(slashIndex + 1)..]);
        }

        return (packageSource, null);
    }
}

public record PackageUsage(
    string PackageId,
    string? Version,
    string ProjectName,
    int ExternalTypeCount,
    int InternalUsageCount,
    IReadOnlyList<string> ExampleInternalUsers,
    IReadOnlyList<string> ExampleExternalSymbols);

public record PackageConflict(
    string PackageId,
    IReadOnlyDictionary<string, string?> VersionsByProject);
