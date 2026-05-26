using System.Text.Json;
using CodeGraph.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.IO.Sqlite;

/// <summary>
/// Writes a complete code graph to a SQLite database file.
/// </summary>
public class SqliteGraphWriter
{
    private const string SelectSolutionNodeIdsSql =
        "SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln";
    private const string DeleteSolutionEdgeMetadataSql = """
        DELETE FROM edge_metadata WHERE edge_rowid IN (
            SELECT rowid FROM edges WHERE from_id IN (
                SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
            )
        )
        """;
    private const string DeleteSolutionEdgesSql = """
        DELETE FROM edges WHERE from_id IN (
            SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
        )
        """;
    private const string DeleteSolutionNodeMetadataSql = """
        DELETE FROM node_metadata WHERE node_id IN (
            SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
        )
        """;
    private const string SelectSolutionsMetadataSql = "SELECT value FROM metadata WHERE key = 'solutions'";
    private const string UpsertSolutionsMetadataSql = """
        INSERT INTO metadata (key, value) VALUES ('solutions', $value)
        ON CONFLICT(key) DO UPDATE SET value = $value
        """;
    private const string InsertMetadataSql = "INSERT INTO metadata (key, value) VALUES ($key, $value)";
    private const string InsertNodeSql = """
        INSERT OR REPLACE INTO nodes (id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                           containing_type_id, containing_namespace_id, accessibility, assembly_name)
        VALUES ($id, $name, $kind, $filePath, $startLine, $endLine, $signature, $docComment,
                $containingTypeId, $containingNamespaceId, $accessibility, $assemblyName)
        """;
    private const string InsertNodeMetadataSql =
        "INSERT OR REPLACE INTO node_metadata (node_id, key, value) VALUES ($nodeId, $key, $value)";
    private const string InsertEdgeSql = """
        INSERT OR IGNORE INTO edges (from_id, to_id, type, is_external, package_source, source_link, resolution, confidence)
        VALUES ($fromId, $toId, $type, $isExternal, $packageSource, $sourceLink, $resolution, $confidence)
        """;
    private const string InsertEdgeMetadataSql =
        "INSERT INTO edge_metadata (edge_rowid, key, value) VALUES ($edgeRowid, $key, $value)";

    public async Task WriteAsync(
        string dbPath,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        GraphMetadata metadata)
    {
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        using var connection = await OpenReadWriteConnectionAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaAsync(connection).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();
        await WriteMetadataAsync(connection, metadata).ConfigureAwait(false);
        await WriteNodesAsync(connection, nodes).ConfigureAwait(false);
        await WriteEdgesAsync(connection, edges).ConfigureAwait(false);
        transaction.Commit();
    }

    /// <summary>
    /// Appends nodes and edges from a solution into an existing (or new) database without
    /// deleting prior data from other solutions. Existing data for the same
    /// <paramref name="solutionName"/> is removed first so re-indexing is idempotent.
    /// </summary>
    public async Task AppendAsync(
        string dbPath,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        string solutionName)
    {
        EnsureDatabaseDirectoryExists(dbPath);

        using var connection = await OpenReadWriteConnectionAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaAsync(connection).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();
        await PurgeSolutionAsync(connection, solutionName).ConfigureAwait(false);
        await WriteNodesAsync(connection, AddSourceSolutionMetadata(nodes, solutionName)).ConfigureAwait(false);
        await WriteEdgesAsync(connection, edges).ConfigureAwait(false);
        await UpdateSolutionsMetadataAsync(connection, solutionName).ConfigureAwait(false);
        transaction.Commit();
    }

