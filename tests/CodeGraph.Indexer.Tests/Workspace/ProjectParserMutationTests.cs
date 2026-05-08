using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Tests.Workspace;

/// <summary>
/// Additional mutation-killing tests for ProjectParser.
/// Targets: ResolveTargetFramework (multi-targeting, default fallback),
/// ExtractVersion, central package management, GlobSourceFiles,
/// ParseLegacySourceFiles, and inherited property resolution.
/// </summary>
public class ProjectParserMutationTests : IDisposable
{
    private readonly string _testDir;

    public ProjectParserMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"proj_parse_mut_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    // ── ResolveTargetFramework: selects highest version from TargetFrameworks ──

    [Fact]
    public void Parse_MultiTargeting_SelectsHighestVersion()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;net7.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "proj1");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("net8.0", info.TargetFramework);
    }

    // ── ResolveTargetFramework: empty TFM/TFMs defaults to net10.0 ──

    [Fact]
    public void Parse_NoTargetFramework_DefaultsToNet10()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "noTfm");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "NoTfm.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("net10.0", info.TargetFramework);
    }

    // ── ResolveTargetFramework: single TargetFramework takes precedence ──

    [Fact]
    public void Parse_SingleTargetFramework_UsesIt()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "single");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Single.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("net9.0", info.TargetFramework);
    }

    // ── ExtractVersion: parsing "netstandard2.1" → 2.1 ──

    [Fact]
    public void Parse_MultiTargeting_WithNetstandard_SelectsHigherNumeric()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "mixed");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Mixed.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        // net8.0 -> 8.0 which is higher than netstandard2.1 -> 2.1
        Assert.Equal("net8.0", info.TargetFramework);
    }

    // ── AssemblyName defaults to project file name ──

    [Fact]
    public void Parse_NoAssemblyName_DefaultsToFileName()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "defname");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "MyProject.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("MyProject", info.AssemblyName);
    }

    // ── RootNamespace defaults to project file name ──

    [Fact]
    public void Parse_NoRootNamespace_DefaultsToFileName()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "defrns");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "RnsProject.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("RnsProject", info.RootNamespace);
    }

    // ── NullableEnabled ──

    [Theory]
    [InlineData("enable", true)]
    [InlineData("disable", false)]
    [InlineData("", false)]
    public void Parse_NullableProperty_SetsNullableEnabled(string nullable, bool expected)
    {
        var nullableProp = string.IsNullOrEmpty(nullable) ? "" : $"<Nullable>{nullable}</Nullable>";
        var content = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    {nullableProp}
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, $"null_{nullable}");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal(expected, info.NullableEnabled);
    }

    // ── SDK style detection ──

    [Fact]
    public void Parse_SdkStyleProject_IsSdkStyleTrue()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "sdkstyle");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Sdk.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.True(info.IsSdkStyle);
    }

    [Fact]
    public void Parse_LegacyProject_IsSdkStyleFalse()
    {
        var content = @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Class1.cs"" />
  </ItemGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "legacy");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Legacy.csproj");
        File.WriteAllText(projPath, content);
        File.WriteAllText(Path.Combine(projDir, "Class1.cs"), "public class C { }");

        var info = ProjectParser.Parse(projPath);

        Assert.False(info.IsSdkStyle);
    }

    // ── GlobSourceFiles excludes bin/ ──

    [Fact]
    public void Parse_SdkStyle_ExcludesBinDirectory()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "binexcl");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);
        File.WriteAllText(Path.Combine(projDir, "Good.cs"), "class Good {}");
        var binDir = Path.Combine(projDir, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "Bad.cs"), "class Bad {}");

        var info = ProjectParser.Parse(projPath);

        Assert.Contains(info.SourceFiles, f => f.Contains("Good.cs"));
        Assert.DoesNotContain(info.SourceFiles, f => f.Contains("Bad.cs"));
    }

    // ── ProjectReferences are parsed ──

    [Fact]
    public void Parse_ProjectReferences_AreExtracted()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Other\Other.csproj"" />
  </ItemGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "projref");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Single(info.ProjectReferences);
        Assert.Contains("Other.csproj", info.ProjectReferences[0]);
    }

    // ── PackageReferences are parsed ──

    [Fact]
    public void Parse_PackageReferences_AreExtracted()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.1"" />
  </ItemGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "pkgref");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Single(info.PackageReferences);
        Assert.Equal("Newtonsoft.Json", info.PackageReferences[0].Name);
        Assert.Equal("13.0.1", info.PackageReferences[0].Version);
    }

    // ── Central package management fallback ──

    [Fact]
    public void Parse_CentralPackageManagement_FallsBackToDirectoryPackagesProps()
    {
        // Create Directory.Packages.props in parent directory
        var parentDir = _testDir;
        File.WriteAllText(Path.Combine(parentDir, "Directory.Packages.props"), @"<Project>
  <ItemGroup>
    <PackageVersion Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>");

        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" />
  </ItemGroup>
</Project>";
        var projDir = Path.Combine(parentDir, "subproj");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Single(info.PackageReferences);
        Assert.Equal("13.0.3", info.PackageReferences[0].Version);
    }

    // ── Directory.Build.props inheritance ──

    [Fact]
    public void Parse_InheritsFromDirectoryBuildProps()
    {
        File.WriteAllText(Path.Combine(_testDir, "Directory.Build.props"), @"<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
</Project>");

        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "inherit");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("12.0", info.LangVersion);
    }

    // ── Local property overrides inherited ──

    [Fact]
    public void Parse_LocalPropertyOverridesInherited()
    {
        File.WriteAllText(Path.Combine(_testDir, "Directory.Build.props"), @"<Project>
  <PropertyGroup>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
</Project>");

        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "override");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("12.0", info.LangVersion);
    }

    // ── Parse non-existent file throws ──

    [Fact]
    public void Parse_NonExistentFile_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(_testDir, "nonexistent.csproj");
        Assert.Throws<FileNotFoundException>(() => ProjectParser.Parse(fakePath));
    }

    // ── ParseContent: Version as child element ──

    [Fact]
    public void Parse_PackageReference_VersionAsChildElement()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""SomePackage"">
      <Version>2.0.0</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "childver");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Test.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Single(info.PackageReferences);
        Assert.Equal("2.0.0", info.PackageReferences[0].Version);
    }

    // ── TargetFrameworks with whitespace ──

    [Fact]
    public void Parse_TargetFramework_TrimsWhitespace()
    {
        var content = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>  net8.0  </TargetFramework>
  </PropertyGroup>
</Project>";
        var projDir = Path.Combine(_testDir, "trim");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Trim.csproj");
        File.WriteAllText(projPath, content);

        var info = ProjectParser.Parse(projPath);

        Assert.Equal("net8.0", info.TargetFramework);
    }
}
