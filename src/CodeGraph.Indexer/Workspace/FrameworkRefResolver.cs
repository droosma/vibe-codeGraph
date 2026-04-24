using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace CodeGraph.Indexer.Workspace;

public static class FrameworkRefResolver
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> s_cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Resolve(string targetFramework)
    {
        var tfm = targetFramework.Trim();
        return s_cache.GetOrAdd(tfm, static key => ResolveCore(key));
    }

    internal static void ClearCache() => s_cache.Clear();

    private static IReadOnlyList<string> ResolveCore(string tfm)
    {
        var dotnetRoots = GetDotnetRoots();

        foreach (var root in dotnetRoots)
        {
            var packsDir = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packsDir))
                continue;

            // Find best matching version directory
            var versionDir = FindBestVersionDir(packsDir, tfm);
            if (versionDir == null) continue;

            var refDir = Path.Combine(versionDir, "ref", tfm);
            if (!Directory.Exists(refDir))
            {
                // Try without exact tfm match - look for any ref subfolder
                var refBase = Path.Combine(versionDir, "ref");
                if (Directory.Exists(refBase))
                {
                    var subdirs = Directory.GetDirectories(refBase);
                    var match = subdirs.FirstOrDefault(d =>
                        Path.GetFileName(d).StartsWith(tfm, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        refDir = match;
                    else if (subdirs.Length > 0)
                        refDir = subdirs[^1]; // last (highest) version
                    else
                        continue;
                }
                else
                {
                    continue;
                }
            }

            var dlls = Directory.GetFiles(refDir, "*.dll");
            if (dlls.Length > 0)
                return dlls;
        }

        Console.Error.WriteLine($"Warning: Could not find framework reference assemblies for {tfm}.");
        return Array.Empty<string>();
    }

    private static string? FindBestVersionDir(string packsDir, string tfm)
    {
        if (!Directory.Exists(packsDir))
            return null;

        var versionDirs = Directory.GetDirectories(packsDir)
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .Where(d => d.Name.Length > 0 && char.IsDigit(d.Name[0]))
            .OrderByDescending(d => d.Name)
            .ToList();

        if (versionDirs.Count == 0)
            return null;

        // Extract major version from TFM (e.g., "net8.0" -> 8)
        var majorStr = new string(tfm.Where(char.IsDigit).Take(1).ToArray());
        if (int.TryParse(majorStr, out var major))
        {
            // Find version that starts with the matching major
            var match = versionDirs.FirstOrDefault(d => d.Name.StartsWith($"{major}."));
            if (match != null)
                return match.Path;
        }

        // Fall back to latest
        return versionDirs[0].Path;
    }

    private static List<string> GetDotnetRoots()
    {
        var roots = new List<string>();

        // Try to find dotnet root from the running process
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Common Windows paths
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            roots.Add(Path.Combine(programFiles, "dotnet"));

            var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
            roots.Add(Path.Combine(programFilesX86, "dotnet"));
        }
        else
        {
            // Linux/macOS paths
            roots.Add("/usr/share/dotnet");
            roots.Add("/usr/local/share/dotnet");
            var home = Environment.GetEnvironmentVariable("HOME");
            if (home != null)
                roots.Add(Path.Combine(home, ".dotnet"));
        }

        // DOTNET_ROOT environment variable
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            roots.Insert(0, dotnetRoot);

        return roots.Where(Directory.Exists).Distinct().ToList();
    }
}