    private static void EnsureDatabaseDirectoryExists(string dbPath)
    {
        var directoryPath = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directoryPath))
            Directory.CreateDirectory(directoryPath);
    }

    private static async Task<SqliteConnection> OpenReadWriteConnectionAsync(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SqliteSchema.CreateTables;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static IEnumerable<GraphNode> AddSourceSolutionMetadata(IEnumerable<GraphNode> nodes, string solutionName)
    {
        return nodes.Select(node =>
        {
            var metadata = new Dictionary<string, string>(node.Metadata)
            {
                ["source_solution"] = solutionName
            };

            return node with { Metadata = metadata };
        });
    }

    private static async Task PurgeSolutionAsync(SqliteConnection connection, string solutionName)
    {
        var solutionNodeIds = await LoadSolutionNodeIdsAsync(connection, solutionName).ConfigureAwait(false);
        if (solutionNodeIds.Count == 0)
            return;

        await ExecuteSolutionCommandAsync(connection, DeleteSolutionEdgeMetadataSql, solutionName).ConfigureAwait(false);
        await ExecuteSolutionCommandAsync(connection, DeleteSolutionEdgesSql, solutionName).ConfigureAwait(false);
        await ExecuteSolutionCommandAsync(connection, DeleteSolutionNodeMetadataSql, solutionName).ConfigureAwait(false);
        await DeleteNodesAsync(connection, solutionNodeIds).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> LoadSolutionNodeIdsAsync(SqliteConnection connection, string solutionName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectSolutionNodeIdsSql;
        command.Parameters.AddWithValue("$sln", solutionName);

        var nodeIds = new HashSet<string>();
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            nodeIds.Add(reader.GetString(0));

        return nodeIds;
    }

    private static async Task ExecuteSolutionCommandAsync(
        SqliteConnection connection,
        string commandText,
        string solutionName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$sln", solutionName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task DeleteNodesAsync(SqliteConnection connection, IReadOnlyCollection<string> nodeIds)
    {
        using var command = connection.CreateCommand();
        var inClause = AddInClauseParameters(command, "$id", nodeIds);
        command.CommandText = $"DELETE FROM nodes WHERE id IN ({inClause})";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpdateSolutionsMetadataAsync(SqliteConnection connection, string solutionName)
    {
        var solutions = await ReadSolutionsMetadataAsync(connection).ConfigureAwait(false);
        if (!solutions.Contains(solutionName))
            solutions.Add(solutionName);

        using var command = connection.CreateCommand();
        command.CommandText = UpsertSolutionsMetadataSql;
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(solutions));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<List<string>> ReadSolutionsMetadataAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectSolutionsMetadataSql;

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        if (result is string json)
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

        return new List<string>();
    }

    private static async Task WriteMetadataAsync(SqliteConnection connection, GraphMetadata metadata)
    {
        await WriteKeyValuePairsAsync(connection, InsertMetadataSql, BuildMetadataPairs(metadata)).ConfigureAwait(false);
    }

    private static Dictionary<string, string> BuildMetadataPairs(GraphMetadata metadata)
    {
        var pairs = new Dictionary<string, string>
        {
            ["schema_version"] = metadata.SchemaVersion.ToString(),
            ["commit_hash"] = metadata.CommitHash,
            ["branch"] = metadata.Branch,
            ["generated_at"] = metadata.GeneratedAt.ToString("O"),
            ["indexer_version"] = metadata.IndexerVersion,
            ["solution"] = metadata.Solution,
            ["solution_name"] = metadata.SolutionName,
            ["projects_indexed"] = JsonSerializer.Serialize(metadata.ProjectsIndexed)
        };

        foreach (var stat in metadata.Stats)
            pairs[$"stat_{stat.Key}"] = stat.Value.ToString();

        return pairs;
    }

    private static async Task WriteKeyValuePairsAsync(
        SqliteConnection connection,
        string commandText,
        IEnumerable<KeyValuePair<string, string>> pairs)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$value", SqliteType.Text);

        foreach (var pair in pairs)
        {
            SetParameterValue(command, "$key", pair.Key);
            SetParameterValue(command, "$value", pair.Value);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteNodesAsync(SqliteConnection connection, IEnumerable<GraphNode> nodes)
    {
        using var nodeCommand = CreateNodeInsertCommand(connection);
        using var metadataCommand = CreateNodeMetadataInsertCommand(connection);

        foreach (var node in nodes)
        {
            BindNodeParameters(nodeCommand, node);
            await nodeCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            await WriteNodeMetadataAsync(metadataCommand, node).ConfigureAwait(false);
        }
    }

    private static SqliteCommand CreateNodeInsertCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = InsertNodeSql;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$kind", SqliteType.Integer);
        command.Parameters.Add("$filePath", SqliteType.Text);
        command.Parameters.Add("$startLine", SqliteType.Integer);
        command.Parameters.Add("$endLine", SqliteType.Integer);
        command.Parameters.Add("$signature", SqliteType.Text);
        command.Parameters.Add("$docComment", SqliteType.Text);
        command.Parameters.Add("$containingTypeId", SqliteType.Text);
        command.Parameters.Add("$containingNamespaceId", SqliteType.Text);
        command.Parameters.Add("$accessibility", SqliteType.Integer);
        command.Parameters.Add("$assemblyName", SqliteType.Text);
        return command;
    }

    private static SqliteCommand CreateNodeMetadataInsertCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = InsertNodeMetadataSql;
        command.Parameters.Add("$nodeId", SqliteType.Text);
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$value", SqliteType.Text);
        return command;
    }

    private static void BindNodeParameters(SqliteCommand command, GraphNode node)
    {
        SetParameterValue(command, "$id", node.Id);
        SetParameterValue(command, "$name", node.Name);
        SetParameterValue(command, "$kind", (int)node.Kind);
        SetParameterValue(command, "$filePath", node.FilePath);
        SetParameterValue(command, "$startLine", node.StartLine);
        SetParameterValue(command, "$endLine", node.EndLine);
        SetParameterValue(command, "$signature", node.Signature);
        SetParameterValue(command, "$docComment", node.DocComment);
        SetParameterValue(command, "$containingTypeId", node.ContainingTypeId);
        SetParameterValue(command, "$containingNamespaceId", node.ContainingNamespaceId);
        SetParameterValue(command, "$accessibility", (int)node.Accessibility);
        SetParameterValue(command, "$assemblyName", node.AssemblyName);
    }

    private static async Task WriteNodeMetadataAsync(SqliteCommand command, GraphNode node)
    {
        foreach (var metadataEntry in node.Metadata)
        {
            SetParameterValue(command, "$nodeId", node.Id);
            SetParameterValue(command, "$key", metadataEntry.Key);
            SetParameterValue(command, "$value", metadataEntry.Value);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteEdgesAsync(SqliteConnection connection, IEnumerable<GraphEdge> edges)
    {
        using var edgeCommand = CreateEdgeInsertCommand(connection);
        using var metadataCommand = CreateEdgeMetadataInsertCommand(connection);

        foreach (var edge in edges)
        {
            BindEdgeParameters(edgeCommand, edge);
            await edgeCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            await WriteEdgeMetadataAsync(connection, metadataCommand, edge).ConfigureAwait(false);
        }
    }

    private static SqliteCommand CreateEdgeInsertCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = InsertEdgeSql;
        command.Parameters.Add("$fromId", SqliteType.Text);
        command.Parameters.Add("$toId", SqliteType.Text);
        command.Parameters.Add("$type", SqliteType.Integer);
        command.Parameters.Add("$isExternal", SqliteType.Integer);
        command.Parameters.Add("$packageSource", SqliteType.Text);
        command.Parameters.Add("$sourceLink", SqliteType.Text);
        command.Parameters.Add("$resolution", SqliteType.Text);
        command.Parameters.Add("$confidence", SqliteType.Integer);
        return command;
    }

    private static SqliteCommand CreateEdgeMetadataInsertCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = InsertEdgeMetadataSql;
        command.Parameters.Add("$edgeRowid", SqliteType.Integer);
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$value", SqliteType.Text);
        return command;
    }

    private static void BindEdgeParameters(SqliteCommand command, GraphEdge edge)
    {
        SetParameterValue(command, "$fromId", edge.FromId);
        SetParameterValue(command, "$toId", edge.ToId);
        SetParameterValue(command, "$type", (int)edge.Type);
        SetParameterValue(command, "$isExternal", edge.IsExternal ? 1 : 0);
        SetParameterValue(command, "$packageSource", edge.PackageSource);
        SetParameterValue(command, "$sourceLink", edge.SourceLink);
        SetParameterValue(command, "$resolution", edge.Resolution);
        SetParameterValue(command, "$confidence", (int)edge.Confidence);
    }

    private static async Task WriteEdgeMetadataAsync(
        SqliteConnection connection,
        SqliteCommand command,
        GraphEdge edge)
    {
        if (edge.Metadata.Count == 0)
            return;

        var rowId = await ReadLastInsertRowIdAsync(connection).ConfigureAwait(false);
        foreach (var metadataEntry in edge.Metadata)
        {
            SetParameterValue(command, "$edgeRowid", rowId);
            SetParameterValue(command, "$key", metadataEntry.Key);
            SetParameterValue(command, "$value", metadataEntry.Value);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task<long> ReadLastInsertRowIdAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid()";
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private static string AddInClauseParameters(
        SqliteCommand command,
        string parameterPrefix,
        IEnumerable<string> values)
    {
        var parameterNames = new List<string>();
        var parameterIndex = 0;

        foreach (var value in values)
        {
            var parameterName = $"{parameterPrefix}{parameterIndex}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, value);
            parameterIndex++;
        }

        return string.Join(",", parameterNames);
    }

    private static void SetParameterValue(SqliteCommand command, string parameterName, object? value)
    {
        command.Parameters[parameterName].Value = value ?? DBNull.Value;
    }
}
