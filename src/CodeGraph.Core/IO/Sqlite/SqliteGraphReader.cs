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
    private const string SelectMetadataSql = "SELECT key, value FROM metadata";
    private const string SelectAllNodesSql = """
        SELECT id, name, kind, file_path, start_line, end_line, signature, doc_comment,
               containing_type_id, containing_namespace_id, accessibility, assembly_name
        FROM nodes
        """;
    private const string SelectAllEdgesSql = """
        SELECT rowid, from_id, to_id, type, is_external, package_source, source_link, resolution, confidence
        FROM edges
        """;
    private const string SelectAllNodeMetadataSql = "SELECT node_id, key, value FROM node_metadata";
    private const string SelectAllEdgeMetadataSql = "SELECT edge_rowid, key, value FROM edge_metadata";
    private const string SelectNodeIdsByNameSql = "SELECT id FROM nodes WHERE name LIKE $pattern";
    private const string SelectNodesByNameSql = """
        SELECT id, name, kind, file_path, start_line, end_line, signature, doc_comment,
               containing_type_id, containing_namespace_id, accessibility, assembly_name
        FROM nodes WHERE name LIKE $pattern
        """;

    /// <summary>
    /// Reads the complete graph from a SQLite database (equivalent to JSON GraphReader).
    /// </summary>
    public static async Task<(GraphMetadata Metadata, Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges)> ReadAsync(
        string dbPath)
    {
        EnsureDatabaseExists(dbPath);

        using var connection = await OpenReadOnlyConnectionAsync(dbPath).ConfigureAwait(false);
        var metadata = await ReadMetadataAsync(connection).ConfigureAwait(false);
        var nodes = await ReadNodesAsync(connection).ConfigureAwait(false);
        var edges = await ReadEdgesAsync(connection).ConfigureAwait(false);

        return (metadata, nodes, edges);
    }

    /// <summary>
    /// Opens the database read-only and returns only the nodes whose IDs match
    /// the supplied set. Useful for cross-solution reference resolution without
    /// loading the full graph into memory.
    /// </summary>
    public static async Task<Dictionary<string, GraphNode>> FindNodesByIds(
        string dbPath, IEnumerable<string> nodeIds)
    {
        EnsureDatabaseExists(dbPath);

        var requestedNodeIds = nodeIds.ToList();
        if (requestedNodeIds.Count == 0)
            return new Dictionary<string, GraphNode>();

        using var connection = await OpenReadOnlyConnectionAsync(dbPath).ConfigureAwait(false);
        var metadataByNodeId = await ReadNodeMetadataAsync(connection, requestedNodeIds).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        var inClause = AddInClauseParameters(command, "$id", requestedNodeIds);
        command.CommandText = $"""
            SELECT id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                   containing_type_id, containing_namespace_id, accessibility, assembly_name
            FROM nodes WHERE id IN ({inClause})
            """;

        var nodes = new Dictionary<string, GraphNode>();
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var node = MaterializeNode(reader, metadataByNodeId);
            nodes[node.Id] = node;
        }

        return nodes;
    }

    /// <summary>
    /// Opens the database read-only and returns nodes whose name matches the
    /// supplied SQL LIKE pattern (e.g. <c>%Service%</c>).
    /// </summary>
    public static async Task<List<GraphNode>> FindNodesByName(string dbPath, string namePattern)
    {
        EnsureDatabaseExists(dbPath);

        using var connection = await OpenReadOnlyConnectionAsync(dbPath).ConfigureAwait(false);
        var matchingNodeIds = await ReadMatchingNodeIdsByNameAsync(connection, namePattern).ConfigureAwait(false);
        if (matchingNodeIds.Count == 0)
            return new List<GraphNode>();

        var metadataByNodeId = await ReadNodeMetadataAsync(connection, matchingNodeIds).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = SelectNodesByNameSql;
        command.Parameters.AddWithValue("$pattern", namePattern);

        var nodes = new List<GraphNode>();
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            nodes.Add(MaterializeNode(reader, metadataByNodeId));

        return nodes;
    }

    private static void EnsureDatabaseExists(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Graph database not found.", dbPath);
    }

    private static async Task<SqliteConnection> OpenReadOnlyConnectionAsync(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task<GraphMetadata> ReadMetadataAsync(SqliteConnection connection)
    {
        var metadataEntries = await ReadStringPairsAsync(connection, SelectMetadataSql).ConfigureAwait(false);
        var stats = ReadStats(metadataEntries);

        return new GraphMetadata
        {
            SchemaVersion = int.Parse(GetOrDefault(metadataEntries, "schema_version", "1"), CultureInfo.InvariantCulture),
            CommitHash = GetOrDefault(metadataEntries, "commit_hash", string.Empty),
            Branch = GetOrDefault(metadataEntries, "branch", string.Empty),
            GeneratedAt = metadataEntries.TryGetValue("generated_at", out var generatedAt)
                ? DateTimeOffset.Parse(generatedAt, CultureInfo.InvariantCulture)
                : DateTimeOffset.MinValue,
            IndexerVersion = GetOrDefault(metadataEntries, "indexer_version", string.Empty),
            Solution = GetOrDefault(metadataEntries, "solution", string.Empty),
            SolutionName = GetOrDefault(metadataEntries, "solution_name", string.Empty),
            ProjectsIndexed = metadataEntries.TryGetValue("projects_indexed", out var projectsIndexedJson)
                ? JsonSerializer.Deserialize<string[]>(projectsIndexedJson) ?? Array.Empty<string>()
                : Array.Empty<string>(),
            Stats = stats
        };
    }

    private static async Task<Dictionary<string, GraphNode>> ReadNodesAsync(SqliteConnection connection)
    {
        var metadataByNodeId = await ReadNodeMetadataAsync(connection).ConfigureAwait(false);
        var nodes = new Dictionary<string, GraphNode>();

        using var command = connection.CreateCommand();
        command.CommandText = SelectAllNodesSql;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var node = MaterializeNode(reader, metadataByNodeId);
            nodes[node.Id] = node;
        }

        return nodes;
    }

    private static async Task<List<GraphEdge>> ReadEdgesAsync(SqliteConnection connection)
    {
        var metadataByEdgeRowId = await ReadEdgeMetadataAsync(connection).ConfigureAwait(false);
        var edges = new List<GraphEdge>();

        using var command = connection.CreateCommand();
        command.CommandText = SelectAllEdgesSql;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            edges.Add(MaterializeEdge(reader, metadataByEdgeRowId));

        return edges;
    }

    private static async Task<List<string>> ReadMatchingNodeIdsByNameAsync(SqliteConnection connection, string namePattern)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectNodeIdsByNameSql;
        command.Parameters.AddWithValue("$pattern", namePattern);

        var matchingNodeIds = new List<string>();
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            matchingNodeIds.Add(reader.GetString(0));

        return matchingNodeIds;
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>> ReadNodeMetadataAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectAllNodeMetadataSql;

        return await ReadMetadataLookupAsync(command, reader => reader.GetString(0)).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>> ReadNodeMetadataAsync(
        SqliteConnection connection,
        IReadOnlyList<string> nodeIds)
    {
        using var command = connection.CreateCommand();
        var inClause = AddInClauseParameters(command, "$id", nodeIds);
        command.CommandText = $"SELECT node_id, key, value FROM node_metadata WHERE node_id IN ({inClause})";

        return await ReadMetadataLookupAsync(command, reader => reader.GetString(0)).ConfigureAwait(false);
    }

    private static async Task<Dictionary<long, Dictionary<string, string>>> ReadEdgeMetadataAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectAllEdgeMetadataSql;

        return await ReadMetadataLookupAsync(command, reader => reader.GetInt64(0)).ConfigureAwait(false);
    }

    private static async Task<Dictionary<TKey, Dictionary<string, string>>> ReadMetadataLookupAsync<TKey>(
        SqliteCommand command,
        Func<SqliteDataReader, TKey> ownerIdSelector)
        where TKey : notnull
    {
        var metadata = new Dictionary<TKey, Dictionary<string, string>>();

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var ownerId = ownerIdSelector(reader);
            if (!metadata.TryGetValue(ownerId, out var values))
            {
                values = new Dictionary<string, string>();
                metadata[ownerId] = values;
            }

            values[reader.GetString(1)] = reader.GetString(2);
        }

        return metadata;
    }

    private static async Task<Dictionary<string, string>> ReadStringPairsAsync(SqliteConnection connection, string commandText)
    {
        var values = new Dictionary<string, string>();

        using var command = connection.CreateCommand();
        command.CommandText = commandText;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            values[reader.GetString(0)] = reader.GetString(1);

        return values;
    }

    private static Dictionary<string, int> ReadStats(IReadOnlyDictionary<string, string> metadataEntries)
    {
        var stats = new Dictionary<string, int>();

        foreach (var entry in metadataEntries)
        {
            if (!entry.Key.StartsWith("stat_", StringComparison.Ordinal))
                continue;

            var statName = entry.Key.Substring("stat_".Length);
            if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var statValue))
                stats[statName] = statValue;
        }

        return stats;
    }

    private static GraphNode MaterializeNode(
        SqliteDataReader reader,
        IReadOnlyDictionary<string, Dictionary<string, string>> metadataByNodeId)
    {
        var nodeId = reader.GetString(0);

        return new GraphNode
        {
            Id = nodeId,
            Name = reader.GetString(1),
            Kind = (NodeKind)reader.GetInt32(2),
            FilePath = reader.GetString(3),
            StartLine = reader.GetInt32(4),
            EndLine = reader.GetInt32(5),
            Signature = reader.GetString(6),
            DocComment = GetNullableString(reader, 7),
            ContainingTypeId = GetNullableString(reader, 8),
            ContainingNamespaceId = GetNullableString(reader, 9),
            Accessibility = (Accessibility)reader.GetInt32(10),
            AssemblyName = reader.GetString(11),
            Metadata = GetMetadataOrEmpty(metadataByNodeId, nodeId)
        };
    }

    private static GraphEdge MaterializeEdge(
        SqliteDataReader reader,
        IReadOnlyDictionary<long, Dictionary<string, string>> metadataByEdgeRowId)
    {
        var rowId = reader.GetInt64(0);

        return new GraphEdge
        {
            FromId = reader.GetString(1),
            ToId = reader.GetString(2),
            Type = (EdgeType)reader.GetInt32(3),
            IsExternal = reader.GetInt32(4) != 0,
            PackageSource = GetNullableString(reader, 5),
            SourceLink = GetNullableString(reader, 6),
            Resolution = GetNullableString(reader, 7),
            Confidence = (EdgeConfidence)reader.GetInt32(8),
            Metadata = GetMetadataOrEmpty(metadataByEdgeRowId, rowId)
        };
    }

    private static string AddInClauseParameters(SqliteCommand command, string parameterPrefix, IReadOnlyList<string> values)
    {
        var parameterNames = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var parameterName = $"{parameterPrefix}{i}";
            parameterNames[i] = parameterName;
            command.Parameters.AddWithValue(parameterName, values[i]);
        }

        return string.Join(",", parameterNames);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Dictionary<string, string> GetMetadataOrEmpty<TKey>(
        IReadOnlyDictionary<TKey, Dictionary<string, string>> metadataByOwnerId,
        TKey ownerId)
        where TKey : notnull
    {
        return metadataByOwnerId.TryGetValue(ownerId, out var metadata)
            ? metadata
            : new Dictionary<string, string>();
    }

    private static TValue GetOrDefault<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        where TKey : notnull
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
