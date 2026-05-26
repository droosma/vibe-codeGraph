using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

public sealed class SolutionParserMutationTests : IDisposable
{
    private readonly string _testDir;

    public SolutionParserMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "solution-parser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void ParseSlnxContent_InvalidXml_ReturnsEmpty()
    {
        var entries = SolutionParser.ParseSlnxContent("<Solution><Project Path=\"broken\"");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseSlnxContent_SkipsFoldersMissingPathsAndNonCsproj_UsesFileNameFallback()
    {
        const string content = """
            <Solution>
              <Project Type="Folder" Name="Folder" Path="src" />
              <Project Name="MissingPath" />
              <Project Path="src/Ignore.fsproj" />
              <Project Path="src/Actual/Actual.csproj" />
            </Solution>
            """;

        var entry = Assert.Single(SolutionParser.ParseSlnxContent(content));
        Assert.Equal("Actual", entry.Name);
        Assert.Equal($"src{Path.DirectorySeparatorChar}Actual{Path.DirectorySeparatorChar}Actual.csproj", entry.RelativePath);
        Assert.Equal(string.Empty, entry.ProjectGuid);
    }

    [Fact]
    public void ParseSlnxContent_PreservesExplicitName()
    {
        const string content = """
            <Solution>
              <Project Name="Custom.Name" Path="src/Actual/Actual.csproj" />
            </Solution>
            """;

        var entry = Assert.Single(SolutionParser.ParseSlnxContent(content));
        Assert.Equal("Custom.Name", entry.Name);
        Assert.Equal($"src{Path.DirectorySeparatorChar}Actual{Path.DirectorySeparatorChar}Actual.csproj", entry.RelativePath);
    }

    [Fact]
    public void Parse_SlnxFile_UsesSlnxParser()
    {
        var slnxPath = Path.Combine(_testDir, "Test.slnx");
        File.WriteAllText(slnxPath, """
            <Solution>
              <Folder Name="src">
                <Project Path="src/App/App.csproj" />
              </Folder>
            </Solution>
            """);

        var entry = Assert.Single(SolutionParser.Parse(slnxPath));
        Assert.Equal("App", entry.Name);
        Assert.Equal($"src{Path.DirectorySeparatorChar}App{Path.DirectorySeparatorChar}App.csproj", entry.RelativePath);
        Assert.Equal(string.Empty, entry.ProjectGuid);
    }
}
