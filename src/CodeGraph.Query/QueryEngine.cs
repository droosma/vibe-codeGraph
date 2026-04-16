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
        var reader = new GraphReader();
        var (metadata, nodes, edges) = await reader.ReadAsync(graphDirectory);
        return new QueryEngine(nodes, edges, metadata);
    }

    public QueryResult Query(QueryOptions options)
    {
        var matchedNodes = FindMatchingNodes(options.Pattern);
        return ExecuteFromMatches(matchedNodes, options);
    }

    public QueryResult QueryFromResult(IReadOnlyCollection<string> seedNodeIds, QueryOptions options)
    {
        var matchedNodes = new List<GraphNode>();
        foreach (var id in seedNodeIds)
        {
            if (_nodes.TryGetValue(id, out var node))
                matchedNodes.Add(node);
        }
        return ExecuteFromMatches(matchedNodes, options);
    }

    private QueryResult ExecuteFromMatches(List<GraphNode> matchedNodes, QueryOptions options)
    {
        // Apply namespace filter
        if (options.NamespaceFilter is not null)
        {
            var allowedIds = NamespaceFilter.Apply(_nodes, options.NamespaceFilter);
            matchedNodes = matchedNodes.Where(n => allowedIds.ContainsKey(n.Id)).ToList();
        }

        // Apply project filter
        if (options.ProjectFilter is not null)
        {
            matchedNodes = matchedNodes
                .Where(n => n.Id.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase)
                    || (n.ContainingNamespaceId?.StartsWith(options.ProjectFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var totalMatchCount = matchedNodes.Count;
        var targetNode = matchedNodes.Count == 1 ? matchedNodes[0] : null;

        // Depth traversal (BFS)
        var seedIds = matchedNodes.Select(n => n.Id).ToList();
        var reachableIds = DepthFilter.Traverse(
            seedIds, _outgoing, _incoming, options.Depth, options.IncludeExternal);

        // Collect direct neighbors for ranking
        var directNeighborIds = options.Depth >= 1
            ? DepthFilter.Traverse(seedIds, _outgoing, _incoming, 1, options.IncludeExternal)
            : new HashSet<string>(seedIds);

        // Build subgraph
        var subgraphNodes = new Dictionary<string, GraphNode>();
        foreach (var id in reachableIds)
        {
            if (_nodes.TryGetValue(id, out var node))
                subgraphNodes[id] = node;
        }

        // Collect edges within the subgraph
        var subgraphEdges = _edges
            .Where(e => reachableIds.Contains(e.FromId) && reachableIds.Contains(e.ToId))
            .ToList();

        // Apply edge type filter
        if (options.EdgeTypeFilter is not null)
        {
            subgraphEdges = EdgeTypeFilter.Apply(subgraphEdges, options.EdgeTypeFilter);
        }

        // Filter out external if not requested
        if (!options.IncludeExternal)
        {
            subgraphEdges = subgraphEdges.Where(e => !e.IsExternal).ToList();
        }

        // Rank and truncate
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

    public static QueryResult Union(QueryResult a, QueryResult b)
    {
        var nodes = new Dictionary<string, GraphNode>(a.Nodes);
        foreach (var (key, value) in b.Nodes)
            nodes.TryAdd(key, value);

        var edgeKeys = new HashSet<(string, string, EdgeType)>(
            a.Edges.Select(e => (e.FromId, e.ToId, e.Type)));
        var edges = new List<GraphEdge>(a.Edges);
        foreach (var e in b.Edges)
        {
            if (edgeKeys.Add((e.FromId, e.ToId, e.Type)))
                edges.Add(e);
        }

        var matchedIds = new HashSet<string>();
        var matchedNodes = new List<GraphNode>();
        foreach (var n in a.MatchedNodes.Concat(b.MatchedNodes))
        {
            if (matchedIds.Add(n.Id))
                matchedNodes.Add(n);
        }

        return new QueryResult
        {
            MatchedNodes = matchedNodes,
            Nodes = nodes,
            Edges = edges,
            Metadata = a.Metadata,
            WasTruncated = a.WasTruncated || b.WasTruncated,
            TotalMatchCount = a.TotalMatchCount + b.TotalMatchCount
        };
    }

    public static QueryResult Intersect(QueryResult a, QueryResult b)
    {
        var commonIds = new HashSet<string>(a.Nodes.Keys);
        commonIds.IntersectWith(b.Nodes.Keys);

        var nodes = a.Nodes
            .Where(kvp => commonIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var edges = a.Edges
            .Where(e => commonIds.Contains(e.FromId) && commonIds.Contains(e.ToId))
            .ToList();

        var matchedNodes = a.MatchedNodes
            .Where(n => commonIds.Contains(n.Id))
            .ToList();

        return new QueryResult
        {
            MatchedNodes = matchedNodes,
            Nodes = nodes,
            Edges = edges,
            Metadata = a.Metadata,
            WasTruncated = false,
            TotalMatchCount = matchedNodes.Count
        };
    }

    public static QueryResult Difference(QueryResult a, QueryResult b)
    {
        var removeIds = new HashSet<string>(b.Nodes.Keys);

        var nodes = a.Nodes
            .Where(kvp => !removeIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var remainingIds = new HashSet<string>(nodes.Keys);
        var edges = a.Edges
            .Where(e => remainingIds.Contains(e.FromId) && remainingIds.Contains(e.ToId))
            .ToList();

        var matchedNodes = a.MatchedNodes
            .Where(n => !removeIds.Contains(n.Id))
            .ToList();

        return new QueryResult
        {
            MatchedNodes = matchedNodes,
            Nodes = nodes,
            Edges = edges,
            Metadata = a.Metadata,
            WasTruncated = false,
            TotalMatchCount = matchedNodes.Count
        };
    }
}
