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
            if (!_outgoing.TryGetValue(edge.FromId, out var outList))
            {
                outList = new List<GraphEdge>();
                _outgoing[edge.FromId] = outList;
            }
            outList.Add(edge);

            if (!_incoming.TryGetValue(edge.ToId, out var inList))
            {
                inList = new List<GraphEdge>();
                _incoming[edge.ToId] = inList;
            }
            inList.Add(edge);
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

        var results = _nodes.Values
            .Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        n.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (n.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (n.ContainingNamespaceId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        if (kindFilter.HasValue)
            results = results.Where(n => n.Kind == kindFilter.Value);

        return results
            .OrderByDescending(n => n.Kind == NodeKind.Type ? 1 : 0)
            .ThenBy(n => n.Name.Length)
            .Take(maxResults)
            .ToList();
    }

    public static bool LooksLikeFilePath(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.Contains('/') || value.Contains('\\') || value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    public List<GraphNode> FindByFilePath(string path, NodeKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var normalized = path.Replace('\\', '/');
        var results = _nodes.Values.Where(n =>
            n.FilePath.Replace('\\', '/').Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            n.FilePath.Replace('\\', '/').EndsWith(normalized, StringComparison.OrdinalIgnoreCase));

        if (kindFilter.HasValue)
            results = results.Where(n => n.Kind == kindFilter.Value);

        return results.ToList();
    }

    public static async Task<QueryEngine> LoadAsync(string graphDirectory)
    {
        return await LoadAsync(graphDirectory, solutionFilter: null);
    }

    public static async Task<QueryEngine> LoadAsync(string graphDirectory, string? solutionFilter)
    {
        // Check if this is a federated graph directory (has subdirectories with meta.json)
        var subGraphDirs = Directory.Exists(graphDirectory)
            ? Directory.GetDirectories(graphDirectory)
                .Where(d => File.Exists(Path.Combine(d, "meta.json")))
                .ToArray()
            : Array.Empty<string>();

        if (subGraphDirs.Length == 0)
        {
            // Single-solution graph — prefer SQLite when available
            var dbPath = Path.Combine(graphDirectory, "graph.db");
            if (File.Exists(dbPath))
            {
                var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(dbPath);
                return new QueryEngine(nodes, edges, metadata);
            }

            // Fall back to JSON
            var result = await GraphReader.ReadAsync(graphDirectory);
            return new QueryEngine(result.Nodes, result.Edges, result.Metadata);
        }

        // Federated: load all sub-graphs, deduplicate nodes, merge edges
        var allNodes = new Dictionary<string, GraphNode>();
        var allEdges = new List<GraphEdge>();
        var solutionNames = new List<string>();
        var allProjectsIndexed = new List<string>();
        GraphMetadata? firstMetadata = null;

        foreach (var subDir in subGraphDirs)
        {
            var solutionName = Path.GetFileName(subDir);

            // Apply solution filter if provided
            if (!string.IsNullOrEmpty(solutionFilter) &&
                !solutionName.Equals(solutionFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (metadata, nodes, edges) = await GraphReader.ReadAsync(subDir);
            firstMetadata ??= metadata;
            solutionNames.Add(solutionName);
            allProjectsIndexed.AddRange(metadata.ProjectsIndexed);

            // Deduplicate nodes by ID (first-seen wins)
            foreach (var kvp in nodes)
            {
                allNodes.TryAdd(kvp.Key, kvp.Value);
            }

            allEdges.AddRange(edges);
        }

        if (firstMetadata is null)
        {
            throw new FileNotFoundException("meta.json not found in graph directory.",
                Path.Combine(graphDirectory, "meta.json"));
        }

        // Deduplicate edges
        var uniqueEdges = allEdges
            .GroupBy(e => (e.FromId, e.ToId, e.Type))
            .Select(g => g.First())
            .ToList();

        // Build a federated metadata record
        var federatedMetadata = firstMetadata with
        {
            SolutionName = string.Join(", ", solutionNames),
            Solution = string.Join(", ", solutionNames.Select(n => n + ".sln")),
            ProjectsIndexed = allProjectsIndexed.Distinct().ToArray()
        };

        return new QueryEngine(allNodes, uniqueEdges, federatedMetadata);
    }

    public QueryResult Query(QueryOptions options)
    {
        // Step 1: Find matching nodes by pattern
        var matchedNodes = FindMatchingNodes(options.Pattern);

        // Step 2: Apply namespace filter
        if (options.NamespaceFilter is not null)
        {
            var allowedIds = NamespaceFilter.Apply(_nodes, options.NamespaceFilter);
            matchedNodes = matchedNodes.Where(n => allowedIds.ContainsKey(n.Id)).ToList();
        }

        // Step 3: Apply project filter
        if (options.ProjectFilter is not null)
        {
            matchedNodes = matchedNodes
                .Where(n => n.Id.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase)
                    || (n.ContainingNamespaceId?.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var totalMatchCount = matchedNodes.Count;
        var targetNode = matchedNodes.Count == 1 ? matchedNodes[0] : null;

        // Step 4: Depth traversal (BFS)
        var seedIds = matchedNodes.Select(n => n.Id).ToList();
        var allowedEdges = QueryModeEdgeSets.ForMode(options.Mode);
        var reachableIds = DepthFilter.Traverse(
            seedIds, _outgoing, _incoming, options.Depth, options.IncludeExternal, allowedEdges);

        // Collect direct neighbors for ranking
        var directNeighborIds = options.Depth >= 1
            ? DepthFilter.Traverse(seedIds, _outgoing, _incoming, 1, options.IncludeExternal, allowedEdges)
            : new HashSet<string>(seedIds);

        // Step 5: Build subgraph
        var subgraphNodes = new Dictionary<string, GraphNode>();
        foreach (var id in reachableIds)
        {
            if (_nodes.TryGetValue(id, out var node))
                subgraphNodes[id] = node;
        }

        // Step 6: Collect edges within the subgraph
        var subgraphEdges = _edges
            .Where(e => reachableIds.Contains(e.FromId) && reachableIds.Contains(e.ToId))
            .ToList();

        // Step 7: Apply edge type filter
        if (options.EdgeTypeFilter is not null)
        {
            subgraphEdges = EdgeTypeFilter.Apply(subgraphEdges, options.EdgeTypeFilter);
        }

        // Step 7b: Apply confidence filter (lower enum value = higher confidence)
        if (options.ConfidenceThreshold is not null)
        {
            subgraphEdges = subgraphEdges.Where(e => e.Confidence <= options.ConfidenceThreshold.Value).ToList();
        }

        // Step 8: Filter out external if not requested
        if (!options.IncludeExternal)
        {
            subgraphEdges = subgraphEdges.Where(e => !e.IsExternal).ToList();
        }

        // Step 9: Rank and truncate
        var wasTruncated = false;
        if (options.Rank && subgraphNodes.Count > options.MaxNodes)
        {
            var externalIds = new HashSet<string>(
                _edges.Where(e => e.IsExternal).SelectMany(e => new[] { e.FromId, e.ToId }));

            var targetProject = targetNode?.ContainingNamespaceId;
            var rankedNodes = RankingStrategy.Rank(
                subgraphNodes.Values.ToList(),
                directNeighborIds,
                externalIds,
                targetProject);

            var keepIds = new HashSet<string>(rankedNodes.Take(options.MaxNodes).Select(n => n.Id));
            // Always keep seed nodes
            foreach (var id in seedIds)
                keepIds.Add(id);

            subgraphNodes = subgraphNodes
                .Where(kvp => keepIds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            subgraphEdges = subgraphEdges
                .Where(e => keepIds.Contains(e.FromId) && keepIds.Contains(e.ToId))
                .ToList();

            wasTruncated = true;
        }
        else if (subgraphNodes.Count > options.MaxNodes)
        {
            wasTruncated = true;
            var keepIds = new HashSet<string>(subgraphNodes.Keys.Take(options.MaxNodes));
            foreach (var id in seedIds)
                keepIds.Add(id);

            subgraphNodes = subgraphNodes
                .Where(kvp => keepIds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            subgraphEdges = subgraphEdges
                .Where(e => keepIds.Contains(e.FromId) && keepIds.Contains(e.ToId))
                .ToList();
        }

        // Fuzzy fallback: if no matches and pattern has no wildcards, suggest similar names
        var suggestions = new List<string>();
        if (matchedNodes.Count == 0 && !options.Pattern.Contains('*'))
        {
            suggestions = FindFuzzyMatches(options.Pattern, maxDistance: 2, maxResults: 5);
        }

        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = matchedNodes,
            Nodes = subgraphNodes,
            Edges = subgraphEdges,
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
        var candidates = _nodes.Values
            .Where(n => n.Kind == NodeKind.Type || n.Kind == NodeKind.Namespace)
            .Select(n => (n.Name, Distance: LevenshteinDistance(pattern.ToLowerInvariant(), n.Name.ToLowerInvariant())))
            .Where(x => x.Distance > 0 && x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .Distinct()
            .Take(maxResults)
            .ToList();

        return candidates;
    }

    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings.
    /// </summary>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var m = source.Length;
        var n = target.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
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

        var seedIds = matchedNodes.Select(n => n.Id).ToList();
        var allowedEdges = QueryModeEdgeSets.ForMode(options.Mode);
        var reachableIds = DepthFilter.Traverse(
            seedIds, _outgoing, _incoming, options.Depth, options.IncludeExternal, allowedEdges);

        var nodeCount = Math.Min(reachableIds.Count, options.MaxNodes);
        var edgeCount = _edges
            .Count(e => reachableIds.Contains(e.FromId) && reachableIds.Contains(e.ToId));

        // Token estimates: compact ~50/node + 20/edge, context ~150/node + 60/edge
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

        // Gather edges for each symbol at the given depth
        var seedIdsA = nodesA.Select(n => n.Id).ToList();
        var seedIdsB = nodesB.Select(n => n.Id).ToList();

        var reachableA = DepthFilter.Traverse(seedIdsA, _outgoing, _incoming, depth, false, null);
        var reachableB = DepthFilter.Traverse(seedIdsB, _outgoing, _incoming, depth, false, null);

        var edgesA = _edges
            .Where(e => reachableA.Contains(e.FromId) && reachableA.Contains(e.ToId))
            .ToList();
        var edgesB = _edges
            .Where(e => reachableB.Contains(e.FromId) && reachableB.Contains(e.ToId))
            .ToList();

        // Normalize edges for comparison: compare by (Type, ToId) for outgoing from seeds
        var outgoingA = edgesA
            .Where(e => seedIdsA.Contains(e.FromId))
            .ToList();
        var outgoingB = edgesB
            .Where(e => seedIdsB.Contains(e.FromId))
            .ToList();

        // Edges are "shared" if they have the same target and type
        var edgeKeyA = outgoingA.Select(e => (e.Type, e.ToId)).ToHashSet();
        var edgeKeyB = outgoingB.Select(e => (e.Type, e.ToId)).ToHashSet();

        var sharedKeys = edgeKeyA.Intersect(edgeKeyB).ToHashSet();
        var sharedEdges = outgoingA.Where(e => sharedKeys.Contains((e.Type, e.ToId))).ToList();
        var uniqueToA = outgoingA.Where(e => !sharedKeys.Contains((e.Type, e.ToId))).ToList();
        var uniqueToB = outgoingB.Where(e => !sharedKeys.Contains((e.Type, e.ToId))).ToList();

        // Find shared interfaces (both implement the same interface)
        var interfacesA = edgesA
            .Where(e => e.Type == EdgeType.Implements && seedIdsA.Contains(e.FromId))
            .Select(e => e.ToId)
            .ToHashSet();
        var interfacesB = edgesB
            .Where(e => e.Type == EdgeType.Implements && seedIdsB.Contains(e.FromId))
            .Select(e => e.ToId)
            .ToHashSet();
        var sharedInterfaces = interfacesA.Intersect(interfacesB).OrderBy(x => x).ToList();

        // Find shared base classes
        var basesA = edgesA
            .Where(e => e.Type == EdgeType.Inherits && seedIdsA.Contains(e.FromId))
            .Select(e => e.ToId)
            .ToHashSet();
        var basesB = edgesB
            .Where(e => e.Type == EdgeType.Inherits && seedIdsB.Contains(e.FromId))
            .Select(e => e.ToId)
            .ToHashSet();
        var sharedBases = basesA.Intersect(basesB).OrderBy(x => x).ToList();

        return new CompareResult(nodeA, nodeB, sharedEdges, uniqueToA, uniqueToB, sharedInterfaces, sharedBases);
    }

    private List<GraphNode> FindMatchingNodes(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return _nodes.Values.ToList();

        // Kind filter: "type:OrderService"
        NodeKind? kindFilter = null;
        var searchPattern = pattern;

        var kindPrefixMatch = Regex.Match(pattern, @"^(namespace|type|method|property|field|event|constructor):(.+)$",
            RegexOptions.IgnoreCase);
        if (kindPrefixMatch.Success)
        {
            kindFilter = Enum.Parse<NodeKind>(kindPrefixMatch.Groups[1].Value, ignoreCase: true);
            searchPattern = kindPrefixMatch.Groups[2].Value;
        }

        List<GraphNode> results;

        if (searchPattern.Contains('*'))
        {
            // Wildcard match
            var regexPattern = "^" + Regex.Escape(searchPattern).Replace("\\*", ".*") + "$";
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            results = _nodes.Values
                .Where(n => regex.IsMatch(n.Id) || regex.IsMatch(n.Name))
                .ToList();
        }
        else
        {
            // Exact match: try Id ending with pattern
            var exact = _nodes.Values
                .Where(n => n.Id.Equals(searchPattern, StringComparison.OrdinalIgnoreCase)
                    || n.Id.EndsWith("." + searchPattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exact.Count > 0)
            {
                results = exact;
            }
            else
            {
                // Partial match: Name or Id ends with pattern
                results = _nodes.Values
                    .Where(n => n.Name.Equals(searchPattern, StringComparison.OrdinalIgnoreCase)
                        || n.Id.EndsWith(searchPattern, StringComparison.OrdinalIgnoreCase)
                        || n.Name.EndsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        if (kindFilter is not null)
            results = results.Where(n => n.Kind == kindFilter.Value).ToList();

        return results;
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
        EdgeType.Contains, EdgeType.DependsOn
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
