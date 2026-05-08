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
        var connectionString = BuildConnectionString(SqliteOpenMode.ReadWriteCreate);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await EnsureSchemaAsync(connection).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        using var upsertCmd = connection.CreateCommand();
        upsertCmd.CommandText = """
            INSERT INTO file_hashes (file_path, hash, last_indexed)
            VALUES ($path, $hash, $ts)
            ON CONFLICT(file_path) DO UPDATE SET
                hash = excluded.hash,
                last_indexed = excluded.last_indexed;
            """;

        var pathParam = upsertCmd.Parameters.Add("$path", SqliteType.Text);
        var hashParam = upsertCmd.Parameters.Add("$hash", SqliteType.Text);
        var tsParam = upsertCmd.Parameters.Add("$ts", SqliteType.Text);

        var now = DateTimeOffset.UtcNow.ToString("o");

        foreach (var kvp in fileHashes)
        {
            pathParam.Value = NormalizePath(kvp.Key);
            hashParam.Value = kvp.Value;
            tsParam.Value = now;
            await upsertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

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

        var connectionString = BuildConnectionString(SqliteOpenMode.ReadOnly);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        if (!await TableExistsAsync(connection).ConfigureAwait(false))
            return new Dictionary<string, string>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT file_path, hash FROM file_hashes;";

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
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

        foreach (var file in currentFiles)
        {
            var normalized = NormalizePath(file);
            seenPaths.Add(normalized);

            if (!storedHashes.TryGetValue(normalized, out var storedHash))
            {
                added.Add(file);
            }
            else
            {
                var currentHash = ComputeHash(file);
                if (!string.Equals(currentHash, storedHash, StringComparison.OrdinalIgnoreCase))
                {
                    modified.Add(file);
                }
            }
        }

        var removed = new List<string>();
        foreach (var kvp in storedHashes)
        {
            if (!seenPaths.Contains(kvp.Key))
            {
                removed.Add(kvp.Key);
            }
        }

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

    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS file_hashes (
                file_path TEXT PRIMARY KEY,
                hash TEXT NOT NULL,
                last_indexed TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='file_hashes';";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt64(result) > 0;
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
