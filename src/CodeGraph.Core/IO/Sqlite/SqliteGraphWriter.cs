using System.Text.Json;
using CodeGraph.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.IO.Sqlite;

/// <summary>
/// Writes a complete code graph to a SQLite database file.
/// </summary>
public class SqliteGraphWriter
{
    public async Task WriteAsync(
        string dbPath,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        GraphMetadata metadata)
    {
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = SqliteSchema.CreateTables;
            await schemaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

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
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = SqliteSchema.CreateTables;
            await schemaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        using var transaction = connection.BeginTransaction();

        // Remove existing data for this solution
        await PurgeSolutionAsync(connection, solutionName).ConfigureAwait(false);

        // Tag each node with source_solution metadata and insert
        var taggedNodes = nodes.Select(n =>
        {
            var meta = new Dictionary<string, string>(n.Metadata)
            {
                ["source_solution"] = solutionName
            };
            return n with { Metadata = meta };
        });
        await WriteNodesAsync(connection, taggedNodes).ConfigureAwait(false);
        await WriteEdgesAsync(connection, edges).ConfigureAwait(false);

        // Update solutions list in metadata
        await UpdateSolutionsMetadataAsync(connection, solutionName).ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task PurgeSolutionAsync(SqliteConnection connection, string solutionName)
    {
        // Collect node IDs belonging to this solution
        using var collectCmd = connection.CreateCommand();
        collectCmd.CommandText =
            "SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln";
        collectCmd.Parameters.AddWithValue("$sln", solutionName);

        var nodeIds = new HashSet<string>();
        using (var reader = await collectCmd.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
                nodeIds.Add(reader.GetString(0));
        }

        if (nodeIds.Count == 0)
            return;

        // Delete edge_metadata for edges originating from these nodes
        using var delEdgeMeta = connection.CreateCommand();
        delEdgeMeta.CommandText = """
            DELETE FROM edge_metadata WHERE edge_rowid IN (
                SELECT rowid FROM edges WHERE from_id IN (
                    SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
                )
            )
            """;
        delEdgeMeta.Parameters.AddWithValue("$sln", solutionName);
        await delEdgeMeta.ExecuteNonQueryAsync().ConfigureAwait(false);

        // Delete edges originating from these nodes
        using var delEdges = connection.CreateCommand();
        delEdges.CommandText = """
            DELETE FROM edges WHERE from_id IN (
                SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
            )
            """;
        delEdges.Parameters.AddWithValue("$sln", solutionName);
        await delEdges.ExecuteNonQueryAsync().ConfigureAwait(false);

        // Delete all node_metadata for these nodes
        using var delNodeMeta = connection.CreateCommand();
        delNodeMeta.CommandText = """
            DELETE FROM node_metadata WHERE node_id IN (
                SELECT node_id FROM node_metadata WHERE key = 'source_solution' AND value = $sln
            )
            """;
        delNodeMeta.Parameters.AddWithValue("$sln", solutionName);
        await delNodeMeta.ExecuteNonQueryAsync().ConfigureAwait(false);

        // Delete nodes
        using var delNodes = connection.CreateCommand();
        delNodes.CommandText = """
            DELETE FROM nodes WHERE id IN ($ids)
            """;
        // Use a parameterized batch approach for node deletion
        delNodes.CommandText = "DELETE FROM nodes WHERE id IN (" +
            string.Join(",", nodeIds.Select((_, i) => $"$id{i}")) + ")";
        int idx = 0;
        foreach (var id in nodeIds)
        {
            delNodes.Parameters.AddWithValue($"$id{idx}", id);
            idx++;
        }
        await delNodes.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpdateSolutionsMetadataAsync(SqliteConnection connection, string solutionName)
    {
        // Read existing solutions list
        var existing = new List<string>();
        using (var readCmd = connection.CreateCommand())
        {
            readCmd.CommandText = "SELECT value FROM metadata WHERE key = 'solutions'";
            var result = await readCmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string json)
                existing = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }

        if (!existing.Contains(solutionName))
            existing.Add(solutionName);

        using var upsertCmd = connection.CreateCommand();
        upsertCmd.CommandText = """
            INSERT INTO metadata (key, value) VALUES ('solutions', $value)
            ON CONFLICT(key) DO UPDATE SET value = $value
            """;
        upsertCmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(existing));
        await upsertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WriteMetadataAsync(SqliteConnection connection, GraphMetadata metadata)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO metadata (key, value) VALUES ($key, $value)";

        var keyParam = cmd.Parameters.Add("$key", SqliteType.Text);
        var valueParam = cmd.Parameters.Add("$value", SqliteType.Text);

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
        {
            pairs[$"stat_{stat.Key}"] = stat.Value.ToString();
        }

        foreach (var pair in pairs)
        {
            keyParam.Value = pair.Key;
            valueParam.Value = pair.Value;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteNodesAsync(SqliteConnection connection, IEnumerable<GraphNode> nodes)
    {
        using var nodeCmd = connection.CreateCommand();
        nodeCmd.CommandText = """
            INSERT INTO nodes (id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                               containing_type_id, containing_namespace_id, accessibility, assembly_name)
            VALUES ($id, $name, $kind, $filePath, $startLine, $endLine, $signature, $docComment,
                    $containingTypeId, $containingNamespaceId, $accessibility, $assemblyName)
            """;

        var pId = nodeCmd.Parameters.Add("$id", SqliteType.Text);
        var pName = nodeCmd.Parameters.Add("$name", SqliteType.Text);
        var pKind = nodeCmd.Parameters.Add("$kind", SqliteType.Integer);
        var pFilePath = nodeCmd.Parameters.Add("$filePath", SqliteType.Text);
        var pStartLine = nodeCmd.Parameters.Add("$startLine", SqliteType.Integer);
        var pEndLine = nodeCmd.Parameters.Add("$endLine", SqliteType.Integer);
        var pSignature = nodeCmd.Parameters.Add("$signature", SqliteType.Text);
        var pDocComment = nodeCmd.Parameters.Add("$docComment", SqliteType.Text);
        var pContainingTypeId = nodeCmd.Parameters.Add("$containingTypeId", SqliteType.Text);
        var pContainingNamespaceId = nodeCmd.Parameters.Add("$containingNamespaceId", SqliteType.Text);
        var pAccessibility = nodeCmd.Parameters.Add("$accessibility", SqliteType.Integer);
        var pAssemblyName = nodeCmd.Parameters.Add("$assemblyName", SqliteType.Text);

        using var metaCmd = connection.CreateCommand();
        metaCmd.CommandText = "INSERT INTO node_metadata (node_id, key, value) VALUES ($nodeId, $key, $value)";
        var pmNodeId = metaCmd.Parameters.Add("$nodeId", SqliteType.Text);
        var pmKey = metaCmd.Parameters.Add("$key", SqliteType.Text);
        var pmValue = metaCmd.Parameters.Add("$value", SqliteType.Text);

        foreach (var node in nodes)
        {
            pId.Value = node.Id;
            pName.Value = node.Name;
            pKind.Value = (int)node.Kind;
            pFilePath.Value = node.FilePath;
            pStartLine.Value = node.StartLine;
            pEndLine.Value = node.EndLine;
            pSignature.Value = node.Signature;
            pDocComment.Value = (object?)node.DocComment ?? DBNull.Value;
            pContainingTypeId.Value = (object?)node.ContainingTypeId ?? DBNull.Value;
            pContainingNamespaceId.Value = (object?)node.ContainingNamespaceId ?? DBNull.Value;
            pAccessibility.Value = (int)node.Accessibility;
            pAssemblyName.Value = node.AssemblyName;

            await nodeCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            foreach (var meta in node.Metadata)
            {
                pmNodeId.Value = node.Id;
                pmKey.Value = meta.Key;
                pmValue.Value = meta.Value;
                await metaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteEdgesAsync(SqliteConnection connection, IEnumerable<GraphEdge> edges)
    {
        using var edgeCmd = connection.CreateCommand();
        edgeCmd.CommandText = """
            INSERT INTO edges (from_id, to_id, type, is_external, package_source, source_link, resolution, confidence)
            VALUES ($fromId, $toId, $type, $isExternal, $packageSource, $sourceLink, $resolution, $confidence)
            """;

        var pFromId = edgeCmd.Parameters.Add("$fromId", SqliteType.Text);
        var pToId = edgeCmd.Parameters.Add("$toId", SqliteType.Text);
        var pType = edgeCmd.Parameters.Add("$type", SqliteType.Integer);
        var pIsExternal = edgeCmd.Parameters.Add("$isExternal", SqliteType.Integer);
        var pPackageSource = edgeCmd.Parameters.Add("$packageSource", SqliteType.Text);
        var pSourceLink = edgeCmd.Parameters.Add("$sourceLink", SqliteType.Text);
        var pResolution = edgeCmd.Parameters.Add("$resolution", SqliteType.Text);
        var pConfidence = edgeCmd.Parameters.Add("$confidence", SqliteType.Integer);

        using var metaCmd = connection.CreateCommand();
        metaCmd.CommandText = "INSERT INTO edge_metadata (edge_rowid, key, value) VALUES ($edgeRowid, $key, $value)";
        var pmRowid = metaCmd.Parameters.Add("$edgeRowid", SqliteType.Integer);
        var pmKey = metaCmd.Parameters.Add("$key", SqliteType.Text);
        var pmValue = metaCmd.Parameters.Add("$value", SqliteType.Text);

        foreach (var edge in edges)
        {
            pFromId.Value = edge.FromId;
            pToId.Value = edge.ToId;
            pType.Value = (int)edge.Type;
            pIsExternal.Value = edge.IsExternal ? 1 : 0;
            pPackageSource.Value = (object?)edge.PackageSource ?? DBNull.Value;
            pSourceLink.Value = (object?)edge.SourceLink ?? DBNull.Value;
            pResolution.Value = (object?)edge.Resolution ?? DBNull.Value;
            pConfidence.Value = (int)edge.Confidence;

            await edgeCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            if (edge.Metadata.Count > 0)
            {
                // Get the rowid of the just-inserted edge
                using var rowidCmd = connection.CreateCommand();
                rowidCmd.CommandText = "SELECT last_insert_rowid()";
                var rowid = (long)(await rowidCmd.ExecuteScalarAsync().ConfigureAwait(false))!;

                foreach (var meta in edge.Metadata)
                {
                    pmRowid.Value = rowid;
                    pmKey.Value = meta.Key;
                    pmValue.Value = meta.Value;
                    await metaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
