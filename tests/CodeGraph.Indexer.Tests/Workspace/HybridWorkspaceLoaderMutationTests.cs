using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

[Collection("StderrCapture")]
public sealed class HybridWorkspaceLoaderMutationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_PreservesSolutionOrder_AfterParallelCompilation()
    {
        var root = CreateTempDir();
        CreateProject(root, "Second", "public class SecondType { }");
        CreateProject(root, "First", "public class FirstType { }");
        var slnPath = CreateSolution(root, ("Second", @"Second\Second.csproj"), ("First", @"First\First.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipRestore: true);

        Assert.Equal(new[] { "Second", "First" }, results.Select(r => r.ProjectName).ToArray());
    }

    [Fact]
    public async Task LoadAsync_DebugConfiguration_IsCaseInsensitive_AndAddsTrace()
    {
        var root = CreateTempDir();
        CreateProject(root, "CaseTest", """
            namespace Demo;
            #if DEBUG
            public class DebugType { }
            #endif
            #if TRACE
            public class TraceType { }
            #endif
            """);
        var slnPath = CreateSolution(root, ("CaseTest", @"CaseTest\CaseTest.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipRestore: true, configuration: "debug");

        var typeNames = GetTypeNames(results[0]);
        Assert.Contains("DebugType", typeNames);
        Assert.Contains("TraceType", typeNames);
    }

    [Fact]
    public async Task LoadAsync_CustomSymbols_AreMergedWithReleaseDefaults()
    {
        var root = CreateTempDir();
        CreateProject(root, "ReleaseSymbols", """
            namespace Demo;
            #if DEBUG
            public class DebugType { }
            #endif
            #if TRACE
            public class TraceType { }
            #endif
            #if CUSTOM
            public class CustomType { }
            #endif
            """);
        var slnPath = CreateSolution(root, ("ReleaseSymbols", @"ReleaseSymbols\ReleaseSymbols.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(
            slnPath,
            skipRestore: true,
            configuration: "Release",
            preprocessorSymbols: new[] { "DEBUG", "CUSTOM" });

        var typeNames = GetTypeNames(results[0]);
        Assert.Contains("DebugType", typeNames);
        Assert.Contains("TraceType", typeNames);
        Assert.Contains("CustomType", typeNames);
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hybrid-loader-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void CreateProject(string root, string name, string source)
    {
        var projectDir = Path.Combine(root, name);
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, name + ".csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, "Class1.cs"), source);
    }

    private static string CreateSolution(string root, params (string name, string relativePath)[] projects)
    {
        var lines = new List<string> { "Microsoft Visual Studio Solution File, Format Version 12.00" };
        for (var i = 0; i < projects.Length; i++)
        {
            var guid = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("D");
            lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projects[i].name}\", \"{projects[i].relativePath}\", \"{{{guid}}}\"");
            lines.Add("EndProject");
        }

        var slnPath = Path.Combine(root, "Test.sln");
        File.WriteAllText(slnPath, string.Join(Environment.NewLine, lines));
        return slnPath;
    }

    private static IReadOnlyList<string> GetTypeNames(ProjectCompilation compilation)
    {
        return compilation.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Select(type => type.Identifier.Text)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
