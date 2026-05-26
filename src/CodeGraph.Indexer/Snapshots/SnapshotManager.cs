namespace CodeGraph.Indexer.Snapshots;

/// <summary>
/// Manages named snapshots of a graph directory under <c>&lt;graph-dir&gt;\snapshots\</c>.
/// </summary>
public class SnapshotManager
{
    private readonly string _graphDirectory;

    public SnapshotManager(string graphDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphDirectory);
        _graphDirectory = Path.GetFullPath(graphDirectory);
    }

    /// <summary>
    /// Copies graph.db, meta.json, and top-level JSON files from the configured graph directory
    /// into <c>&lt;graph-dir&gt;\snapshots\&lt;snapshotName&gt;\</c>. Existing snapshots are overwritten.
    /// </summary>
    public Task SaveAsync(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);

        if (!Directory.Exists(_graphDirectory))
            throw new DirectoryNotFoundException($"Graph directory not found: {_graphDirectory}");

        var metaPath = Path.Combine(_graphDirectory, "meta.json");
        if (!File.Exists(metaPath))
            throw new FileNotFoundException("meta.json not found in graph directory.", metaPath);

        var snapshotDir = GetSnapshotPath(snapshotName);
        if (Directory.Exists(snapshotDir))
            Directory.Delete(snapshotDir, recursive: true);

        Directory.CreateDirectory(snapshotDir);

        File.Copy(metaPath, Path.Combine(snapshotDir, "meta.json"));

        var dbPath = Path.Combine(_graphDirectory, "graph.db");
        if (File.Exists(dbPath))
            File.Copy(dbPath, Path.Combine(snapshotDir, "graph.db"));

        foreach (var jsonFile in Directory.GetFiles(_graphDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(jsonFile);
            if (fileName.Equals("meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(jsonFile, Path.Combine(snapshotDir, fileName));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Lists all snapshots found under <c>&lt;graph-dir&gt;\snapshots\</c>.
    /// </summary>
    public IReadOnlyList<SnapshotInfo> List()
    {
        var root = GetSnapshotRoot();
        if (!Directory.Exists(root))
            return Array.Empty<SnapshotInfo>();

        var results = new List<SnapshotInfo>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath))
                continue;

            var createdAt = File.GetLastWriteTimeUtc(metaPath);
            var name = Path.GetFileName(dir);
            results.Add(new SnapshotInfo(name, new DateTimeOffset(createdAt, TimeSpan.Zero), dir));
        }

        return results.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Deletes the snapshot with the given name. Returns <c>true</c> if it existed.
    /// </summary>
    public bool Delete(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);

        var snapshotDir = GetSnapshotPath(snapshotName);
        if (!Directory.Exists(snapshotDir))
            return false;

        Directory.Delete(snapshotDir, recursive: true);
        return true;
    }

    internal string GetSnapshotPath(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        return Path.Combine(GetSnapshotRoot(), snapshotName);
    }

    private string GetSnapshotRoot()
    {
        return Path.Combine(_graphDirectory, "snapshots");
    }
}

/// <summary>Describes a stored snapshot.</summary>
public record SnapshotInfo(string Name, DateTimeOffset CreatedAt, string Path);
