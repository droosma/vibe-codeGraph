using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class PackageQueryEngine
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly List<GraphEdge> _edges;

    public PackageQueryEngine(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
    }

    public IReadOnlyList<PackageUsage> ListPackages(string? projectFilter = null)
    {
        var groupedPackages = GetPackageEdges(projectFilter)
            .GroupBy(edge => (edge.ProjectName, edge.PackageId, edge.Version), edge => edge);

        var usages = new List<PackageUsage>();
        foreach (var group in groupedPackages)
            usages.Add(CreatePackageUsage(group));

        return usages
            .OrderBy(usage => usage.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PackageDependent> FindWhoUses(string packageName, string? projectFilter = null)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return Array.Empty<PackageDependent>();

        return GetPackageEdges(projectFilter)
            .Where(edge => edge.PackageId.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                edge => (edge.ProjectName, edge.PackageId, edge.Version, edge.ConsumerId, edge.ConsumerName, edge.ConsumerKind),
                edge => edge)
            .Select(CreatePackageDependent)
            .OrderBy(dependent => dependent.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependent => dependent.ConsumerKind)
            .ThenBy(dependent => dependent.ConsumerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PackageConflict> FindConflicts()
    {
        var versionsByPackage = BuildVersionsByPackage();

        return versionsByPackage
            .Where(entry => entry.Value.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(entry => new PackageConflict(
                entry.Key,
                new Dictionary<string, string?>(entry.Value, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(conflict => conflict.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static (string PackageId, string? Version) ParsePackageSource(string packageSource)
    {
        var slashIndex = packageSource.IndexOf('/');
        if (slashIndex > 0 && slashIndex < packageSource.Length - 1)
            return (packageSource[..slashIndex], packageSource[(slashIndex + 1)..]);

        return (packageSource, null);
    }

    private static PackageUsage CreatePackageUsage(IGrouping<(string ProjectName, string PackageId, string? Version), PackageEdge> group)
    {
        var edges = group.ToList();
        var externalSymbols = GetDistinctValues(edges.Select(edge => edge.ExternalSymbolId), StringComparer.Ordinal);
        var internalUsers = GetDistinctValues(edges.Select(edge => edge.ConsumerId), StringComparer.Ordinal);

        return new PackageUsage(
            PackageId: group.Key.PackageId,
            Version: group.Key.Version,
            ProjectName: group.Key.ProjectName,
            ExternalTypeCount: externalSymbols.Count,
            InternalUsageCount: internalUsers.Count,
            ExampleInternalUsers: internalUsers.Take(5).ToList(),
            ExampleExternalSymbols: externalSymbols.Take(5).ToList());
    }

    private static PackageDependent CreatePackageDependent(
        IGrouping<(string ProjectName, string PackageId, string? Version, string ConsumerId, string ConsumerName, NodeKind ConsumerKind), PackageEdge> group)
    {
        var externalSymbols = group
            .Select(edge => edge.ExternalSymbolId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new PackageDependent(
            PackageId: group.Key.PackageId,
            Version: group.Key.Version,
            ProjectName: group.Key.ProjectName,
            ConsumerId: group.Key.ConsumerId,
            ConsumerName: group.Key.ConsumerName,
            ConsumerKind: group.Key.ConsumerKind,
            ExternalSymbols: externalSymbols);
    }

    private static List<string> GetDistinctValues(IEnumerable<string> values, IEqualityComparer<string> comparer)
    {
        return values.Distinct(comparer).ToList();
    }

    private Dictionary<string, Dictionary<string, string?>> BuildVersionsByPackage()
    {
        var versionsByPackage = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in GetPackageEdges())
            RegisterPackageVersion(versionsByPackage, edge);

        return versionsByPackage;
    }

    private static void RegisterPackageVersion(Dictionary<string, Dictionary<string, string?>> versionsByPackage, PackageEdge edge)
    {
        if (!versionsByPackage.TryGetValue(edge.PackageId, out var versionsByProject))
        {
            versionsByProject = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            versionsByPackage[edge.PackageId] = versionsByProject;
        }

        if (!versionsByProject.ContainsKey(edge.ProjectName))
            versionsByProject[edge.ProjectName] = edge.Version;
    }

    private IEnumerable<PackageEdge> GetPackageEdges(string? projectFilter = null)
    {
        foreach (var edge in _edges)
        {
            if (TryCreatePackageEdge(edge, projectFilter, out var packageEdge))
                yield return packageEdge;
        }
    }

    private bool TryCreatePackageEdge(GraphEdge edge, string? projectFilter, out PackageEdge packageEdge)
    {
        packageEdge = null!;

        if (!edge.IsExternal || string.IsNullOrWhiteSpace(edge.PackageSource))
            return false;

        var (packageId, version) = ParsePackageSource(edge.PackageSource);
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        var projectName = GetProjectName(edge.FromId);
        if (!MatchesProjectFilter(projectName, projectFilter))
            return false;

        var consumer = GetConsumer(edge.FromId);
        packageEdge = new PackageEdge(
            ProjectName: projectName,
            PackageId: packageId,
            Version: version,
            ConsumerId: edge.FromId,
            ConsumerName: consumer.Name,
            ConsumerKind: consumer.Kind,
            ExternalSymbolId: edge.ToId);
        return true;
    }

    private static bool MatchesProjectFilter(string projectName, string? projectFilter)
    {
        return string.IsNullOrWhiteSpace(projectFilter)
            || projectName.Equals(projectFilter, StringComparison.OrdinalIgnoreCase);
    }

    private (string Name, NodeKind Kind) GetConsumer(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var node))
            return (string.IsNullOrWhiteSpace(node.Name) ? nodeId : node.Name, node.Kind);

        var lastDot = nodeId.LastIndexOf('.');
        var inferredName = lastDot >= 0 && lastDot < nodeId.Length - 1 ? nodeId[(lastDot + 1)..] : nodeId;
        var inferredKind = nodeId.Contains('(') ? NodeKind.Method : NodeKind.Type;
        return (inferredName, inferredKind);
    }

    private string GetProjectName(string nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var node) && !string.IsNullOrWhiteSpace(node.AssemblyName))
            return node.AssemblyName;

        var dotIndex = nodeId.IndexOf('.');
        return dotIndex > 0 ? nodeId[..dotIndex] : nodeId;
    }

    private sealed record PackageEdge(
        string ProjectName,
        string PackageId,
        string? Version,
        string ConsumerId,
        string ConsumerName,
        NodeKind ConsumerKind,
        string ExternalSymbolId);
}

public class PackageAnalyzer
{
    private readonly PackageQueryEngine _engine;

    public PackageAnalyzer(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _engine = new PackageQueryEngine(nodes, edges);
    }

    public IReadOnlyList<PackageUsage> AnalyzeByProject(string? projectFilter = null) => _engine.ListPackages(projectFilter);

    public IReadOnlyList<PackageUsage> AnalyzeByPackage(string packageName) => _engine.ListPackages()
        .Where(usage => usage.PackageId.Equals(packageName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    public IReadOnlyList<PackageConflict> FindConflicts() => _engine.FindConflicts();

    internal static (string PackageId, string? Version) ParsePackageSource(string packageSource) => PackageQueryEngine.ParsePackageSource(packageSource);
}

public record PackageUsage(
    string PackageId,
    string? Version,
    string ProjectName,
    int ExternalTypeCount,
    int InternalUsageCount,
    IReadOnlyList<string> ExampleInternalUsers,
    IReadOnlyList<string> ExampleExternalSymbols);

public record PackageDependent(
    string PackageId,
    string? Version,
    string ProjectName,
    string ConsumerId,
    string ConsumerName,
    NodeKind ConsumerKind,
    IReadOnlyList<string> ExternalSymbols);

public record PackageConflict(
    string PackageId,
    IReadOnlyDictionary<string, string?> VersionsByProject);
