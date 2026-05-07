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
