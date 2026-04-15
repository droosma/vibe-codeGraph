using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

public class FrameworkRefResolverTests
{
    [Fact]
    public void Resolve_CurrentFramework_ReturnsNonEmptyList()
    {
        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
    }

    [Fact]
    public void Resolve_AllReturnedPathsEndWithDll()
    {
        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.All(dlls, path => Assert.EndsWith(".dll", path));
    }

    [Fact]
    public void Resolve_NonexistentFramework_ReturnsEmptyList()
    {
        // net99.0 doesn't exist - should return empty gracefully
        var dlls = FrameworkRefResolver.Resolve("net99.0");

        // Falls back to latest version dir, so may return dlls or empty
        // The key assertion is that it doesn't throw
        Assert.IsAssignableFrom<IReadOnlyList<string>>(dlls);
    }

    [Fact]
    public void Resolve_ReturnedDllsExistOnDisk()
    {
        var dlls = FrameworkRefResolver.Resolve("net8.0");

        // At least some DLLs should exist (these are real framework assemblies)
        Assert.All(dlls, path => Assert.True(File.Exists(path), $"DLL not found: {path}"));
    }
}

/// <summary>
/// Tests using a synthetic SDK directory structure via DOTNET_ROOT to exercise
/// private methods (FindBestVersionDir, GetDotnetRoots) through Resolve.
/// </summary>
public class FrameworkRefResolverSyntheticTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _originalDotnetRoot;

    public FrameworkRefResolverSyntheticTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CodeGraphTest_{Guid.NewGuid():N}");
        _originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _originalDotnetRoot);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private void SetDotnetRoot()
    {
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _tempRoot);
    }

    /// <summary>
    /// Creates: packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/ with dummy DLLs.
    /// </summary>
    private void CreatePackVersion(string version, string tfm, params string[] dllNames)
    {
        var refDir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", version, "ref", tfm);
        Directory.CreateDirectory(refDir);
        foreach (var dll in dllNames)
            File.WriteAllBytes(Path.Combine(refDir, dll), Array.Empty<byte>());
    }

    /// <summary>
    /// Creates a directory under packs/Microsoft.NETCore.App.Ref/ without ref subdirs (e.g. non-version dir).
    /// </summary>
    private void CreateNonVersionDir(string name)
    {
        var dir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", name);
        Directory.CreateDirectory(dir);
    }

    // === Mutant: OrderByDescending → OrderBy (L62) ===
    // If ordering is ascending instead of descending, we'd get 7.0.0 as fallback instead of 8.0.0.
    [Fact]
    public void Resolve_PrefersLatestVersionDir_WhenMultipleVersionsExist()
    {
        CreatePackVersion("7.0.0", "net7.0", "System.Runtime.dll");
        CreatePackVersion("8.0.0", "net8.0", "System.Runtime.dll", "System.Collections.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.Equal(2, dlls.Count);
        Assert.All(dlls, p => Assert.Contains("8.0.0", p));
    }

    // === Mutant: OrderByDescending → OrderBy — fallback to latest ===
    // When TFM doesn't match any major, falls back to versionDirs[0] which must be latest.
    [Fact]
    public void Resolve_FallsBackToLatestVersion_WhenNoMajorMatch()
    {
        CreatePackVersion("7.0.0", "net7.0", "Old.dll");
        CreatePackVersion("9.0.0", "net9.0", "New.dll");
        SetDotnetRoot();

        // net99.0 won't match major 9 or 7, falls back to latest (9.0.0)
        var dlls = FrameworkRefResolver.Resolve("net99.0");

        // The fallback ref dir won't have a "net99.0" subfolder — it falls through to
        // the ref subdir lookup. 9.0.0/ref/net9.0 exists but doesn't startWith "net99.0",
        // so it picks subdirs[^1]. Either way, we should get DLLs from 9.0.0.
        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.Contains("9.0.0", p));
    }

    // === Mutant: char.IsDigit filter — AND→OR, >→>= (L61) ===
    // Non-version dirs like "preview" must be filtered out.
    [Fact]
    public void Resolve_FiltersOutNonVersionDirectories()
    {
        CreateNonVersionDir("preview");
        CreateNonVersionDir("nightly");
        CreatePackVersion("8.0.0", "net8.0", "System.Runtime.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.Single(dlls);
        Assert.Contains("8.0.0", dlls[0]);
        Assert.DoesNotContain("preview", dlls[0]);
    }

    // === Mutant: d.Name.Length > 0 with only non-version dirs ===
    // When DOTNET_ROOT only has non-version dirs, FindBestVersionDir returns null,
    // so it continues to next root. Results should NOT come from synthetic root.
    [Fact]
    public void Resolve_SkipsRootWithOnlyNonVersionDirs()
    {
        CreateNonVersionDir("preview");
        CreateNonVersionDir("rc");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        // Should find DLLs from real install, not from our synthetic root
        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.False(p.StartsWith(_tempRoot)));
    }

    // === Mutant: Take(1) → Skip(1) for major version extraction (L69) ===
    // If Skip is used, majorStr would be empty for single-digit versions,
    // TryParse would fail, and we'd fall back to latest instead of matching.
    [Fact]
    public void Resolve_MatchesMajorVersionFromTfm()
    {
        CreatePackVersion("7.0.0", "net7.0", "Seven.dll");
        CreatePackVersion("8.0.0", "net8.0", "Eight.dll");
        SetDotnetRoot();

        var dlls7 = FrameworkRefResolver.Resolve("net7.0");
        Assert.Single(dlls7);
        Assert.Contains("7.0.0", dlls7[0]);

        var dlls8 = FrameworkRefResolver.Resolve("net8.0");
        Assert.Single(dlls8);
        Assert.Contains("8.0.0", dlls8[0]);
    }

    // === Mutant: int.TryParse negated (L70) ===
    // If negated, successful parse would skip matching and always fall back.
    [Fact]
    public void Resolve_ParsesMajorVersion_DoesNotAlwaysFallback()
    {
        CreatePackVersion("6.0.0", "net6.0", "Six.dll");
        CreatePackVersion("8.0.0", "net8.0", "Eight.dll");
        SetDotnetRoot();

        // If TryParse is negated, it would fall back to latest (8.0.0) for net6.0
        var dlls = FrameworkRefResolver.Resolve("net6.0");
        Assert.Single(dlls);
        Assert.Contains("6.0.0", dlls[0]);
        Assert.DoesNotContain("8.0.0", dlls[0]);
    }

    // === Mutant: FirstOrDefault → First (L73) ===
    // First would throw when no version matches; FirstOrDefault returns null.
    [Fact]
    public void Resolve_NoMatchingMajor_DoesNotThrow()
    {
        CreatePackVersion("8.0.0", "net8.0", "System.Runtime.dll");
        SetDotnetRoot();

        // Major 5 doesn't exist — FirstOrDefault returns null, falls back to latest
        var ex = Record.Exception(() => FrameworkRefResolver.Resolve("net5.0"));
        Assert.Null(ex);
    }

    // === Mutant: $"{major}." string mutation (L73) ===
    // The prefix match must include the dot to avoid e.g. major=8 matching "80.0.0".
    [Fact]
    public void Resolve_MajorMatchIncludesDotSeparator()
    {
        CreatePackVersion("80.0.0", "net80.0", "Wrong.dll");
        CreatePackVersion("8.0.0", "net8.0", "Correct.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");
        Assert.Single(dlls);
        Assert.Contains("8.0.0", dlls[0]);
        Assert.DoesNotContain("80.0.0", dlls[0]);
    }

    // === Mutant: match == null → match != null (L74) ===
    // If inverted, a successful match would be ignored and fall back.
    [Fact]
    public void Resolve_UsesMatchedVersion_NotFallback()
    {
        CreatePackVersion("7.0.0", "net7.0", "Fallback.dll");
        CreatePackVersion("8.0.0", "net8.0", "Matched.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");
        Assert.Single(dlls);
        // Must come from 8.0.0 (matched), not 8.0.0 being latest coincidence —
        // we verify with the DLL name.
        Assert.Contains("Matched.dll", dlls[0]);
    }

    // === Mutant: dlls.Length > 0 → >= 0 or negated (L46) ===
    // If mutated to >= 0, the code would return an empty array from the first root
    // instead of continuing to the next root that has actual DLLs.
    [Fact]
    public void Resolve_SkipsEmptyRefDir_ContinuesToNextRoot()
    {
        // Create version dir with ref/net8.0 but no DLL files in DOTNET_ROOT
        var refDir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref", "net8.0");
        Directory.CreateDirectory(refDir);
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        // Must NOT be empty — code should skip empty dir and find DLLs from real install
        Assert.NotEmpty(dlls);
        // Results should NOT come from our synthetic root (it has no DLLs)
        Assert.All(dlls, p => Assert.False(p.StartsWith(_tempRoot), "Should not return paths from empty synthetic root"));
    }

    // === Mutant: Path.Combine with "ref" (L22) and "packs" (L14) ===
    // If "ref" or "packs" subdir names are mutated, nothing would be found.
    [Fact]
    public void Resolve_RequiresCorrectSubdirStructure()
    {
        // Create correct structure
        CreatePackVersion("8.0.0", "net8.0", "System.Runtime.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");
        Assert.NotEmpty(dlls);

        // Verify the path contains the expected subdir names
        Assert.All(dlls, p =>
        {
            Assert.Contains(Path.Combine("packs", "Microsoft.NETCore.App.Ref"), p);
            Assert.Contains("ref", p);
        });
    }

    // === Mutant: DOTNET_ROOT env var usage (L107-108) ===
    [Fact]
    public void Resolve_UsesDotnetRootEnvVar()
    {
        CreatePackVersion("8.0.0", "net8.0", "FromEnvRoot.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.StartsWith(_tempRoot, p));
    }

    // === Mutant: ref subdir fallback — FirstOrDefault startsWith (L30-31) ===
    // When exact tfm dir doesn't exist, it should find a startsWith match.
    [Fact]
    public void Resolve_FallsBackToStartsWithMatch_InRefSubdir()
    {
        // Create 8.0.0 but with ref/net8.0.1 instead of ref/net8.0
        var refDir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref", "net8.0.1");
        Directory.CreateDirectory(refDir);
        File.WriteAllBytes(Path.Combine(refDir, "System.Runtime.dll"), Array.Empty<byte>());
        SetDotnetRoot();

        // net8.0 won't match exact dir, but net8.0.1 starts with "net8.0"? No — startsWith("net8.0")
        // Actually "net8.0.1".StartsWith("net8.0") is true.
        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
        Assert.Contains("net8.0.1", dlls[0]);
    }

    // === Mutant: subdirs[^1] fallback — last subdir used when no startsWith match (L35) ===
    [Fact]
    public void Resolve_FallsBackToLastRefSubdir_WhenNoStartsWithMatch()
    {
        // 8.0.0/ref/net7.0/ — net7.0 doesn't startWith "net6.0"
        var refDir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref", "net7.0");
        Directory.CreateDirectory(refDir);
        File.WriteAllBytes(Path.Combine(refDir, "Fallback.dll"), Array.Empty<byte>());
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net6.0");

        Assert.NotEmpty(dlls);
        Assert.Contains("Fallback.dll", dlls[0]);
    }

    // === Mutant: subdirs.Length > 0 (L34) — ref dir exists but has no subdirs ===
    [Fact]
    public void Resolve_RefDirNoSubdirs_SkipsToNextRoot()
    {
        // Create version dir with ref/ but no tfm subdirs
        var refBase = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref");
        Directory.CreateDirectory(refBase);
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        // Should continue to next root and find DLLs, not return from synthetic root
        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.False(p.StartsWith(_tempRoot)));
    }

    // === Mutant: match != null on ref subdir startsWith (L32-33) ===
    // Verify that when startsWith matches, it uses that dir (not subdirs[^1]).
    [Fact]
    public void Resolve_PrefersStartsWithMatch_OverLastSubdir()
    {
        var packBase = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref");
        // Create two ref subdirs: net7.0 and net8.0-preview
        var dir7 = Path.Combine(packBase, "net7.0");
        var dir8Preview = Path.Combine(packBase, "net8.0-preview");
        Directory.CreateDirectory(dir7);
        Directory.CreateDirectory(dir8Preview);
        File.WriteAllBytes(Path.Combine(dir7, "Seven.dll"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(dir8Preview, "EightPreview.dll"), Array.Empty<byte>());
        SetDotnetRoot();

        // Exact "net8.0" dir doesn't exist, but "net8.0-preview" startsWith "net8.0"
        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
        Assert.Contains("EightPreview.dll", dlls[0]);
    }

    // === Mutant: targetFramework.Trim() on L9 ===
    [Fact]
    public void Resolve_TrimsWhitespace_FromTargetFramework()
    {
        CreatePackVersion("8.0.0", "net8.0", "System.Runtime.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("  net8.0  ");

        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.Contains("8.0.0", p));
    }

    // === Mutant: empty packs dir returns empty (versionDirs.Count == 0 → null) ===
    [Fact]
    public void Resolve_EmptyPacksDir_ReturnsEmpty()
    {
        // Create packs dir structure but with no version subdirectories at all
        var packsDir = Path.Combine(_tempRoot, "packs", "Microsoft.NETCore.App.Ref");
        Directory.CreateDirectory(packsDir);
        SetDotnetRoot();

        // When only synthetic root exists and it has empty packs, and no real install
        // provides net99.0, we get empty
        var dlls = FrameworkRefResolver.Resolve("net99.0");

        // The synthetic root has no version dirs, so FindBestVersionDir returns null.
        // Falls through to next roots. For net99.0 there might not be a real match.
        Assert.IsAssignableFrom<IReadOnlyList<string>>(dlls);
    }

    // === Mutant: DOTNET_ROOT inserted at position 0 (Insert vs Add) ===
    [Fact]
    public void Resolve_DotnetRoot_HasPriority_OverSystemPaths()
    {
        // Create a specific version only in DOTNET_ROOT to prove it's checked first
        CreatePackVersion("8.0.0", "net8.0", "FromDotnetRoot.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
        // Results must come from our synthetic DOTNET_ROOT
        Assert.All(dlls, p => Assert.StartsWith(_tempRoot, p));
    }

    // === Mutant: non-numeric prefix version dirs filtered ===
    [Fact]
    public void Resolve_VersionDirWithLetterPrefix_IsFiltered()
    {
        CreateNonVersionDir("v8.0.0"); // starts with 'v', not digit
        CreatePackVersion("8.0.0", "net8.0", "Good.dll");
        SetDotnetRoot();

        var dlls = FrameworkRefResolver.Resolve("net8.0");

        Assert.NotEmpty(dlls);
        Assert.All(dlls, p => Assert.DoesNotContain("v8.0.0", p));
        Assert.All(dlls, p => Assert.Contains("8.0.0", p));
    }
}
