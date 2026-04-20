using System.Text.Json;
using CodeGraph.Core.Models;
#if NETSTANDARD2_0
using CodeGraph.Core.Polyfills;
#endif

namespace CodeGraph.Core.IO;

public class GraphWriter
{
    private readonly SplitFileStrategy _strategy;

    public GraphWriter(SplitFileStrategy strategy = SplitFileStrategy.ByAssembly)
    {
        _strategy = strategy;
    }

    public async Task WriteAsync(
        string outputDirectory,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        GraphMetadata metadata)
    {
        Directory.CreateDirectory(outputDirectory);

        var nodeList = nodes.ToList();
        var edgeList = edges.ToList();

        var projectGraphs = GroupByStrategy(nodeList, edgeList);

        foreach (var kvp in projectGraphs)
        {
            var fileName = SanitizeFileName(kvp.Key) + ".json";
            var filePath = Path.Combine(outputDirectory, fileName);
#if NETSTANDARD2_0
            using var stream = File.Create(filePath);
#else
            await using var stream = File.Create(filePath);
#endif
            await JsonSerializer.SerializeAsync(stream, kvp.Value, GraphSerializationOptions.Default);
        }

        // Write meta.json
        var metaPath = Path.Combine(outputDirectory, "meta.json");
#if NETSTANDARD2_0
        using var metaStream = File.Create(metaPath);
#else
        await using var metaStream = File.Create(metaPath);
#endif
        await JsonSerializer.SerializeAsync(metaStream, metadata, GraphSerializationOptions.Default);
    }

    private Dictionary<string, ProjectGraph> GroupByStrategy(
        List<GraphNode> nodes, List<GraphEdge> edges)
    {
        IEnumerable<IGrouping<string, GraphNode>> grouped = _strategy switch
        {
            SplitFileStrategy.ByNamespace => nodes.GroupBy(n => ExtractNamespace(n.Id)),
            SplitFileStrategy.ByAssembly => nodes.GroupBy(n => GetAssemblyKey(n)),
            _ => nodes.GroupBy(n => ExtractProject(n.Id))
        };

        var result = new Dictionary<string, ProjectGraph>();
        var nodeIdToKey = new Dictionary<string, string>();

        foreach (var group in grouped)
        {
            var key = string.IsNullOrEmpty(group.Key) ? "_default" : group.Key;
            var nodeDict = new Dictionary<string, GraphNode>();

            foreach (var node in group)
            {
                nodeDict[node.Id] = node;
                nodeIdToKey[node.Id] = key;
            }

            result[key] = new ProjectGraph
            {
                ProjectOrNamespace = key,
                Nodes = nodeDict,
                Edges = new List<GraphEdge>()
            };
        }

        // Assign edges to the project of their source node
        foreach (var edge in edges)
        {
            if (nodeIdToKey.TryGetValue(edge.FromId, out var key) && result.ContainsKey(key))
            {
                result[key].Edges.Add(edge);
            }
            else
            {
                // Fallback: assign to first project or _default
                var fallbackKey = result.Keys.FirstOrDefault() ?? "_default";
                if (!result.ContainsKey(fallbackKey))
                {
                    result[fallbackKey] = new ProjectGraph
                    {
                        ProjectOrNamespace = fallbackKey,
                        Nodes = new Dictionary<string, GraphNode>(),
                        Edges = new List<GraphEdge>()
                    };
                }
                result[fallbackKey].Edges.Add(edge);
            }
        }

        return result;
    }

    /// <summary>
    /// Groups by AssemblyName. External nodes (empty file path + assembly metadata) → "_external".
    /// </summary>
    private static string GetAssemblyKey(GraphNode node)
    {
        if (string.IsNullOrEmpty(node.FilePath) && node.Metadata.ContainsKey("assembly"))
            return "_external";

        if (!string.IsNullOrEmpty(node.AssemblyName))
            return node.AssemblyName;

        return ExtractProject(node.Id);
    }

    /// <summary>
    /// Extracts the first segment of a fully qualified ID as the project name.
    /// E.g. "MyApp.Services.OrderService.PlaceOrder" → "MyApp"
    /// </summary>
    private static string ExtractProject(string fullyQualifiedId)
    {
        var dotIndex = fullyQualifiedId.IndexOf('.');
        return dotIndex > 0 ? fullyQualifiedId.Substring(0, dotIndex) : fullyQualifiedId;
    }

    /// <summary>
    /// Extracts the namespace portion (all but the last segment) of a fully qualified ID.
    /// E.g. "MyApp.Services.OrderService.PlaceOrder" → "MyApp.Services.OrderService"
    /// </summary>
    private static string ExtractNamespace(string fullyQualifiedId)
    {
        var lastDot = fullyQualifiedId.LastIndexOf('.');
        return lastDot > 0 ? fullyQualifiedId.Substring(0, lastDot) : fullyQualifiedId;
    }

    private static string SanitizeFileName(string name)
    {
        // Use a fixed set of invalid chars so output is consistent across OS
        var invalid = new HashSet<char>(
            Path.GetInvalidFileNameChars()
                .Union(new[] { '<', '>', ':', '"', '|', '?', '*' }));
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
