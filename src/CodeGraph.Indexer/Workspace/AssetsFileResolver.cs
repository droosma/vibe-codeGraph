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
        return s_cache.GetOrAdd(key, static item => ResolveCore(item.Directory, item.Framework));
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

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;
        var packageFolders = GetPackageFolders(root);

        if (packageFolders.Count == 0)
        {
            Console.Error.WriteLine("Warning: No packageFolders found in project.assets.json.");
            return Array.Empty<ResolvedPackage>();
        }

        if (!root.TryGetProperty("targets", out var targetsElement))
            return Array.Empty<ResolvedPackage>();

        var targetElement = FindTarget(targetsElement, targetFramework);
        return targetElement is null
            ? Array.Empty<ResolvedPackage>()
            : ResolvePackages(targetElement.Value, packageFolders);
    }

    private static List<string> GetPackageFolders(JsonElement root)
    {
        if (!root.TryGetProperty("packageFolders", out var foldersElement))
            return new List<string>();

        return foldersElement.EnumerateObject()
            .Select(folder => folder.Name)
            .ToList();
    }

    private static JsonElement? FindTarget(JsonElement targetsElement, string targetFramework)
    {
        foreach (var target in targetsElement.EnumerateObject())
        {
            if (target.Name.Equals(targetFramework, StringComparison.OrdinalIgnoreCase)
                || target.Name.StartsWith(targetFramework, StringComparison.OrdinalIgnoreCase))
            {
                return target.Value;
            }
        }

        foreach (var target in targetsElement.EnumerateObject())
            return target.Value;

        return null;
    }

    private static IReadOnlyList<ResolvedPackage> ResolvePackages(
        JsonElement targetElement,
        IReadOnlyList<string> packageFolders)
    {
        var results = new List<ResolvedPackage>();

        foreach (var package in targetElement.EnumerateObject())
        {
            if (!TryParsePackageName(package.Name, out var packageId, out var version))
                continue;

            if (!package.Value.TryGetProperty("compile", out var compileElement))
                continue;

            foreach (var compileEntry in compileElement.EnumerateObject())
            {
                var dllRelativePath = compileEntry.Name;
                if (!IsCompileDll(dllRelativePath))
                    continue;

                var resolvedPath = TryResolveDllPath(packageFolders, packageId, version, dllRelativePath);
                if (resolvedPath is null)
                {
                    Console.Error.WriteLine(
                        $"Warning: DLL not found in package cache: {packageId}/{version} -> {dllRelativePath}");
                    continue;
                }

                results.Add(new ResolvedPackage(packageId, version, resolvedPath));
            }
        }

        return results;
    }

    private static bool TryParsePackageName(string packageName, out string packageId, out string version)
    {
        var parts = packageName.Split('/', 2);
        if (parts.Length == 2)
        {
            packageId = parts[0];
            version = parts[1];
            return true;
        }

        packageId = string.Empty;
        version = string.Empty;
        return false;
    }

    private static bool IsCompileDll(string relativePath)
    {
        return relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith("_._", StringComparison.Ordinal);
    }

    private static string? TryResolveDllPath(
        IReadOnlyList<string> packageFolders,
        string packageId,
        string version,
        string dllRelativePath)
    {
        foreach (var packageFolder in packageFolders)
        {
            var candidate = Path.Combine(
                packageFolder,
                packageId.ToLowerInvariant(),
                version,
                dllRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
