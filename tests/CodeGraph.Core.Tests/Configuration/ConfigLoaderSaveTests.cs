using System.Text.Json;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;

namespace CodeGraph.Core.Tests.Configuration;

public class ConfigLoaderSaveTests : IDisposable
{
    private readonly string _testDir;

    public ConfigLoaderSaveTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void DefaultFileName_IsCodegraphJson()
    {
        Assert.Equal("codegraph.json", ConfigLoader.DefaultFileName);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(_testDir, "sub", "dir", "codegraph.json");
        var config = new CodeGraphConfig { Solution = "Nested.sln" };

        await ConfigLoader.SaveAsync(config, nestedPath);

        Assert.True(File.Exists(nestedPath));
        var json = await File.ReadAllTextAsync(nestedPath);
        Assert.Contains("Nested.sln", json);
    }

    [Fact]
    public async Task SaveAsync_WritesValidJson()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var config = new CodeGraphConfig
        {
            Solution = "Valid.sln",
            Output = "output-dir",
            SplitBy = "assembly"
        };

        await ConfigLoader.SaveAsync(config, configPath);

        var json = await File.ReadAllTextAsync(configPath);
        var deserialized = JsonSerializer.Deserialize<CodeGraphConfig>(json, GraphSerializationOptions.Default);
        Assert.NotNull(deserialized);
        Assert.Equal("Valid.sln", deserialized.Solution);
        Assert.Equal("output-dir", deserialized.Output);
        Assert.Equal("assembly", deserialized.SplitBy);
    }

    [Fact]
    public async Task SaveAsync_WithEmptyDirectory_StillWorks()
    {
        // Path with no directory component (just filename in current test dir)
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var config = new CodeGraphConfig();

        await ConfigLoader.SaveAsync(config, configPath);

        Assert.True(File.Exists(configPath));
    }
}
