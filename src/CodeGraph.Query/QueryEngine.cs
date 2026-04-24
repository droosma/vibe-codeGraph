using System.Text.RegularExpressions;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Query.Filters;

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

    public static async Task<QueryEngine> LoadAsync(string graphDirectory)
    {
        return await LoadAsync(graphDirectory, solutionFilter: null);
    }

    public static async Task<QueryEngine> LoadAsync(string graphDirectory, string? solutionFilter)
    {
        var reader = new GraphReader();

        // Check if this is a federated graph directory (has subdirectories with meta.json)
        var subGraphDirs = Directory.Exists(graphDirectory)
            ? Directory.GetDirectories(graphDirectory)
                .Where(d => File.Exists(Path.Combine(d, "meta.json")))
                .ToArray()
            : Array.Empty<string>();

        if (subGraphDirs.Length == 0)
        {
            // Single-solution graph (backward compatible)
            var (metadata, nodes, edges) = await reader.ReadAsync(graphDirectory);
            return new QueryEngine(nodes, edges, metadata);
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

            var (metadata, nodes, edges) = await reader.ReadAsync(subDir);
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
        var reachableIds = DepthFilter.Traverse(
            seedIds, _outgoing, _incoming, options.Depth, options.IncludeExternal);

        // Collect direct neighbors for ranking
        var directNeighborIds = options.Depth >= 1
            ? DepthFilter.Traverse(seedIds, _outgoing, _incoming, 1, options.IncludeExternal)
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

        return new QueryResult
        {
            TargetNode = targetNode,
            MatchedNodes = matchedNodes,
            Nodes = subgraphNodes,
            Edges = subgraphEdges,
            Metadata = _metadata,
            WasTruncated = wasTruncated,
            TotalMatchCount = totalMatchCount
        };
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
    public OutputFormat Format { get; init; } = OutputFormat.Context;
}

public enum OutputFormat { Json, Text, Context }

public record QueryResult
{
    public GraphNode? TargetNode { get; init; }
    public List<GraphNode> MatchedNodes { get; init; } = new();
    public Dictionary<string, GraphNode> Nodes { get; init; } = new();
    public List<GraphEdge> Edges { get; init; } = new();
    public GraphMetadata Metadata { get; init; } = null!;
    public bool WasTruncated { get; init; }
    public int TotalMatchCount { get; init; }
}
