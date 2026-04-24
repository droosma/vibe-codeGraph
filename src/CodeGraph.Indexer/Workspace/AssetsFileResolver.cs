using System.Collections.Concurrent;
using System.Text.Json;

namespace CodeGraph.Indexer.Workspace;

public record ResolvedPackage(string PackageId, string Version, string DllPath);

public static class AssetsFileResolver
{
    private static readonly ConcurrentDictionary<(string Directory, string Framework), IReadOnlyList<ResolvedPackage>> s_cache = new();

    public static IReadOnlyList<ResolvedPackage> Resolve(string projectDirectory, string targetFramework)
    {
        var key = (projectDirectory, targetFramework);
        return s_cache.GetOrAdd(key, static k => ResolveCore(k.Directory, k.Framework));
    }

    internal static void ClearCache() => s_cache.Clear();

    private static IReadOnlyList<ResolvedPackage> ResolveCore(string projectDirectory, string targetFramework)
    {
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            Console.Error.WriteLine($"Warning: project.assets.json not found at {assetsPath}. Run 'dotnet restore' first.");
            return Array.Empty<ResolvedPackage>();
        }

        var json = File.ReadAllText(assetsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Get package folders
        var packageFolders = new List<string>();
        if (root.TryGetProperty("packageFolders", out var foldersEl))
        {
            foreach (var folder in foldersEl.EnumerateObject())
            {
                packageFolders.Add(folder.Name);
            }
        }

        if (packageFolders.Count == 0)
        {
            Console.Error.WriteLine("Warning: No packageFolders found in project.assets.json.");
            return Array.Empty<ResolvedPackage>();
        }

        // Find the matching target
        if (!root.TryGetProperty("targets", out var targetsEl))
            return Array.Empty<ResolvedPackage>();

        // Try exact match first, then prefix match
        JsonElement? targetEl = null;
        foreach (var target in targetsEl.EnumerateObject())
        {
            if (target.Name.Equals(targetFramework, StringComparison.OrdinalIgnoreCase)
                || target.Name.StartsWith(targetFramework, StringComparison.OrdinalIgnoreCase))
            {
                targetEl = target.Value;
                break;
            }
        }

        if (targetEl == null)
        {
            // Fall back to first target
            foreach (var target in targetsEl.EnumerateObject())
            {
                targetEl = target.Value;
                break;
            }
        }

        if (targetEl == null)
            return Array.Empty<ResolvedPackage>();

        var results = new List<ResolvedPackage>();

        foreach (var package in targetEl.Value.EnumerateObject())
        {
            // package.Name is like "Microsoft.CodeAnalysis.CSharp/5.3.0"
            var parts = package.Name.Split('/', 2);
            if (parts.Length != 2) continue;

            var packageId = parts[0];
            var version = parts[1];

            if (!package.Value.TryGetProperty("compile", out var compileEl))
                continue;

            foreach (var dll in compileEl.EnumerateObject())
            {
                var dllRelative = dll.Name;
                if (!dllRelative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Skip the placeholder "_._"
                if (dllRelative.EndsWith("_._", StringComparison.Ordinal))
                    continue;

                // Try each package folder to find the actual DLL
                string? resolvedPath = null;
                foreach (var folder in packageFolders)
                {
                    var candidate = Path.Combine(folder, packageId.ToLowerInvariant(),
                        version, dllRelative.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                    {
                        resolvedPath = candidate;
                        break;
                    }
                }

                if (resolvedPath != null)
                {
                    results.Add(new ResolvedPackage(packageId, version, resolvedPath));
                }
                else
                {
                    Console.Error.WriteLine(
                        $"Warning: DLL not found in package cache: {packageId}/{version} -> {dllRelative}");
                }
            }
        }

        return results;
    }
}
