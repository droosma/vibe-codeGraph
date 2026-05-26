using CodeGraph.Core.IO;
using CodeGraph.Indexer;

namespace CodeGraph.Indexer.Tests;

public sealed class CodeGraphIndexerIntegrationTests : IDisposable
{
    private readonly string _rootDir;

    public CodeGraphIndexerIntegrationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "indexer-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public async Task IndexAsync_BuildsMetadataAndStats_ForTemporarySolution()
    {
        CreateProject("CoreApp", "namespace CoreApp; public class Calculator { public int Add(int a, int b) => a + b; public int Sum() => Add(1, 2); }");
        var solutionPath = CreateSolution(("CoreApp", @"CoreApp\CoreApp.csproj"));

        var indexer = new CodeGraphIndexer(new IndexerOptions { SkipBuild = true, SkipRestore = true });
        var result = await indexer.IndexAsync(solutionPath);

        Assert.Equal("Sample.sln", result.Metadata.Solution);
        Assert.Equal("Sample", result.Metadata.SolutionName);
        Assert.Equal(string.Empty, result.Metadata.CommitHash);
        Assert.Equal(string.Empty, result.Metadata.Branch);
        Assert.Equal(new[] { "CoreApp" }, result.Metadata.ProjectsIndexed);
        Assert.Equal(result.Nodes.Count, result.Metadata.Stats["node_count"]);
        Assert.Equal(result.Edges.Count, result.Metadata.Stats["edge_count"]);
        Assert.Equal(result.Nodes.Count(n => n.Kind == CodeGraph.Core.Models.NodeKind.Type), result.Metadata.Stats["type_count"]);
        Assert.Equal(result.Nodes.Count(n => n.Kind is CodeGraph.Core.Models.NodeKind.Method or CodeGraph.Core.Models.NodeKind.Constructor), result.Metadata.Stats["method_count"]);
        Assert.Contains(result.Nodes, node => node.Name == "Calculator");
        Assert.Contains(result.Nodes, node => node.Name == "Add");
    }

    [Fact]
    public async Task IndexAsync_ProjectFilter_OnlyIndexesMatchingProjects()
    {
        CreateProject("CoreApp", "namespace CoreApp; public class CoreOnly { }");
        CreateProject("TestsApp", "namespace TestsApp; public class TestsOnly { }");
        var solutionPath = CreateSolution(("CoreApp", @"CoreApp\CoreApp.csproj"), ("TestsApp", @"TestsApp\TestsApp.csproj"));

        var indexer = new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            ProjectFilter = "Core*"
        });
        var result = await indexer.IndexAsync(solutionPath);

        Assert.Equal(new[] { "CoreApp" }, result.Metadata.ProjectsIndexed);
        Assert.DoesNotContain(result.Nodes, node => node.Id.Contains("TestsApp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexAsync_ExcludeProjects_WinsOverIncludeProjects()
    {
        CreateProject("CoreApp", "namespace CoreApp; public class CoreOnly { }");
        CreateProject("TestsApp", "namespace TestsApp; public class TestsOnly { }");
        var solutionPath = CreateSolution(("CoreApp", @"CoreApp\CoreApp.csproj"), ("TestsApp", @"TestsApp\TestsApp.csproj"));

        var indexer = new CodeGraphIndexer(new IndexerOptions
        {
            SkipBuild = true,
            SkipRestore = true,
            IncludeProjects = new[] { "*" },
            ExcludeProjects = new[] { "Tests*" }
        });
        var result = await indexer.IndexAsync(solutionPath);

        Assert.Equal(new[] { "CoreApp" }, result.Metadata.ProjectsIndexed);
        Assert.DoesNotContain(result.Nodes, node => node.Id.Contains("TestsApp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexAndWriteAsync_WritesJsonAndSqliteOutputs()
    {
        CreateProject("CoreApp", "namespace CoreApp; public class Calculator { public int Add(int a, int b) => a + b; }");
        var solutionPath = CreateSolution(("CoreApp", @"CoreApp\CoreApp.csproj"));
        var outputDir = Path.Combine(_rootDir, "out");

        var indexer = new CodeGraphIndexer(new IndexerOptions { SkipBuild = true, SkipRestore = true });
        var result = await indexer.IndexAndWriteAsync(solutionPath, outputDir);

        Assert.True(File.Exists(Path.Combine(outputDir, "meta.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "graph.db")));
        Assert.Contains(
            Directory.GetFiles(outputDir, "*.json"),
            path => !path.EndsWith("meta.json", StringComparison.OrdinalIgnoreCase));

        var (metadata, nodes, edges) = await GraphReader.ReadAsync(outputDir);
        Assert.Equal(result.Metadata.Solution, metadata.Solution);
        Assert.Equal(result.Nodes.Count, nodes.Count);
        Assert.Equal(result.Edges.Count, edges.Count);
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

    private string CreateSolution(params (string name, string relativePath)[] projects)
    {
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Global",
            "EndGlobal"
        };

        for (var i = projects.Length - 1; i >= 0; i--)
        {
            var guid = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("D");
            lines.Insert(1, $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projects[i].name}\", \"{projects[i].relativePath}\", \"{{{guid}}}\"");
            lines.Insert(2, "EndProject");
        }

        var solutionPath = Path.Combine(_rootDir, "Sample.sln");
        File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
        return solutionPath;
    }
}
