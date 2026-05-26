using System.Text.Json;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;

namespace CodeGraph.Core.Tests.Configuration;

public class MultiSolutionConfigTests : IDisposable
{
    private readonly string _testDir;

    public MultiSolutionConfigTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Default_Solutions_IsEmpty()
    {
        var config = new CodeGraphConfig();
        Assert.Empty(config.Solutions);
    }

    [Fact]
    public void GetEffectiveSolutions_WithSolutionsArray_ReturnsSolutions()
    {
        var config = new CodeGraphConfig
        {
            Solutions = new[]
            {
                new SolutionEntry { Path = "backend.sln" },
                new SolutionEntry { Path = "frontend.sln" }
            }
        };

        var effective = config.GetEffectiveSolutions();
        Assert.Equal(2, effective.Length);
        Assert.Equal("backend.sln", effective[0].Path);
        Assert.Equal("frontend.sln", effective[1].Path);
    }

    [Fact]
    public void GetEffectiveSolutions_WithSingleSolution_WrapsInArray()
    {
        var config = new CodeGraphConfig { Solution = "MyApp.sln" };

        var effective = config.GetEffectiveSolutions();
        Assert.Single(effective);
        Assert.Equal("MyApp.sln", effective[0].Path);
    }

    [Fact]
    public void GetEffectiveSolutions_WithBothEmpty_ReturnsEmpty()
    {
        var config = new CodeGraphConfig();

        var effective = config.GetEffectiveSolutions();
        Assert.Empty(effective);
    }

    [Fact]
    public void GetEffectiveSolutions_SolutionsArrayTakesPrecedence()
    {
        var config = new CodeGraphConfig
        {
            Solution = "old.sln",
            Solutions = new[] { new SolutionEntry { Path = "new.sln" } }
        };

        // Note: ConfigLoader would reject this, but GetEffectiveSolutions prefers Solutions
        var effective = config.GetEffectiveSolutions();
        Assert.Single(effective);
        Assert.Equal("new.sln", effective[0].Path);
    }

    [Fact]
    public void SolutionEntry_HasOptionalOverrides()
    {
        var entry = new SolutionEntry
        {
            Path = "backend.sln",
            Ioc = new IocConfig { RegistrationMethodPatterns = new[] { "RegisterType*" } },
            Index = new IndexConfig { Configuration = "Release" }
        };

        Assert.Equal("backend.sln", entry.Path);
        Assert.NotNull(entry.Ioc);
        Assert.Equal(new[] { "RegisterType*" }, entry.Ioc.RegistrationMethodPatterns);
        Assert.NotNull(entry.Index);
        Assert.Equal("Release", entry.Index.Configuration);
        Assert.Null(entry.Tests);
        Assert.Null(entry.Docs);
    }

    [Fact]
    public void Load_SolutionsArray_DeserializesCorrectly()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": "src/backend/backend.sln" },
                {
                    "path": "src/client/wpf/wpfclient.sln",
                    "ioc": {
                        "registrationMethodPatterns": ["RegisterType*", "Add*"]
                    }
                }
            ],
            "output": ".codegraph",
            "query": { "defaultDepth": 2 }
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);

        Assert.Null(config.Solution);
        Assert.Equal(2, config.Solutions.Length);
        Assert.Equal("src/backend/backend.sln", config.Solutions[0].Path);
        Assert.Equal("src/client/wpf/wpfclient.sln", config.Solutions[1].Path);
        Assert.Null(config.Solutions[0].Ioc);
        Assert.NotNull(config.Solutions[1].Ioc);
        Assert.Equal(new[] { "RegisterType*", "Add*" }, config.Solutions[1].Ioc!.RegistrationMethodPatterns);
        Assert.Equal(2, config.Query.DefaultDepth);
    }

    [Fact]
    public void Load_BothSolutionAndSolutions_ThrowsMigrationError()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solution": "old.sln",
            "solutions": [
                { "path": "new.sln" }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        var expected =
            $"Configuration file '{configPath}' contains both 'solution' (singular) and 'solutions' (array). " +
            "Please migrate to the new format by moving the single solution into the 'solutions' array. " +
            "Example: {{ \"solutions\": [{{ \"path\": \"MyApp.sln\" }}] }}";

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void Load_DuplicateSolutionNames_ThrowsError()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": "src/a/MyApp.sln" },
                { "path": "src/b/MyApp.sln" }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        var expected =
            $"Configuration file '{configPath}' contains solutions that resolve to the same name 'MyApp'. " +
            "Use --solution to specify a single solution, or manually edit codegraph.json to remove duplicates like " +
            "'src/a/MyApp.sln', 'src/b/MyApp.sln'.";

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task SaveAndReload_SolutionsArray_RoundTrips()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var config = new CodeGraphConfig
        {
            Solutions = new[]
            {
                new SolutionEntry { Path = "backend.sln" },
                new SolutionEntry
                {
                    Path = "frontend.sln",
                    Ioc = new IocConfig { RegistrationMethodPatterns = new[] { "Register*" } }
                }
            },
            Output = ".codegraph"
        };

        await ConfigLoader.SaveAsync(config, configPath);
        var reloaded = ConfigLoader.Load(configPath);

        Assert.Equal(2, reloaded.Solutions.Length);
        Assert.Equal("backend.sln", reloaded.Solutions[0].Path);
        Assert.Equal("frontend.sln", reloaded.Solutions[1].Path);
        Assert.NotNull(reloaded.Solutions[1].Ioc);
        Assert.Equal(new[] { "Register*" }, reloaded.Solutions[1].Ioc!.RegistrationMethodPatterns);
    }

    [Fact]
    public void Load_SingleSolution_StillWorks()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solution": "Legacy.sln"
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);
        Assert.Equal("Legacy.sln", config.Solution);
        Assert.Empty(config.Solutions);

        var effective = config.GetEffectiveSolutions();
        Assert.Single(effective);
        Assert.Equal("Legacy.sln", effective[0].Path);
    }

    [Fact]
    public void Load_SingleSolutionInArray_NoDuplicateCheck()
    {
        // With exactly 1 entry, the duplicate check (Solutions.Length > 1) should NOT trigger.
        // A null path would throw inside duplicate-name validation, so successful load proves
        // the boundary condition stays at > 1 rather than >= 1.
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": null }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);
        Assert.Single(config.Solutions);
        Assert.Null(config.Solutions[0].Path);
    }

    [Fact]
    public void Load_DuplicateNamesIgnoringCase_ThrowsError()
    {
        // OrdinalIgnoreCase means "Backend" and "backend" are duplicate
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": "src/Backend.sln" },
                { "path": "other/backend.sln" }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("resolve to the same name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ThreeUniqueSolutions_Succeeds()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": "A.sln" },
                { "path": "B.sln" },
                { "path": "C.sln" }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);
        Assert.Equal(3, config.Solutions.Length);
    }

    [Fact]
    public void Load_EmptySolution_WithSolutions_DoesNotThrow()
    {
        // Solution is null (not set), Solutions has entries → no conflict
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solutions": [
                { "path": "app.sln" }
            ]
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);
        Assert.Null(config.Solution);
        Assert.Single(config.Solutions);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNeeded()
    {
        var nestedDir = Path.Combine(_testDir, "nested", "dir");
        var configPath = Path.Combine(nestedDir, "codegraph.json");

        await ConfigLoader.SaveAsync(new CodeGraphConfig { Solution = "test.sln" }, configPath);

        Assert.True(File.Exists(configPath));
        var reloaded = ConfigLoader.Load(configPath);
        Assert.Equal("test.sln", reloaded.Solution);
    }
}
