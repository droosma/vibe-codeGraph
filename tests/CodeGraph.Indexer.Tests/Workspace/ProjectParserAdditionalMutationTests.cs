using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

public sealed class ProjectParserAdditionalMutationTests : IDisposable
{
    private readonly string _testDir;

    public ProjectParserAdditionalMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "project-parser-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Parse_UsesNearestDirectoryBuildPropsOnly()
    {
        File.WriteAllText(Path.Combine(_testDir, "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <LangVersion>10.0</LangVersion>
              </PropertyGroup>
            </Project>
            """);

        var projectDir = Path.Combine(_testDir, "src", "App");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(_testDir, "src", "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <LangVersion>12.0</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = ProjectParser.Parse(projectPath);

        Assert.Equal("12.0", info.LangVersion);
        Assert.True(info.NullableEnabled);
    }

    [Fact]
    public void Parse_MalformedInheritedProps_AreIgnored()
    {
        File.WriteAllText(Path.Combine(_testDir, "Directory.Build.props"), "<Project><PropertyGroup><LangVersion>broken");
        File.WriteAllText(Path.Combine(_testDir, "Directory.Packages.props"), "<Project><ItemGroup><PackageVersion Include=\"Pkg\"");

        var projectDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Pkg" />
              </ItemGroup>
            </Project>
            """);

        var info = ProjectParser.Parse(projectPath);

        Assert.Null(info.LangVersion);
        var package = Assert.Single(info.PackageReferences);
        Assert.Equal("Pkg", package.Name);
        Assert.Null(package.Version);
    }

    [Fact]
    public void Parse_SdkStyle_IncludesObjFilesButExcludesBinFiles()
    {
        var projectDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "obj", "generated"));
        Directory.CreateDirectory(Path.Combine(projectDir, "bin", "Debug"));

        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), "class Program { }");
        File.WriteAllText(Path.Combine(projectDir, "obj", "generated", "Generated.cs"), "class Generated { }");
        File.WriteAllText(Path.Combine(projectDir, "bin", "Debug", "Skipped.cs"), "class Skipped { }");

        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = ProjectParser.Parse(projectPath);

        Assert.Equal(2, info.SourceFiles.Count);
        Assert.Contains(Path.Combine(projectDir, "Program.cs"), info.SourceFiles);
        Assert.Contains(Path.Combine(projectDir, "obj", "generated", "Generated.cs"), info.SourceFiles);
        Assert.DoesNotContain(Path.Combine(projectDir, "bin", "Debug", "Skipped.cs"), info.SourceFiles);
    }

    [Fact]
    public void ParseContent_ProjectReference_ResolvesFullPath()
    {
        var projectDir = Path.Combine(_testDir, "src", "App");
        Directory.CreateDirectory(projectDir);

        var info = ProjectParser.ParseContent(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Shared/Shared.csproj" />
              </ItemGroup>
            </Project>
            """,
            Path.Combine(projectDir, "App.csproj"),
            projectDir);

        var reference = Assert.Single(info.ProjectReferences);
        Assert.Equal(Path.GetFullPath(Path.Combine(projectDir, "..", "Shared", "Shared.csproj")), reference);
    }
}
