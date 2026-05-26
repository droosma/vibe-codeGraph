using System.Text.Json;
using CodeGraph.Core.Configuration;
using CodeGraph.Core.IO;

namespace CodeGraph.Core.Tests.Configuration;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _testDir;

    public ConfigLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "codegraph-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void LoadDefaults_WhenNoConfigFileExists_ReturnsDefaults()
    {
        // Use a directory with no config file
        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_testDir);
        try
        {
            var config = ConfigLoader.Load();

            Assert.Null(config.Solution);
            Assert.Equal(".codegraph", config.Output);
            Assert.Equal("project", config.SplitBy);
            Assert.Equal(new[] { "*" }, config.Index.IncludeProjects);
            Assert.Empty(config.Index.ExcludeProjects);
            Assert.True(config.Ioc.Enabled);
            Assert.True(config.Tests.Enabled);
            Assert.True(config.Docs.Enabled);
            Assert.Equal(1, config.Query.DefaultDepth);
            Assert.Equal("context", config.Query.DefaultFormat);
            Assert.Equal(50, config.Query.MaxNodes);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void LoadFromFile_ReadsSpecifiedPath()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solution": "Test.sln",
            "output": "out",
            "splitBy": "namespace",
            "index": {
                "includeProjects": ["Proj1", "Proj2"],
                "maxDepthForExternals": 3
            },
            "query": {
                "defaultDepth": 5,
                "maxNodes": 100
            }
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);

        Assert.Equal("Test.sln", config.Solution);
        Assert.Equal("out", config.Output);
        Assert.Equal("namespace", config.SplitBy);
        Assert.Equal(new[] { "Proj1", "Proj2" }, config.Index.IncludeProjects);
        Assert.Equal(3, config.Index.MaxDepthForExternals);
        Assert.Equal(5, config.Query.DefaultDepth);
        Assert.Equal(100, config.Query.MaxNodes);
    }

    [Fact]
    public void LoadPartialConfig_FillsInDefaults()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solution": "Partial.sln"
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);

        Assert.Equal("Partial.sln", config.Solution);
        Assert.Equal(".codegraph", config.Output);
        Assert.Equal("project", config.SplitBy);
        Assert.Equal(new[] { "*" }, config.Index.IncludeProjects);
        Assert.True(config.Ioc.Enabled);
        Assert.Equal(1, config.Query.DefaultDepth);
    }

    [Fact]
    public void LoadInvalidJson_ThrowsMeaningfulError()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        File.WriteAllText(configPath, "{ invalid json }}}");

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("Failed to parse configuration file", ex.Message);
        Assert.Contains(configPath, ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsCorrectly()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var config = new CodeGraphConfig
        {
            Solution = "RoundTrip.sln",
            Output = "custom-output",
            SplitBy = "namespace"
        };
        config.Index.MaxDepthForExternals = 5;
        config.Query.MaxNodes = 200;

        await ConfigLoader.SaveAsync(config, configPath);
        var reloaded = ConfigLoader.Load(configPath);

        Assert.Equal("RoundTrip.sln", reloaded.Solution);
        Assert.Equal("custom-output", reloaded.Output);
        Assert.Equal("namespace", reloaded.SplitBy);
        Assert.Equal(5, reloaded.Index.MaxDepthForExternals);
        Assert.Equal(200, reloaded.Query.MaxNodes);
    }

    [Fact]
    public void SearchParentDirectories_FindsConfigInParent()
    {
        var parentDir = Path.Combine(_testDir, "parent");
        var childDir = Path.Combine(parentDir, "child");
        Directory.CreateDirectory(childDir);

        var configPath = Path.Combine(parentDir, "codegraph.json");
        File.WriteAllText(configPath, """
        {
            "solution": "Parent.sln"
        }
        """);

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(childDir);
        try
        {
            var config = ConfigLoader.Load();
            Assert.Equal("Parent.sln", config.Solution);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void Load_WithNonExistentPath_ThrowsFileNotFound()
    {
        var bogusPath = Path.Combine(_testDir, "does-not-exist.json");

        var ex = Assert.Throws<FileNotFoundException>(() => ConfigLoader.Load(bogusPath));
        Assert.Contains("Configuration file not found", ex.Message);
    }

    [Fact]
    public void Load_EmptyConfigPath_ThrowsFileNotFound()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => ConfigLoader.Load(string.Empty));
        Assert.Contains("Configuration file not found", ex.Message);
    }

    [Fact]
    public void LoadFromFile_AllSectionsPresent_DeserializesEveryConfiguredField()
    {
        var configPath = Path.Combine(_testDir, "codegraph.json");
        var json = """
        {
            "solution": "AllValues.sln",
            "output": "graph-out",
            "splitBy": "assembly",
            "index": {
                "includeProjects": ["App*"],
                "excludeProjects": ["App.Tests"],
                "includeExternalPackages": ["Newtonsoft.*"],
                "excludeExternalPackages": ["System.*", "Microsoft.*"],
                "maxDepthForExternals": 3,
                "configuration": "Release",
                "preprocessorSymbols": ["TRACE", "CUSTOM"]
            },
            "ioc": {
                "enabled": false,
                "entryPoints": ["Program.Main"],
                "additionalEntryPoints": ["Startup.ConfigureServices"],
                "registrationMethodPatterns": ["Register*"],
                "ignoreMethodPatterns": ["Ignore*"] ,
                "inferSingleImplementations": false,
                "scanAssemblyRegistrations": false,
                "followExtensionMethodDepth": 2
            },
            "tests": {
                "enabled": false,
                "testAttributePatterns": ["*Spec"],
                "setupAttributePatterns": ["*Init"],
                "includeSetupMethods": false
            },
            "docs": {
                "enabled": false,
                "markdownDirs": ["guides/", "reference/"],
                "includeXmlDocs": false
            },
            "query": {
                "defaultDepth": 4,
                "defaultFormat": "json",
                "defaultMode": "callers",
                "maxNodes": 99
            }
        }
        """;
        File.WriteAllText(configPath, json);

        var config = ConfigLoader.Load(configPath);

        Assert.Equal("AllValues.sln", config.Solution);
        Assert.Equal("graph-out", config.Output);
        Assert.Equal("assembly", config.SplitBy);

        Assert.Equal(new[] { "App*" }, config.Index.IncludeProjects);
        Assert.Equal(new[] { "App.Tests" }, config.Index.ExcludeProjects);
        Assert.Equal(new[] { "Newtonsoft.*" }, config.Index.IncludeExternalPackages);
        Assert.Equal(new[] { "System.*", "Microsoft.*" }, config.Index.ExcludeExternalPackages);
        Assert.Equal(3, config.Index.MaxDepthForExternals);
        Assert.Equal("Release", config.Index.Configuration);
        Assert.Equal(new[] { "TRACE", "CUSTOM" }, config.Index.PreprocessorSymbols);

        Assert.False(config.Ioc.Enabled);
        Assert.Equal(new[] { "Program.Main" }, config.Ioc.EntryPoints);
        Assert.Equal(new[] { "Startup.ConfigureServices" }, config.Ioc.AdditionalEntryPoints);
        Assert.Equal(new[] { "Register*" }, config.Ioc.RegistrationMethodPatterns);
        Assert.Equal(new[] { "Ignore*" }, config.Ioc.IgnoreMethodPatterns);
        Assert.False(config.Ioc.InferSingleImplementations);
        Assert.False(config.Ioc.ScanAssemblyRegistrations);
        Assert.Equal(2, config.Ioc.FollowExtensionMethodDepth);

        Assert.False(config.Tests.Enabled);
        Assert.Equal(new[] { "*Spec" }, config.Tests.TestAttributePatterns);
        Assert.Equal(new[] { "*Init" }, config.Tests.SetupAttributePatterns);
        Assert.False(config.Tests.IncludeSetupMethods);

        Assert.False(config.Docs.Enabled);
        Assert.Equal(new[] { "guides/", "reference/" }, config.Docs.MarkdownDirs);
        Assert.False(config.Docs.IncludeXmlDocs);

        Assert.Equal(4, config.Query.DefaultDepth);
        Assert.Equal("json", config.Query.DefaultFormat);
        Assert.Equal("callers", config.Query.DefaultMode);
        Assert.Equal(99, config.Query.MaxNodes);
    }
}
