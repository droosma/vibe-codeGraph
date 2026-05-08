using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Indexer.Snapshots;

/// <summary>
/// Manages named snapshots of graph directories under <c>.codegraph-snapshots/</c>.
/// </summary>
public class SnapshotManager
{
    private const string SnapshotRoot = ".codegraph-snapshots";

    private readonly string _workingDirectory;

    public SnapshotManager(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
    }

    /// <summary>
    /// Copies graph.db, meta.json, and all JSON files from <paramref name="graphDir"/>
    /// into <c>.codegraph-snapshots/&lt;snapshotName&gt;/</c>.
    /// If a snapshot with the same name already exists it is overwritten.
    /// </summary>
    public async Task SaveAsync(string graphDir, string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);

        var sourceDir = Path.GetFullPath(Path.Combine(_workingDirectory, graphDir));
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Graph directory not found: {sourceDir}");

        var metaPath = Path.Combine(sourceDir, "meta.json");
        if (!File.Exists(metaPath))
            throw new FileNotFoundException("meta.json not found in graph directory.", metaPath);

        var snapshotDir = GetSnapshotPath(snapshotName);

        if (Directory.Exists(snapshotDir))
            Directory.Delete(snapshotDir, recursive: true);

        Directory.CreateDirectory(snapshotDir);

        // Copy meta.json
        File.Copy(metaPath, Path.Combine(snapshotDir, "meta.json"));

        // Copy graph.db if present
        var dbPath = Path.Combine(sourceDir, "graph.db");
        if (File.Exists(dbPath))
            File.Copy(dbPath, Path.Combine(snapshotDir, "graph.db"));

        // Copy all JSON data files (excluding meta.json)
        foreach (var jsonFile in Directory.GetFiles(sourceDir, "*.json"))
        {
            var fileName = Path.GetFileName(jsonFile);
            if (fileName.Equals("meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(jsonFile, Path.Combine(snapshotDir, fileName));
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Lists all snapshots found under <c>.codegraph-snapshots/</c>.
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

        return results
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
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
        return Path.Combine(GetSnapshotRoot(), snapshotName);
    }

    private string GetSnapshotRoot()
    {
        return Path.Combine(_workingDirectory, SnapshotRoot);
    }
}

/// <summary>Describes a stored snapshot.</summary>
public record SnapshotInfo(string Name, DateTimeOffset CreatedAt, string Path);
