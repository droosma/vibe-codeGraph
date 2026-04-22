using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

[CollectionDefinition("StderrCapture", DisableParallelization = true)]
public class StderrCaptureCollection { }

public class HybridWorkspaceLoaderTests
{
    [Fact]
    public async Task LoadAsync_NonExistentSolution_ThrowsFileNotFoundException()
    {
        var loader = new HybridWorkspaceLoader();
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".sln");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => loader.LoadAsync(fakePath, skipBuild: true));
        Assert.Contains(fakePath, ex.Message);
    }

    [Fact]
    public void CanConstruct_HybridWorkspaceLoader()
    {
        var loader = new HybridWorkspaceLoader();

        Assert.NotNull(loader);
    }
}

[Collection("StderrCapture")]
public class HybridWorkspaceLoaderIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hwl_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private string CreateMinimalSolution(string rootDir, params (string name, string relPath)[] projects)
    {
        var slnPath = Path.Combine(rootDir, "Test.sln");
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00"
        };
        for (int i = 0; i < projects.Length; i++)
        {
            var guid = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("D");
            lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projects[i].name}\", \"{projects[i].relPath}\", \"{{{guid}}}\"");
            lines.Add("EndProject");
        }
        File.WriteAllText(slnPath, string.Join(Environment.NewLine, lines));
        return slnPath;
    }

    private void CreateMinimalProject(string rootDir, string projSubDir, string projName, string? csContent = null)
    {
        var projDir = Path.Combine(rootDir, projSubDir);
        Directory.CreateDirectory(projDir);

        var csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        File.WriteAllText(Path.Combine(projDir, projName + ".csproj"), csproj);

        csContent ??= $"namespace {projName} {{ public class Foo {{ public void Bar() {{ }} }} }}";
        File.WriteAllText(Path.Combine(projDir, "Class1.cs"), csContent);
    }

    [Fact]
    public async Task LoadAsync_SingleProject_ReturnsCorrectProjectCompilation()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "TestProj", "TestProj");
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true);

        Assert.Single(results);
        var pc = results[0];
        Assert.Equal("TestProj", pc.ProjectName);
        Assert.Equal("TestProj", pc.AssemblyName);
        Assert.Equal("net8.0", pc.TargetFramework);
        Assert.NotNull(pc.Compilation);
    }

    [Fact]
    public async Task LoadAsync_MultipleProjects_ReturnsAll()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "ProjA", "ProjA");
        CreateMinimalProject(root, "ProjB", "ProjB");
        var slnPath = CreateMinimalSolution(root,
            ("ProjA", @"ProjA\ProjA.csproj"),
            ("ProjB", @"ProjB\ProjB.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ProjectName == "ProjA");
        Assert.Contains(results, r => r.ProjectName == "ProjB");
    }

    [Fact]
    public async Task LoadAsync_MissingProjectFile_SkipsItAndLoadsOthers()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "Good", "Good");
        // Reference a non-existent project
        var slnPath = CreateMinimalSolution(root,
            ("Good", @"Good\Good.csproj"),
            ("Missing", @"Missing\Missing.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true);

        Assert.Single(results);
        Assert.Equal("Good", results[0].ProjectName);
    }

    [Fact]
    public async Task LoadAsync_DebugConfiguration_AddsDebugAndTraceSymbols()
    {
        var root = CreateTempDir();
        var csContent = @"namespace TestProj {
#if DEBUG
    public class DebugOnly { }
#endif
#if TRACE
    public class TraceOnly { }
#endif
    public class Always { }
}";
        CreateMinimalProject(root, "TestProj", "TestProj", csContent);
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true, configuration: "Debug");

        var compilation = results[0].Compilation;
        var typeNames = compilation.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Select(c => c.Identifier.Text)
            .ToList();

        Assert.Contains("DebugOnly", typeNames);
        Assert.Contains("TraceOnly", typeNames);
        Assert.Contains("Always", typeNames);
    }

    [Fact]
    public async Task LoadAsync_ReleaseConfiguration_AddsTraceButNotDebug()
    {
        var root = CreateTempDir();
        var csContent = @"namespace TestProj {
#if DEBUG
    public class DebugOnly { }
#endif
#if TRACE
    public class TraceOnly { }
#endif
    public class Always { }
}";
        CreateMinimalProject(root, "TestProj", "TestProj", csContent);
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true, configuration: "Release");

        var compilation = results[0].Compilation;
        var typeNames = compilation.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Select(c => c.Identifier.Text)
            .ToList();

        Assert.DoesNotContain("DebugOnly", typeNames);
        Assert.Contains("TraceOnly", typeNames);
        Assert.Contains("Always", typeNames);
    }

    [Fact]
    public async Task LoadAsync_CustomPreprocessorSymbols_IncludedInCompilation()
    {
        var root = CreateTempDir();
        var csContent = @"namespace TestProj {
#if MY_DEFINE
    public class CustomDefined { }
#endif
    public class Always { }
}";
        CreateMinimalProject(root, "TestProj", "TestProj", csContent);
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true,
            preprocessorSymbols: new[] { "MY_DEFINE" });

        var typeNames = results[0].Compilation.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Select(c => c.Identifier.Text)
            .ToList();

        Assert.Contains("CustomDefined", typeNames);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "TestProj", "TestProj");
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var loader = new HybridWorkspaceLoader();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => loader.LoadAsync(slnPath, skipBuild: true, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LoadAsync_CompilationHasSyntaxTrees()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "TestProj", "TestProj");
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true);

        Assert.NotEmpty(results[0].Compilation.SyntaxTrees);
    }

    [Fact]
    public async Task LoadAsync_ProjectPathIsSet()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "TestProj", "TestProj");
        var slnPath = CreateMinimalSolution(root, ("TestProj", @"TestProj\TestProj.csproj"));

        var loader = new HybridWorkspaceLoader();
        var results = await loader.LoadAsync(slnPath, skipBuild: true);

        Assert.EndsWith("TestProj.csproj", results[0].ProjectPath);
    }

    [Fact]
    public async Task LoadAsync_EmitsParsingProgressToStderr()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "ProjA", "ProjA");
        CreateMinimalProject(root, "ProjB", "ProjB");
        var slnPath = CreateMinimalSolution(root,
            ("ProjA", @"ProjA\ProjA.csproj"),
            ("ProjB", @"ProjB\ProjB.csproj"));

        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var loader = new HybridWorkspaceLoader();
            await loader.LoadAsync(slnPath, skipBuild: true);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var stderr = sw.ToString();
        Assert.Contains("Parsing project 1/2", stderr);
        Assert.Contains("Parsing project 2/2", stderr);
    }

    [Fact]
    public async Task LoadAsync_EmitsCompilingProgressToStderr()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "ProjA", "ProjA");
        CreateMinimalProject(root, "ProjB", "ProjB");
        var slnPath = CreateMinimalSolution(root,
            ("ProjA", @"ProjA\ProjA.csproj"),
            ("ProjB", @"ProjB\ProjB.csproj"));

        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var loader = new HybridWorkspaceLoader();
            await loader.LoadAsync(slnPath, skipBuild: true);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var stderr = sw.ToString();
        Assert.Contains("Compiling 1/2", stderr);
        Assert.Contains("Compiling 2/2", stderr);
    }

    [Fact]
    public async Task LoadAsync_MissingProject_WritesWarningToStderr()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "Good", "Good");
        var slnPath = CreateMinimalSolution(root,
            ("Good", @"Good\Good.csproj"),
            ("Missing", @"Missing\Missing.csproj"));

        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var loader = new HybridWorkspaceLoader();
            await loader.LoadAsync(slnPath, skipBuild: true);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var stderr = sw.ToString();
        Assert.Contains("Warning", stderr);
        Assert.Contains("Missing.csproj", stderr);
    }

    [Fact]
    public async Task LoadAsync_BuildFailure_EmitsWarningAndContinues()
    {
        var root = CreateTempDir();
        CreateMinimalProject(root, "BadBuild", "BadBuild");

        // Add a non-existent NuGet package to force dotnet build to fail
        var csprojPath = Path.Combine(root, "BadBuild", "BadBuild.csproj");
        var csproj = File.ReadAllText(csprojPath).Replace(
            "</Project>",
            """
              <ItemGroup>
                <PackageReference Include="NonExistent.Package.ZZZZZ" Version="99.99.99" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(csprojPath, csproj);

        // Create a proper solution file that dotnet build can understand
        var slnPath = Path.Combine(root, "Test.sln");
        var sep = Path.DirectorySeparatorChar;
        var slnContent = "Microsoft Visual Studio Solution File, Format Version 12.00\n"
            + $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"BadBuild\", \"BadBuild{sep}BadBuild.csproj\", \"{{00000001-0000-0000-0000-000000000000}}\"\n"
            + "EndProject\n"
            + "Global\n"
            + "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n"
            + "\t\tDebug|Any CPU = Debug|Any CPU\n"
            + "\tEndGlobalSection\n"
            + "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n"
            + "\t\t{00000001-0000-0000-0000-000000000000}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n"
            + "\t\t{00000001-0000-0000-0000-000000000000}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
            + "\tEndGlobalSection\n"
            + "EndGlobal\n";
        File.WriteAllText(slnPath, slnContent);

        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var loader = new HybridWorkspaceLoader();
            // skipBuild: false — the real dotnet build will fail
            var results = await loader.LoadAsync(slnPath, skipBuild: false);

            // Should still return results (best-effort Roslyn compilation)
            Assert.Single(results);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var stderr = sw.ToString();
        Assert.Contains("Warning: dotnet build failed", stderr);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
