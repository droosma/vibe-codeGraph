using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.IO;

/// <summary>
/// Tracks file content hashes in a SQLite database to enable incremental indexing.
/// Compares current file hashes against previously stored values to detect added,
/// modified, and removed files.
/// </summary>
public class FileHashTracker
{
    private const string UpsertFileHashesSql = """
        INSERT INTO file_hashes (file_path, hash, last_indexed)
        VALUES ($path, $hash, $ts)
        ON CONFLICT(file_path) DO UPDATE SET
            hash = excluded.hash,
            last_indexed = excluded.last_indexed;
        """;
    private const string SelectFileHashesSql = "SELECT file_path, hash FROM file_hashes;";
    private const string CreateFileHashesTableSql = """
        CREATE TABLE IF NOT EXISTS file_hashes (
            file_path TEXT PRIMARY KEY,
            hash TEXT NOT NULL,
            last_indexed TEXT NOT NULL
        );
        """;
    private const string CheckFileHashesTableExistsSql =
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='file_hashes';";

    private readonly string _hashDbPath;

    public FileHashTracker(string graphDir)
    {
        if (string.IsNullOrWhiteSpace(graphDir))
            throw new ArgumentException("Graph directory must not be null or empty.", nameof(graphDir));

        _hashDbPath = Path.Combine(graphDir, "filehashes.db");
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file's contents.
    /// </summary>
    public static string ComputeHash(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);

#if NET8_0_OR_GREATER
        return Convert.ToHexString(hashBytes);
#else
        return BitConverter.ToString(hashBytes).Replace("-", "");
#endif
    }

    /// <summary>
    /// Saves file hashes to the tracking database, replacing any existing entries.
    /// </summary>
    public async Task SaveHashesAsync(IReadOnlyDictionary<string, string> fileHashes)
    {
        using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadWriteCreate).ConfigureAwait(false);
        await EnsureSchemaAsync(connection).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();
        await UpsertHashesAsync(connection, fileHashes).ConfigureAwait(false);
        transaction.Commit();
    }

    /// <summary>
    /// Loads all previously stored file hashes from the tracking database.
    /// Returns an empty dictionary if the database does not exist.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadHashesAsync()
    {
        if (!File.Exists(_hashDbPath))
            return new Dictionary<string, string>();

        using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadOnly).ConfigureAwait(false);
        if (!await TableExistsAsync(connection).ConfigureAwait(false))
            return new Dictionary<string, string>();

        return await ReadHashesAsync(connection).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares the set of current files against stored hashes to detect changes.
    /// Files present now but not stored are "Added".
    /// Files present now with a different hash are "Modified".
    /// Files in stored hashes but not in current files are "Removed".
    /// </summary>
    public async Task<FileChangeSet> DetectChangesAsync(IEnumerable<string> currentFiles)
    {
        var storedHashes = await LoadHashesAsync().ConfigureAwait(false);
        var added = new List<string>();
        var modified = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CategorizeCurrentFiles(currentFiles, storedHashes, seenPaths, added, modified);
        var removed = FindRemovedFiles(storedHashes, seenPaths);
        return new FileChangeSet(added, modified, removed);
    }

    private string BuildConnectionString(SqliteOpenMode mode)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = _hashDbPath,
            Mode = mode,
            Pooling = false
        }.ToString();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(BuildConnectionString(mode));
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task UpsertHashesAsync(SqliteConnection connection, IReadOnlyDictionary<string, string> fileHashes)
    {
        using var command = connection.CreateCommand();
        command.CommandText = UpsertFileHashesSql;
        command.Parameters.Add("$path", SqliteType.Text);
        command.Parameters.Add("$hash", SqliteType.Text);
        command.Parameters.Add("$ts", SqliteType.Text);

        var indexedAt = DateTimeOffset.UtcNow.ToString("o");
        foreach (var fileHash in fileHashes)
        {
            SetParameterValue(command, "$path", NormalizePath(fileHash.Key));
            SetParameterValue(command, "$hash", fileHash.Value);
            SetParameterValue(command, "$ts", indexedAt);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadHashesAsync(SqliteConnection connection)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = SelectFileHashesSql;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
            hashes[reader.GetString(0)] = reader.GetString(1);

        return hashes;
    }

    private static void CategorizeCurrentFiles(
        IEnumerable<string> currentFiles,
        IReadOnlyDictionary<string, string> storedHashes,
        ISet<string> seenPaths,
        ICollection<string> added,
        ICollection<string> modified)
    {
        foreach (var currentFile in currentFiles)
        {
            var normalizedPath = NormalizePath(currentFile);
            seenPaths.Add(normalizedPath);

            if (!storedHashes.TryGetValue(normalizedPath, out var storedHash))
            {
                added.Add(currentFile);
                continue;
            }

            var currentHash = ComputeHash(currentFile);
            if (!string.Equals(currentHash, storedHash, StringComparison.OrdinalIgnoreCase))
                modified.Add(currentFile);
        }
    }

    private static List<string> FindRemovedFiles(
        IReadOnlyDictionary<string, string> storedHashes,
        ISet<string> seenPaths)
    {
        var removed = new List<string>();

        foreach (var storedHash in storedHashes)
        {
            if (!seenPaths.Contains(storedHash.Key))
                removed.Add(storedHash.Key);
        }

        return removed;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CreateFileHashesTableSql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CheckFileHashesTableExistsSql;
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt64(result) > 0;
    }

    private static void SetParameterValue(SqliteCommand command, string parameterName, object? value)
    {
        command.Parameters[parameterName].Value = value ?? DBNull.Value;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}

/// <summary>
/// Represents the set of file changes detected by comparing current files against stored hashes.
/// </summary>
public record FileChangeSet(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Removed);
