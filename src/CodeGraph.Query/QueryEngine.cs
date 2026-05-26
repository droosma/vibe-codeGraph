using System.Text.RegularExpressions;
using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;
using CodeGraph.Query.Metrics;

namespace CodeGraph.Query;

public class QueryEngine
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly List<GraphEdge> _edges;
    private readonly GraphMetadata _metadata;
    private readonly Dictionary<string, List<GraphEdge>> _outgoing;
    private readonly Dictionary<string, List<GraphEdge>> _incoming;

    public QueryEngine(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges, GraphMetadata metadata)
    {
        _nodes = nodes;
        _edges = edges;
        _metadata = metadata;

        _outgoing = new Dictionary<string, List<GraphEdge>>();
        _incoming = new Dictionary<string, List<GraphEdge>>();

        foreach (var edge in edges)
        {
            AddEdgeLookupEntry(_outgoing, edge.FromId, edge);
            AddEdgeLookupEntry(_incoming, edge.ToId, edge);
        }
    }

    public Dictionary<string, GraphNode> Nodes => _nodes;
    public List<GraphEdge> Edges => _edges;
    public GraphMetadata Metadata => _metadata;

    /// <summary>
    /// Search across all nodes by name, ID, file path, or namespace using case-insensitive substring matching.
    /// Results prioritize types over other kinds, then shorter names (more relevant).
    /// </summary>
    public List<GraphNode> Search(string query, int maxResults = 20, NodeKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        IEnumerable<GraphNode> results = _nodes.Values.Where(node => MatchesSearchQuery(node, query));
        if (kindFilter.HasValue)
            results = results.Where(node => node.Kind == kindFilter.Value);

        return results
            .OrderByDescending(node => node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(node => node.Kind == NodeKind.Type ? 1 : 0)
            .ThenBy(node => node.Name.Length)
            .Take(maxResults)
            .ToList();
    }

    public static bool LooksLikeFilePath(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return value.Contains('/')
            || value.Contains('\\')
            || value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    public List<GraphNode> FindByFilePath(string path, NodeKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var normalizedPath = NormalizePath(path);
        IEnumerable<GraphNode> results = _nodes.Values.Where(node => MatchesFilePath(node, normalizedPath));
        if (kindFilter.HasValue)
            results = results.Where(node => node.Kind == kindFilter.Value);

        return results.ToList();
    }

    public static async Task<QueryEngine> LoadAsync(string graphDirectory)
    {
        return await LoadAsync(graphDirectory, solutionFilter: null);
    }

    public static async Task<QueryEngine> LoadAsync(string graphDirectory, string? solutionFilter)
    {
        var subGraphDirectories = Directory.Exists(graphDirectory)
            ? Directory.GetDirectories(graphDirectory)
                .Where(directory => File.Exists(Path.Combine(directory, "meta.json")))
                .ToArray()
            : Array.Empty<string>();

        if (subGraphDirectories.Length == 0)
            return await LoadSingleGraphAsync(graphDirectory);

        return await LoadFederatedGraphAsync(graphDirectory, subGraphDirectories, solutionFilter);
    }

    public QueryResult Query(QueryOptions options)
    {
        var matchedNodes = ApplyNodeFilters(FindMatchingNodes(options.Pattern), options);
        var totalMatchCount = matchedNodes.Count;
        var targetNode = matchedNodes.Count == 1 ? matchedNodes[0] : null;

        var seedIds = matchedNodes.Select(node => node.Id).ToList();
        var allowedEdges = QueryModeEdgeSets.ForMode(options.Mode);
        var reachableIds = TraverseFromSeeds(seedIds, options.Depth, options.IncludeExternal, allowedEdges);
        var directNeighborIds = options.Depth >= 1
            ? TraverseFromSeeds(seedIds, 1, options.IncludeExternal, allowedEdges)
            : new HashSet<string>(seedIds);

        var subgraphNodes = BuildSubgraphNodes(reachableIds);
        var subgraphEdges = BuildSubgraphEdges(reachableIds);
        subgraphEdges = ApplyEdgeFilters(subgraphEdges, options);

        var (finalNodes, finalEdges, wasTruncated) = TruncateSubgraphIfNeeded(
            subgraphNodes,
            subgraphEdges,
            seedIds,
            directNeighborIds,
            targetNode,
            options);

        var suggestions = matchedNodes.Count == 0 && !options.Pattern.Contains('*')
            ? FindFuzzyMatches(options.Pattern, maxDistance: 2, maxResults: 5)
            : new List<string>();

        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = matchedNodes,
            Nodes = finalNodes,
            Edges = finalEdges,
            Metadata = _metadata,
            WasTruncated = wasTruncated,
            TotalMatchCount = totalMatchCount,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Find nodes whose names are within edit distance of the query pattern.
    /// Only considers Type and Namespace nodes for performance.
    /// </summary>
    public List<string> FindFuzzyMatches(string pattern, int maxDistance = 2, int maxResults = 5)
    {
        var normalizedPattern = pattern.ToLowerInvariant();

        return _nodes.Values
            .Where(node => node.Kind == NodeKind.Type || node.Kind == NodeKind.Namespace)
            .Select(node => (node.Name, Distance: LevenshteinDistance(normalizedPattern, node.Name.ToLowerInvariant())))
            .Where(candidate => candidate.Distance > 0 && candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name)
            .Select(candidate => candidate.Name)
            .Distinct()
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings.
    /// </summary>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distances = new int[sourceLength + 1, targetLength + 1];

        for (var sourceIndex = 0; sourceIndex <= sourceLength; sourceIndex++)
            distances[sourceIndex, 0] = sourceIndex;

        for (var targetIndex = 0; targetIndex <= targetLength; targetIndex++)
            distances[0, targetIndex] = targetIndex;

        for (var sourceIndex = 1; sourceIndex <= sourceLength; sourceIndex++)
        {
            for (var targetIndex = 1; targetIndex <= targetLength; targetIndex++)
            {
                var substitutionCost = source[sourceIndex - 1] == target[targetIndex - 1] ? 0 : 1;
                distances[sourceIndex, targetIndex] = Math.Min(
                    Math.Min(distances[sourceIndex - 1, targetIndex] + 1, distances[sourceIndex, targetIndex - 1] + 1),
                    distances[sourceIndex - 1, targetIndex - 1] + substitutionCost);
            }
        }

        return distances[sourceLength, targetLength];
    }

    /// <summary>
    /// Estimates cost of running a query without executing full traversal.
    /// Useful for agents to decide if a query is worth running.
    /// </summary>
    public QueryCostEstimate EstimateCost(string pattern, QueryOptions options)
    {
        var matchedNodes = FindMatchingNodes(pattern);
        if (matchedNodes.Count == 0)
            return new QueryCostEstimate(0, 0, 0, 0);

        var seedIds = matchedNodes.Select(node => node.Id).ToList();
        var allowedEdges = QueryModeEdgeSets.ForMode(options.Mode);
        var reachableIds = TraverseFromSeeds(seedIds, options.Depth, options.IncludeExternal, allowedEdges);

        var nodeCount = Math.Min(reachableIds.Count, options.MaxNodes);
        var edgeCount = _edges.Count(edge => reachableIds.Contains(edge.FromId) && reachableIds.Contains(edge.ToId));

        var tokensCompact = nodeCount * 50 + edgeCount * 20;
        var tokensContext = nodeCount * 150 + edgeCount * 60;

        return new QueryCostEstimate(nodeCount, edgeCount, tokensCompact, tokensContext);
    }

    /// <summary>
    /// Compare two symbols structurally. Shows shared interfaces/bases, unique dependencies,
    /// different edge patterns, and structural similarities/differences.
    /// </summary>
    public CompareResult Compare(string patternA, string patternB, int depth = 1)
    {
        var nodesA = FindMatchingNodes(patternA);
        var nodesB = FindMatchingNodes(patternB);

        var nodeA = nodesA.FirstOrDefault();
        var nodeB = nodesB.FirstOrDefault();
        if (nodeA is null && nodeB is null)
            return new CompareResult(null, null, [], [], [], [], []);

        var seedIdsA = nodesA.Select(node => node.Id).ToList();
        var seedIdsB = nodesB.Select(node => node.Id).ToList();

        var reachableA = TraverseFromSeeds(seedIdsA, depth, includeExternal: false, allowedEdges: null);
        var reachableB = TraverseFromSeeds(seedIdsB, depth, includeExternal: false, allowedEdges: null);

        var edgesA = BuildSubgraphEdges(reachableA);
        var edgesB = BuildSubgraphEdges(reachableB);

        var outgoingA = edgesA.Where(edge => seedIdsA.Contains(edge.FromId)).ToList();
        var outgoingB = edgesB.Where(edge => seedIdsB.Contains(edge.FromId)).ToList();

        var sharedEdgeKeys = outgoingA
            .Select(edge => (edge.Type, edge.ToId))
            .ToHashSet()
            .Intersect(outgoingB.Select(edge => (edge.Type, edge.ToId)).ToHashSet())
            .ToHashSet();

        var sharedEdges = outgoingA.Where(edge => sharedEdgeKeys.Contains((edge.Type, edge.ToId))).ToList();
        var uniqueToA = outgoingA.Where(edge => !sharedEdgeKeys.Contains((edge.Type, edge.ToId))).ToList();
        var uniqueToB = outgoingB.Where(edge => !sharedEdgeKeys.Contains((edge.Type, edge.ToId))).ToList();

        var sharedInterfaces = GetSharedEdgeTargets(edgesA, edgesB, seedIdsA, seedIdsB, EdgeType.Implements);
        var sharedBases = GetSharedEdgeTargets(edgesA, edgesB, seedIdsA, seedIdsB, EdgeType.Inherits);

        return new CompareResult(nodeA, nodeB, sharedEdges, uniqueToA, uniqueToB, sharedInterfaces, sharedBases);
    }

    private static void AddEdgeLookupEntry(Dictionary<string, List<GraphEdge>> lookup, string nodeId, GraphEdge edge)
    {
        if (!lookup.TryGetValue(nodeId, out var edges))
        {
            edges = new List<GraphEdge>();
            lookup[nodeId] = edges;
        }

        edges.Add(edge);
    }

    private static bool MatchesSearchQuery(GraphNode node, string query)
    {
        return node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (node.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (node.ContainingNamespaceId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool MatchesFilePath(GraphNode node, string normalizedPath)
    {
        var filePath = NormalizePath(node.FilePath);
        return filePath.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<QueryEngine> LoadSingleGraphAsync(string graphDirectory)
    {
        var dbPath = Path.Combine(graphDirectory, "graph.db");
        if (File.Exists(dbPath))
        {
            var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
            return new QueryEngine(nodes, edges, metadata);
        }

        var graph = await GraphReader.ReadAsync(graphDirectory);
        return new QueryEngine(graph.Nodes, graph.Edges, graph.Metadata);
    }

    private static async Task<QueryEngine> LoadFederatedGraphAsync(
        string graphDirectory,
        IEnumerable<string> subGraphDirectories,
        string? solutionFilter)
    {
        var allNodes = new Dictionary<string, GraphNode>();
        var allEdges = new List<GraphEdge>();
        var solutionNames = new List<string>();
        var allProjectsIndexed = new List<string>();
        GraphMetadata? firstMetadata = null;

        foreach (var subGraphDirectory in subGraphDirectories)
        {
            var solutionName = Path.GetFileName(subGraphDirectory);
            if (!ShouldIncludeSolution(solutionName, solutionFilter))
                continue;

            var (metadata, nodes, edges) = await GraphReader.ReadAsync(subGraphDirectory);
            firstMetadata ??= metadata;
            solutionNames.Add(solutionName);
            allProjectsIndexed.AddRange(metadata.ProjectsIndexed);

            foreach (var node in nodes)
                allNodes.TryAdd(node.Key, node.Value);

            allEdges.AddRange(edges);
        }

        if (firstMetadata is null)
        {
            throw new FileNotFoundException(
                "meta.json not found in graph directory.",
                Path.Combine(graphDirectory, "meta.json"));
        }

        var uniqueEdges = allEdges
            .GroupBy(edge => (edge.FromId, edge.ToId, edge.Type))
            .Select(group => group.First())
            .ToList();

        var federatedMetadata = firstMetadata with
        {
            SolutionName = string.Join(", ", solutionNames),
            Solution = string.Join(", ", solutionNames.Select(name => name + ".sln")),
            ProjectsIndexed = allProjectsIndexed.Distinct().ToArray()
        };

        return new QueryEngine(allNodes, uniqueEdges, federatedMetadata);
    }

    private static bool ShouldIncludeSolution(string solutionName, string? solutionFilter)
    {
        return string.IsNullOrEmpty(solutionFilter)
            || solutionName.Equals(solutionFilter, StringComparison.OrdinalIgnoreCase);
    }

    private List<GraphNode> ApplyNodeFilters(List<GraphNode> matchedNodes, QueryOptions options)
    {
        if (options.NamespaceFilter is not null)
        {
            var allowedIds = NamespaceFilter.Apply(_nodes, options.NamespaceFilter);
            matchedNodes = matchedNodes.Where(node => allowedIds.ContainsKey(node.Id)).ToList();
        }

        if (options.ProjectFilter is not null)
        {
            matchedNodes = matchedNodes
                .Where(node => node.Id.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase)
                    || (node.ContainingNamespaceId?.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return matchedNodes;
    }

    private HashSet<string> TraverseFromSeeds(
        List<string> seedIds,
        int depth,
        bool includeExternal,
        HashSet<EdgeType>? allowedEdges)
    {
        return DepthFilter.Traverse(seedIds, _outgoing, _incoming, depth, includeExternal, allowedEdges);
    }

    private Dictionary<string, GraphNode> BuildSubgraphNodes(HashSet<string> reachableIds)
    {
        var subgraphNodes = new Dictionary<string, GraphNode>();
        foreach (var nodeId in reachableIds)
        {
            if (_nodes.TryGetValue(nodeId, out var node))
                subgraphNodes[nodeId] = node;
        }

        return subgraphNodes;
    }

    private List<GraphEdge> BuildSubgraphEdges(HashSet<string> reachableIds)
    {
        return _edges
            .Where(edge => reachableIds.Contains(edge.FromId) && reachableIds.Contains(edge.ToId))
            .ToList();
    }

    private static List<GraphEdge> ApplyEdgeFilters(List<GraphEdge> subgraphEdges, QueryOptions options)
    {
        if (options.EdgeTypeFilter is not null)
            subgraphEdges = EdgeTypeFilter.Apply(subgraphEdges, options.EdgeTypeFilter);

        if (options.ConfidenceThreshold is not null)
            subgraphEdges = subgraphEdges.Where(edge => edge.Confidence <= options.ConfidenceThreshold.Value).ToList();

        if (!options.IncludeExternal)
            subgraphEdges = subgraphEdges.Where(edge => !edge.IsExternal).ToList();

        return subgraphEdges;
    }

    private (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges, bool WasTruncated) TruncateSubgraphIfNeeded(
        Dictionary<string, GraphNode> subgraphNodes,
        List<GraphEdge> subgraphEdges,
        List<string> seedIds,
        HashSet<string> directNeighborIds,
        GraphNode? targetNode,
        QueryOptions options)
    {
        if (subgraphNodes.Count <= options.MaxNodes)
            return (subgraphNodes, subgraphEdges, false);

        var keepIds = options.Rank
            ? GetRankedNodeIds(subgraphNodes, directNeighborIds, targetNode, options.MaxNodes)
            : new HashSet<string>(subgraphNodes.Keys.Take(options.MaxNodes));

        foreach (var seedId in seedIds)
            keepIds.Add(seedId);

        var truncatedNodes = subgraphNodes
            .Where(node => keepIds.Contains(node.Key))
            .ToDictionary(node => node.Key, node => node.Value);
        var truncatedEdges = subgraphEdges
            .Where(edge => keepIds.Contains(edge.FromId) && keepIds.Contains(edge.ToId))
            .ToList();

        return (truncatedNodes, truncatedEdges, true);
    }

    private HashSet<string> GetRankedNodeIds(
        Dictionary<string, GraphNode> subgraphNodes,
        HashSet<string> directNeighborIds,
        GraphNode? targetNode,
        int maxNodes)
    {
        var externalIds = new HashSet<string>(
            _edges.Where(edge => edge.IsExternal).SelectMany(edge => new[] { edge.FromId, edge.ToId }));
        var targetProject = targetNode?.ContainingNamespaceId;

        var rankedNodes = RankingStrategy.Rank(
            subgraphNodes.Values.ToList(),
            directNeighborIds,
            externalIds,
            targetProject);

        return new HashSet<string>(rankedNodes.Take(maxNodes).Select(node => node.Id));
    }

    private static List<string> GetSharedEdgeTargets(
        List<GraphEdge> edgesA,
        List<GraphEdge> edgesB,
        List<string> seedIdsA,
        List<string> seedIdsB,
        EdgeType edgeType)
    {
        var targetsA = edgesA
            .Where(edge => edge.Type == edgeType && seedIdsA.Contains(edge.FromId))
            .Select(edge => edge.ToId)
            .ToHashSet();
        var targetsB = edgesB
            .Where(edge => edge.Type == edgeType && seedIdsB.Contains(edge.FromId))
            .Select(edge => edge.ToId)
            .ToHashSet();

        return targetsA.Intersect(targetsB).OrderBy(target => target).ToList();
    }

    private List<GraphNode> FindMatchingNodes(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return _nodes.Values.ToList();

        var (kindFilter, searchPattern) = ParseKindFilter(pattern);
        var results = searchPattern.Contains('*')
            ? FindWildcardMatches(searchPattern)
            : FindExactOrPartialMatches(searchPattern);

        if (kindFilter is not null)
            results = results.Where(node => node.Kind == kindFilter.Value).ToList();

        return results;
    }

    private static (NodeKind? KindFilter, string SearchPattern) ParseKindFilter(string pattern)
    {
        var kindPrefixMatch = Regex.Match(
            pattern,
            @"^(namespace|type|method|property|field|event|constructor):(.+)$",
            RegexOptions.IgnoreCase);

        if (!kindPrefixMatch.Success)
            return (null, pattern);

        var kindFilter = Enum.Parse<NodeKind>(kindPrefixMatch.Groups[1].Value, ignoreCase: true);
        var searchPattern = kindPrefixMatch.Groups[2].Value;
        return (kindFilter, searchPattern);
    }

    private List<GraphNode> FindWildcardMatches(string searchPattern)
    {
        var regexPattern = "^" + Regex.Escape(searchPattern).Replace("\\*", ".*") + "$";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        return _nodes.Values
            .Where(node => regex.IsMatch(node.Id) || regex.IsMatch(node.Name))
            .ToList();
    }

    private List<GraphNode> FindExactOrPartialMatches(string searchPattern)
    {
        var exactMatches = _nodes.Values
            .Where(node => node.Id.Equals(searchPattern, StringComparison.OrdinalIgnoreCase)
                || node.Id.EndsWith("." + searchPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactMatches.Count > 0)
            return exactMatches;

        return _nodes.Values
            .Where(node => node.Name.Equals(searchPattern, StringComparison.OrdinalIgnoreCase)
                || node.Id.EndsWith(searchPattern, StringComparison.OrdinalIgnoreCase)
                || node.Name.EndsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public record QueryOptions
{
    public string Pattern { get; init; } = string.Empty;
    public int Depth { get; init; } = 1;
    public EdgeType? EdgeTypeFilter { get; init; }
    public string? NamespaceFilter { get; init; }
    public string? ProjectFilter { get; init; }
    public int MaxNodes { get; init; } = 50;
    public bool IncludeExternal { get; init; }
    public bool Rank { get; init; } = true;
    public bool IgnoreCase { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Context;
    public QueryMode Mode { get; init; } = QueryMode.All;
    public int? Budget { get; init; }
    /// <summary>
    /// Include edges at this confidence level or better.
    /// Verified (0) is highest confidence, Unresolved (2) is lowest.
    /// A threshold of Inferred includes Verified and Inferred, but not Unresolved.
    /// </summary>
    public EdgeConfidence? ConfidenceThreshold { get; init; }
}

public enum OutputFormat { Json, Text, Context, Compact }

public enum QueryMode
{
    /// <summary>Follow only high-signal edges: Calls, Implements, ResolvesTo, Covers, CoveredBy, Inherits, Overrides</summary>
    Focused,
    /// <summary>Follow structural edges too: Focused + Contains, DependsOn</summary>
    Structural,
    /// <summary>Follow all edge types (current behavior)</summary>
    All
}

internal static class QueryModeEdgeSets
{
    public static readonly HashSet<EdgeType> Focused = new()
    {
        EdgeType.Calls, EdgeType.Implements, EdgeType.ResolvesTo,
        EdgeType.Covers, EdgeType.CoveredBy, EdgeType.Inherits, EdgeType.Overrides
    };

    public static readonly HashSet<EdgeType> Structural = new(Focused)
    {
        EdgeType.Contains, EdgeType.DependsOn,
        EdgeType.HandlesRoute, EdgeType.BindsConfiguration, EdgeType.UsesMiddleware,
        EdgeType.MapsToTable, EdgeType.NavigatesTo, EdgeType.ConfiguredBy
    };

    public static HashSet<EdgeType>? ForMode(QueryMode mode) => mode switch
    {
        QueryMode.Focused => Focused,
        QueryMode.Structural => Structural,
        QueryMode.All => null,
        _ => null
    };
}

public record QueryResult
{
    public GraphNode? TargetNode { get; init; }
    public List<GraphNode> MatchedNodes { get; init; } = new();
    public Dictionary<string, GraphNode> Nodes { get; init; } = new();
    public List<GraphEdge> Edges { get; init; } = new();
    public GraphMetadata Metadata { get; init; } = null!;
    public bool WasTruncated { get; init; }
    public int TotalMatchCount { get; init; }
    public List<string> Suggestions { get; init; } = new();
}

public record CompareResult(
    GraphNode? NodeA,
    GraphNode? NodeB,
    List<GraphEdge> SharedEdges,
    List<GraphEdge> UniqueToA,
    List<GraphEdge> UniqueToB,
    List<string> SharedInterfaces,
    List<string> SharedBases);
