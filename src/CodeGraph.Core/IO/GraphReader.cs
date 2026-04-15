using System.Text.Json;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.IO;

public class GraphReader
{
    public async Task<(GraphMetadata Metadata, Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges)> ReadAsync(
        string directory)
    {
        var metaPath = Path.Combine(directory, "meta.json");
        if (!File.Exists(metaPath))
            throw new FileNotFoundException("meta.json not found in graph directory.", metaPath);

        await using var metaStream = File.OpenRead(metaPath);
        var metadata = await JsonSerializer.DeserializeAsync<GraphMetadata>(metaStream, GraphSerializationOptions.Default)
            ?? throw new InvalidOperationException("Failed to deserialize meta.json.");

        GraphSchema.Validate(metadata.SchemaVersion);

        var allNodes = new Dictionary<string, GraphNode>();
        var allEdges = new List<GraphEdge>();

        var jsonFiles = Directory.GetFiles(directory, "*.json")
            .Where(f => !Path.GetFileName(f).Equals("meta.json", StringComparison.OrdinalIgnoreCase));

        foreach (var file in jsonFiles)
        {
            await using var stream = File.OpenRead(file);
            var projectGraph = await JsonSerializer.DeserializeAsync<ProjectGraph>(stream, GraphSerializationOptions.Default);
            if (projectGraph is null) continue;

            foreach (var (id, node) in projectGraph.Nodes)
            {
                allNodes[id] = node;
            }

            allEdges.AddRange(projectGraph.Edges);
        }

        return (metadata, allNodes, allEdges);
    }
}
