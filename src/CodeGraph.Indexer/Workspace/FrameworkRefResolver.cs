using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace CodeGraph.Indexer.Workspace;

public static class FrameworkRefResolver
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> s_cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Resolve(string targetFramework)
    {
        var tfm = targetFramework.Trim();
        return s_cache.GetOrAdd(tfm, static framework => ResolveCore(framework));
    }

    internal static void ClearCache() => s_cache.Clear();

    private static IReadOnlyList<string> ResolveCore(string tfm)
    {
        foreach (var dotnetRoot in GetDotnetRoots())
        {
            var resolvedDlls = TryResolveFromRoot(dotnetRoot, tfm);
            if (resolvedDlls.Count > 0)
                return resolvedDlls;
        }

        Console.Error.WriteLine($"Warning: Could not find framework reference assemblies for {tfm}.");
        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> TryResolveFromRoot(string dotnetRoot, string tfm)
    {
        var packsDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packsDir))
            return Array.Empty<string>();

        var versionDir = FindBestVersionDir(packsDir, tfm);
        if (versionDir is null)
            return Array.Empty<string>();

        var referenceDir = FindReferenceDirectory(versionDir, tfm);
        if (referenceDir is null)
            return Array.Empty<string>();

        var dlls = Directory.GetFiles(referenceDir, "*.dll");
        return dlls.Length > 0 ? dlls : Array.Empty<string>();
    }

    private static string? FindReferenceDirectory(string versionDir, string tfm)
    {
        var exactReferenceDir = Path.Combine(versionDir, "ref", tfm);
        if (Directory.Exists(exactReferenceDir))
            return exactReferenceDir;

        var referenceBaseDir = Path.Combine(versionDir, "ref");
        if (!Directory.Exists(referenceBaseDir))
            return null;

        var subdirectories = Directory.GetDirectories(referenceBaseDir);
        if (subdirectories.Length == 0)
            return null;

        return subdirectories.FirstOrDefault(directory =>
                   Path.GetFileName(directory).StartsWith(tfm, StringComparison.OrdinalIgnoreCase))
               ?? subdirectories[^1];
    }

    private static string? FindBestVersionDir(string packsDir, string tfm)
    {
        if (!Directory.Exists(packsDir))
            return null;

        var versionDirs = Directory.GetDirectories(packsDir)
            .Select(directory => new { Path = directory, Name = Path.GetFileName(directory) })
            .Where(static directory => directory.Name.Length > 0 && char.IsDigit(directory.Name[0]))
            .OrderByDescending(static directory => directory.Name)
            .ToList();

        if (versionDirs.Count == 0)
            return null;

        if (TryGetTargetMajorVersion(tfm, out var majorVersion))
        {
            var matchingVersion = versionDirs.FirstOrDefault(directory =>
                directory.Name.StartsWith($"{majorVersion}.", StringComparison.Ordinal));
            if (matchingVersion is not null)
                return matchingVersion.Path;
        }

        return versionDirs[0].Path;
    }

    private static bool TryGetTargetMajorVersion(string tfm, out int majorVersion)
    {
        var majorDigits = new string(tfm.Where(char.IsDigit).Take(1).ToArray());
        return int.TryParse(majorDigits, out majorVersion);
    }

    private static List<string> GetDotnetRoots()
    {
        var roots = new List<string>();
        AddDotnetRootFromEnvironment(roots);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AddWindowsDotnetRoots(roots);
        }
        else
        {
            AddUnixDotnetRoots(roots);
        }

        return roots
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddDotnetRootFromEnvironment(List<string> roots)
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            roots.Add(dotnetRoot);
    }

    private static void AddWindowsDotnetRoots(List<string> roots)
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        roots.Add(Path.Combine(programFiles, "dotnet"));

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        roots.Add(Path.Combine(programFilesX86, "dotnet"));
    }

    private static void AddUnixDotnetRoots(List<string> roots)
    {
        roots.Add("/usr/share/dotnet");
        roots.Add("/usr/local/share/dotnet");

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
            roots.Add(Path.Combine(home, ".dotnet"));
    }
}
