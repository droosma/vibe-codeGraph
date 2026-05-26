using System.Text.Json;
using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

[Collection("FrameworkRefResolver")]
public sealed class ResolverCacheMutationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly string? _originalDotnetRoot;

    public ResolverCacheMutationTests()
    {
        _originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        AssetsFileResolver.ClearCache();
        FrameworkRefResolver.ClearCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _originalDotnetRoot);
        AssetsFileResolver.ClearCache();
        FrameworkRefResolver.ClearCache();

        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AssetsFileResolver_UsesCachedResult_UntilCacheCleared()
    {
        var packageRoot = CreateTempDir("package-root");
        var dllV1 = CreatePackageDll(packageRoot, "cachedpkg", "1.0.0", "CachedPkg.dll");
        var projectDir = CreateProjectDir(CreateAssetsJson(packageRoot, "CachedPkg", "1.0.0", "lib/net8.0/CachedPkg.dll"));

        var first = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));
        Assert.Equal(dllV1, first.DllPath);

        var dllV2 = CreatePackageDll(packageRoot, "cachedpkg", "2.0.0", "CachedPkg.dll");
        File.WriteAllText(Path.Combine(projectDir, "obj", "project.assets.json"), CreateAssetsJson(packageRoot, "CachedPkg", "2.0.0", "lib/net8.0/CachedPkg.dll"));

        var cached = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));
        Assert.Equal(dllV1, cached.DllPath);

        AssetsFileResolver.ClearCache();
        var refreshed = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));
        Assert.Equal(dllV2, refreshed.DllPath);
    }

    [Fact]
    public void FrameworkRefResolver_UsesCachedResult_UntilCacheCleared()
    {
        var rootOne = CreateTempDir("dotnet-root-one");
        CreateFrameworkPack(rootOne, "8.0.0", "net8.0", "First.dll");
        Environment.SetEnvironmentVariable("DOTNET_ROOT", rootOne);

        var first = Assert.Single(FrameworkRefResolver.Resolve("net8.0"));
        Assert.Contains("First.dll", first);

        var rootTwo = CreateTempDir("dotnet-root-two");
        CreateFrameworkPack(rootTwo, "8.0.0", "net8.0", "Second.dll");
        Environment.SetEnvironmentVariable("DOTNET_ROOT", rootTwo);

        var cached = Assert.Single(FrameworkRefResolver.Resolve("net8.0"));
        Assert.Equal(first, cached);

        FrameworkRefResolver.ClearCache();
        var refreshed = Assert.Single(FrameworkRefResolver.Resolve("net8.0"));
        Assert.Contains("Second.dll", refreshed);
    }

    private string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string CreateAssetsJson(string packageRoot, string packageId, string version, string relativeDllPath)
    {
        var packageFolder = packageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? packageRoot
            : packageRoot + Path.DirectorySeparatorChar;
        var encodedPackageFolder = JsonSerializer.Serialize(packageFolder).Trim('"');
        return $$"""
            {
              "packageFolders": { "{{encodedPackageFolder}}": {} },
              "targets": {
                "net8.0": {
                  "{{packageId}}/{{version}}": {
                    "compile": { "{{relativeDllPath}}": {} }
                  }
                }
              }
            }
            """;
    }

    private string CreateProjectDir(string assetsJson)
    {
        var dir = CreateTempDir("assets-project");
        var objDir = Path.Combine(dir, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"), assetsJson);
        return dir;
    }

    private static string CreatePackageDll(string root, string packageId, string version, string fileName)
    {
        var dllDir = Path.Combine(root, packageId, version, "lib", "net8.0");
        Directory.CreateDirectory(dllDir);
        var dllPath = Path.Combine(dllDir, fileName);
        File.WriteAllBytes(dllPath, Array.Empty<byte>());
        return dllPath;
    }

    private static void CreateFrameworkPack(string root, string version, string tfm, string dllName)
    {
        var refDir = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref", version, "ref", tfm);
        Directory.CreateDirectory(refDir);
        File.WriteAllBytes(Path.Combine(refDir, dllName), Array.Empty<byte>());
    }
}
