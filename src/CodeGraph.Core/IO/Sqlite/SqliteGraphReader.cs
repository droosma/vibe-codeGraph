using System.Globalization;
using System.Text.Json;
using CodeGraph.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.IO.Sqlite;

/// <summary>
/// Reads a complete code graph from a SQLite database file.
/// </summary>
public class SqliteGraphReader
{
    /// <summary>
    /// Reads the complete graph from a SQLite database (equivalent to JSON GraphReader).
    /// </summary>
    public static async Task<(GraphMetadata Metadata, Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges)> ReadAsync(
        string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Graph database not found.", dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var metadata = await ReadMetadataAsync(connection).ConfigureAwait(false);
        var nodes = await ReadNodesAsync(connection).ConfigureAwait(false);
        var edges = await ReadEdgesAsync(connection).ConfigureAwait(false);

        return (metadata, nodes, edges);
    }

    private static async Task<GraphMetadata> ReadMetadataAsync(SqliteConnection connection)
    {
        var kvp = new Dictionary<string, string>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM metadata";

        using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            kvp[reader.GetString(0)] = reader.GetString(1);
        }

        var stats = new Dictionary<string, int>();
        foreach (var entry in kvp)
        {
            if (entry.Key.StartsWith("stat_", StringComparison.Ordinal))
            {
                var statName = entry.Key.Substring("stat_".Length);
                if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                {
                    stats[statName] = val;
                }
            }
        }

        return new GraphMetadata
        {
            SchemaVersion = int.Parse(GetOrDefault(kvp, "schema_version", "1"), CultureInfo.InvariantCulture),
            CommitHash = GetOrDefault(kvp, "commit_hash", string.Empty),
            Branch = GetOrDefault(kvp, "branch", string.Empty),
            GeneratedAt = kvp.TryGetValue("generated_at", out var genAt)
                ? DateTimeOffset.Parse(genAt, CultureInfo.InvariantCulture)
                : DateTimeOffset.MinValue,
            IndexerVersion = GetOrDefault(kvp, "indexer_version", string.Empty),
            Solution = GetOrDefault(kvp, "solution", string.Empty),
            SolutionName = GetOrDefault(kvp, "solution_name", string.Empty),
            ProjectsIndexed = kvp.TryGetValue("projects_indexed", out var pi)
                ? JsonSerializer.Deserialize<string[]>(pi) ?? Array.Empty<string>()
                : Array.Empty<string>(),
            Stats = stats
        };
    }

    private static async Task<Dictionary<string, GraphNode>> ReadNodesAsync(SqliteConnection connection)
    {
        // Read node metadata first
        var nodeMetadata = new Dictionary<string, Dictionary<string, string>>();
        using (var metaCmd = connection.CreateCommand())
        {
            metaCmd.CommandText = "SELECT node_id, key, value FROM node_metadata";
            using var metaReader = await metaCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await metaReader.ReadAsync().ConfigureAwait(false))
            {
                var nodeId = metaReader.GetString(0);
                if (!nodeMetadata.TryGetValue(nodeId, out var dict))
                {
                    dict = new Dictionary<string, string>();
                    nodeMetadata[nodeId] = dict;
                }
                dict[metaReader.GetString(1)] = metaReader.GetString(2);
            }
        }

        var nodes = new Dictionary<string, GraphNode>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                   containing_type_id, containing_namespace_id, accessibility, assembly_name
            FROM nodes
            """;

        using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var node = new GraphNode
            {
                Id = id,
                Name = reader.GetString(1),
                Kind = (NodeKind)reader.GetInt32(2),
                FilePath = reader.GetString(3),
                StartLine = reader.GetInt32(4),
                EndLine = reader.GetInt32(5),
                Signature = reader.GetString(6),
                DocComment = reader.IsDBNull(7) ? null : reader.GetString(7),
                ContainingTypeId = reader.IsDBNull(8) ? null : reader.GetString(8),
                ContainingNamespaceId = reader.IsDBNull(9) ? null : reader.GetString(9),
                Accessibility = (Accessibility)reader.GetInt32(10),
                AssemblyName = reader.GetString(11),
                Metadata = nodeMetadata.TryGetValue(id, out var meta) ? meta : new Dictionary<string, string>()
            };
            nodes[id] = node;
        }

        return nodes;
    }

    private static async Task<List<GraphEdge>> ReadEdgesAsync(SqliteConnection connection)
    {
        // Read edge metadata first
        var edgeMetadata = new Dictionary<long, Dictionary<string, string>>();
        using (var metaCmd = connection.CreateCommand())
        {
            metaCmd.CommandText = "SELECT edge_rowid, key, value FROM edge_metadata";
            using var metaReader = await metaCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await metaReader.ReadAsync().ConfigureAwait(false))
            {
                var rowid = metaReader.GetInt64(0);
                if (!edgeMetadata.TryGetValue(rowid, out var dict))
                {
                    dict = new Dictionary<string, string>();
                    edgeMetadata[rowid] = dict;
                }
                dict[metaReader.GetString(1)] = metaReader.GetString(2);
            }
        }

        var edges = new List<GraphEdge>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, from_id, to_id, type, is_external, package_source, source_link, resolution, confidence
            FROM edges
            """;

        using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var rowid = reader.GetInt64(0);
            var edge = new GraphEdge
            {
                FromId = reader.GetString(1),
                ToId = reader.GetString(2),
                Type = (EdgeType)reader.GetInt32(3),
                IsExternal = reader.GetInt32(4) != 0,
                PackageSource = reader.IsDBNull(5) ? null : reader.GetString(5),
                SourceLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                Resolution = reader.IsDBNull(7) ? null : reader.GetString(7),
                Confidence = (EdgeConfidence)reader.GetInt32(8),
                Metadata = edgeMetadata.TryGetValue(rowid, out var meta) ? meta : new Dictionary<string, string>()
            };
            edges.Add(edge);
        }

        return edges;
    }

    private static TValue GetOrDefault<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        where TKey : notnull
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
