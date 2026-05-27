using CodeGraph.Indexer;

namespace CodeGraph.Indexer.Tests;

public sealed class CodeGraphIndexerAdditionalMutationTests : IDisposable
{
    private readonly string _rootDir;

    public CodeGraphIndexerAdditionalMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "indexer-more-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public async Task IndexAsync_ProjectFilter_TakesPrecedenceOverIncludeProjects()
    {
        CreateProject("AlphaApp", "namespace AlphaApp; public class AlphaType { }");
        CreateProject("BetaApp", "namespace BetaApp; public class BetaType { }");
        var solutionPath = CreateSolution(("AlphaApp", @"AlphaApp\AlphaApp.csproj"), ("BetaApp", @"BetaApp\BetaApp.csproj"));

        var result = await new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            ProjectFilter = "Beta*",
            IncludeProjects = new[] { "Alpha*" }
        }).IndexAsync(solutionPath);

        Assert.Equal(new[] { "BetaApp" }, result.Metadata.ProjectsIndexed);
        Assert.DoesNotContain(result.Nodes, node => node.Id.Contains("AlphaApp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexAsync_IncludeProjectsSingleWildcard_DoesNotFilterProjects()
    {
        CreateProject("AlphaApp", "namespace AlphaApp; public class AlphaType { }");
        CreateProject("BetaApp", "namespace BetaApp; public class BetaType { }");
        var solutionPath = CreateSolution(("AlphaApp", @"AlphaApp\AlphaApp.csproj"), ("BetaApp", @"BetaApp\BetaApp.csproj"));

        var result = await new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            IncludeProjects = new[] { "*" }
        }).IndexAsync(solutionPath);

        Assert.Equal(new[] { "AlphaApp", "BetaApp" }, result.Metadata.ProjectsIndexed);
    }

    [Fact]
    public async Task IndexAsync_ProjectFilter_QuestionWildcard_IsCaseInsensitive()
    {
        CreateProject("CoreA", "namespace CoreA; public class MatchType { }");
        CreateProject("CoreAB", "namespace CoreAB; public class ExtraType { }");
        var solutionPath = CreateSolution(("CoreA", @"CoreA\CoreA.csproj"), ("CoreAB", @"CoreAB\CoreAB.csproj"));

        var result = await new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            ProjectFilter = "core?"
        }).IndexAsync(solutionPath);

        Assert.Equal(new[] { "CoreA" }, result.Metadata.ProjectsIndexed);
        Assert.DoesNotContain(result.Nodes, node => node.Id.Contains("CoreAB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexAsync_ProjectFilter_NoMatches_ReturnsEmptyResult()
    {
        CreateProject("AlphaApp", "namespace AlphaApp; public class AlphaType { }");
        CreateProject("BetaApp", "namespace BetaApp; public class BetaType { }");
        var solutionPath = CreateSolution(("AlphaApp", @"AlphaApp\AlphaApp.csproj"), ("BetaApp", @"BetaApp\BetaApp.csproj"));

        var result = await new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            ProjectFilter = "Missing*"
        }).IndexAsync(solutionPath);

        Assert.Empty(result.Metadata.ProjectsIndexed);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.Equal(0, result.Metadata.Stats["node_count"]);
        Assert.Equal(0, result.Metadata.Stats["edge_count"]);
        Assert.Equal(0, result.Metadata.Stats["type_count"]);
        Assert.Equal(0, result.Metadata.Stats["method_count"]);
    }

    private void CreateProject(string projectName, string source)
    {
        var projectDir = Path.Combine(_rootDir, projectName);
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, projectName + ".csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, projectName + ".cs"), source);
    }

    private string CreateSolution(params (string Name, string RelativePath)[] projects)
    {
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Global",
            "EndGlobal"
        };

        for (var index = projects.Length - 1; index >= 0; index--)
        {
            var guid = new Guid(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("D");
            lines.Insert(1, $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projects[index].Name}\", \"{projects[index].RelativePath}\", \"{{{guid}}}\"");
            lines.Insert(2, "EndProject");
        }

        var solutionPath = Path.Combine(_rootDir, "Sample.sln");
        File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
        return solutionPath;
    }
}
