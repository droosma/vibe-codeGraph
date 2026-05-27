using System.Text.Json;
using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Workspace;

[Collection("FrameworkRefResolver")]
public sealed class FrameworkRefResolverAdditionalMutationTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string? _originalDotnetRoot;
    private readonly string? _originalProgramFiles;
    private readonly string? _originalProgramFilesX86;

    public FrameworkRefResolverAdditionalMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "framework-ref-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
        _originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        _originalProgramFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        _originalProgramFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        FrameworkRefResolver.ClearCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _originalDotnetRoot);
        Environment.SetEnvironmentVariable("ProgramFiles", _originalProgramFiles);
        Environment.SetEnvironmentVariable("ProgramFiles(x86)", _originalProgramFilesX86);
        FrameworkRefResolver.ClearCache();

        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public void Resolve_NoReferenceAssembliesFound_ReturnsEmptyAndWritesWarning()
    {
        UseSyntheticRootsOnly();

        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);

        try
        {
            var resolved = FrameworkRefResolver.Resolve("net8.0");
            Assert.Empty(resolved);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains(
            "Could not find framework reference assemblies for net8.0.",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ExactReferenceDirectory_IsPreferredOverPrefixMatches()
    {
        var exactDir = Path.Combine(_rootDir, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref", "net8.0");
        var prefixDir = Path.Combine(_rootDir, "packs", "Microsoft.NETCore.App.Ref", "8.0.0", "ref", "net8.0-preview");
        Directory.CreateDirectory(exactDir);
        Directory.CreateDirectory(prefixDir);
        File.WriteAllBytes(Path.Combine(exactDir, "ExactOnly.dll"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(prefixDir, "PreviewOnly.dll"), Array.Empty<byte>());

        UseSyntheticRootsOnly();

        var dll = Assert.Single(FrameworkRefResolver.Resolve("net8.0"));

        Assert.EndsWith("ExactOnly.dll", dll, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", dll, StringComparison.Ordinal);
    }

    private void UseSyntheticRootsOnly()
    {
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _rootDir);
        Environment.SetEnvironmentVariable("ProgramFiles", Path.Combine(_rootDir, "missing-program-files"));
        Environment.SetEnvironmentVariable("ProgramFiles(x86)", Path.Combine(_rootDir, "missing-program-files-x86"));
        FrameworkRefResolver.ClearCache();
    }
}

[Collection("StderrCapture")]
public sealed class HybridWorkspaceLoaderAdditionalMutationTests : IDisposable
{
    private readonly string _rootDir;

    public HybridWorkspaceLoaderAdditionalMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "hybrid-loader-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ReleaseConfiguration_AddsOnlyTraceSymbol()
    {
        CreateProject("Symbols");
        var solutionPath = CreateSolution(("Symbols", @"Symbols\Symbols.csproj"));

        var loader = new HybridWorkspaceLoader();
        var compilation = Assert.Single(await loader.LoadAsync(solutionPath, skipRestore: true, configuration: "Release"));
        var parseOptions = GetParseOptions(compilation);

        Assert.Equal(new[] { "TRACE" }, parseOptions.PreprocessorSymbolNames.ToArray());
    }

    [Fact]
    public async Task LoadAsync_DebugConfiguration_DoesNotDuplicateExistingSymbols()
    {
        CreateProject("Symbols");
        var solutionPath = CreateSolution(("Symbols", @"Symbols\Symbols.csproj"));

        var loader = new HybridWorkspaceLoader();
        var compilation = Assert.Single(await loader.LoadAsync(
            solutionPath,
            skipRestore: true,
            configuration: "Debug",
            preprocessorSymbols: new[] { "CUSTOM", "DEBUG", "TRACE" }));
        var parseOptions = GetParseOptions(compilation);

        Assert.Equal(new[] { "CUSTOM", "DEBUG", "TRACE" }, parseOptions.PreprocessorSymbolNames.ToArray());
    }

    private static CSharpParseOptions GetParseOptions(ProjectCompilation compilation)
    {
        var syntaxTree = Assert.Single(compilation.Compilation.SyntaxTrees);
        return Assert.IsType<CSharpParseOptions>(syntaxTree.Options);
    }

    private void CreateProject(string projectName)
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
        File.WriteAllText(Path.Combine(projectDir, "Class1.cs"), "namespace Demo; public class Sample { }");
    }

    private string CreateSolution(params (string Name, string RelativePath)[] projects)
    {
        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00"
        };

        for (var index = 0; index < projects.Length; index++)
        {
            var guid = new Guid(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("D");
            lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projects[index].Name}\", \"{projects[index].RelativePath}\", \"{{{guid}}}\"");
            lines.Add("EndProject");
        }

        var solutionPath = Path.Combine(_rootDir, "Test.sln");
        File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
        return solutionPath;
    }
}

public sealed class AssetsFileResolverAdditionalMutationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        AssetsFileResolver.ClearCache();

        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ValidPackage_UsesNormalizedDllPath()
    {
        var packageFolder = CreatePackageFolder("MyPkg", "1.0.0", "lib/net8.0/MyPkg.dll");
        var assetsJson = $$"""
            {
              "packageFolders": { "{{JsonFolder(packageFolder)}}": {} },
              "targets": {
                "net8.0": {
                  "MyPkg/1.0.0": {
                    "compile": { "lib/net8.0/MyPkg.dll": {} }
                  }
                }
              }
            }
            """;
        var projectDir = CreateProjectDir(assetsJson);

        var package = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));

        Assert.Equal(
            Path.Combine(packageFolder, "mypkg", "1.0.0", "lib", "net8.0", "MyPkg.dll"),
            package.DllPath);
    }

    [Fact]
    public void Resolve_InvalidPackageName_IsSkipped()
    {
        var packageFolder = CreatePackageFolder("invalid", "1.0.0", "lib/net8.0/Invalid.dll");
        var assetsJson = $$"""
            {
              "packageFolders": { "{{JsonFolder(packageFolder)}}": {} },
              "targets": {
                "net8.0": {
                  "InvalidPackageName": {
                    "compile": { "lib/net8.0/Invalid.dll": {} }
                  }
                }
              }
            }
            """;
        var projectDir = CreateProjectDir(assetsJson);

        var resolved = AssetsFileResolver.Resolve(projectDir, "net8.0");

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_PackageWithoutCompileSection_IsIgnoredWhenAnotherPackageIsValid()
    {
        var packageFolder = CreatePackageFolder("ValidPkg", "1.0.0", "lib/net8.0/ValidPkg.dll");
        var assetsJson = $$"""
            {
              "packageFolders": { "{{JsonFolder(packageFolder)}}": {} },
              "targets": {
                "net8.0": {
                  "MissingCompile/1.0.0": {},
                  "ValidPkg/1.0.0": {
                    "compile": { "lib/net8.0/ValidPkg.dll": {} }
                  }
                }
              }
            }
            """;
        var projectDir = CreateProjectDir(assetsJson);

        var package = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));

        Assert.Equal("ValidPkg", package.PackageId);
    }

    [Fact]
    public void Resolve_TargetFrameworkMatch_IsCaseInsensitive()
    {
        var packageFolder = CreatePackageFolder("CasePkg", "1.0.0", "lib/net8.0/CasePkg.dll");
        var assetsJson = $$"""
            {
              "packageFolders": { "{{JsonFolder(packageFolder)}}": {} },
              "targets": {
                "NET8.0": {
                  "CasePkg/1.0.0": {
                    "compile": { "lib/net8.0/CasePkg.dll": {} }
                  }
                }
              }
            }
            """;
        var projectDir = CreateProjectDir(assetsJson);

        var package = Assert.Single(AssetsFileResolver.Resolve(projectDir, "net8.0"));

        Assert.Equal("CasePkg", package.PackageId);
    }

    private string CreatePackageFolder(string packageId, string version, string relativeDllPath)
    {
        var packageFolder = Path.Combine(Path.GetTempPath(), "assets-package-" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(packageFolder);

        var dllPath = Path.Combine(
            packageFolder,
            packageId.ToLowerInvariant(),
            version,
            relativeDllPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        File.WriteAllBytes(dllPath, Array.Empty<byte>());
        return packageFolder;
    }

    private string CreateProjectDir(string assetsJson)
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "assets-project-" + Guid.NewGuid().ToString("N"));
        var objDir = Path.Combine(projectDir, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"), assetsJson);
        _tempDirs.Add(projectDir);
        return projectDir;
    }

    private static string JsonFolder(string path)
    {
        var withTrailing = path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
        return JsonSerializer.Serialize(withTrailing).Trim('"');
    }
}

public sealed class ProjectParserMoreMutationTests : IDisposable
{
    private readonly string _rootDir;

    public ProjectParserMoreMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "project-parser-more-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public void Parse_DirectoryPackagesProps_UsesNearestFile()
    {
        File.WriteAllText(Path.Combine(_rootDir, "Directory.Packages.props"), """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Demo.Package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var projectParent = Path.Combine(_rootDir, "src");
        Directory.CreateDirectory(projectParent);
        File.WriteAllText(Path.Combine(projectParent, "Directory.Packages.props"), """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Demo.Package" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        var projectDir = Path.Combine(projectParent, "App");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Demo.Package" />
              </ItemGroup>
            </Project>
            """);

        var package = Assert.Single(ProjectParser.Parse(projectPath).PackageReferences);

        Assert.Equal("2.0.0", package.Version);
    }

    [Fact]
    public void Parse_PackageReferenceWithoutInclude_IsIgnored()
    {
        var projectDir = Path.Combine(_rootDir, "NoInclude");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "NoInclude.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var info = ProjectParser.Parse(projectPath);

        Assert.Empty(info.PackageReferences);
    }

    [Fact]
    public void Parse_LegacyProject_NonCsCompileItemsAreIgnored()
    {
        var projectDir = Path.Combine(_rootDir, "Legacy");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), "class Program { }");
        File.WriteAllText(Path.Combine(projectDir, "HELPER.CS"), "class Helper { }");
        File.WriteAllText(Path.Combine(projectDir, "Notes.txt"), "ignored");
        var projectPath = Path.Combine(projectDir, "Legacy.csproj");
        File.WriteAllText(projectPath, """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.cs" />
                <Compile Include="HELPER.CS" />
                <Compile Include="Notes.txt" />
              </ItemGroup>
            </Project>
            """);

        var sourceFiles = ProjectParser.Parse(projectPath).SourceFiles;

        Assert.Equal(2, sourceFiles.Count);
        Assert.Contains(Path.Combine(projectDir, "Program.cs"), sourceFiles);
        Assert.Contains(Path.Combine(projectDir, "HELPER.CS"), sourceFiles);
        Assert.DoesNotContain(sourceFiles, path => path.EndsWith("Notes.txt", StringComparison.Ordinal));
    }
}

public sealed class CompilationFactoryKeywordLangVersionTests
{
    [Theory]
    [InlineData("latest", LanguageVersion.Latest)]
    [InlineData("preview", LanguageVersion.Preview)]
    [InlineData("default", LanguageVersion.Default)]
    public void CreateFromSourceTexts_KeywordLangVersion_UsesExpectedVersion(string langVersion, LanguageVersion expected)
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "LangTest",
            new[] { ("Sample.cs", "public class Sample { }") },
            Array.Empty<MetadataReference>(),
            langVersion: langVersion);

        var syntaxTree = Assert.Single(compilation.SyntaxTrees);
        var parseOptions = Assert.IsType<CSharpParseOptions>(syntaxTree.Options);

        Assert.Equal(expected, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_InvalidLangVersion_FallsBackToDefault()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "LangTest",
            new[] { ("Sample.cs", "public class Sample { }") },
            Array.Empty<MetadataReference>(),
            langVersion: "not-a-version");

        var syntaxTree = Assert.Single(compilation.SyntaxTrees);
        var parseOptions = Assert.IsType<CSharpParseOptions>(syntaxTree.Options);

        Assert.Equal(LanguageVersion.Default, parseOptions.SpecifiedLanguageVersion);
    }
}

public sealed class SolutionParserAdditionalMutationTests : IDisposable
{
    private readonly string _rootDir;

    public SolutionParserAdditionalMutationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "solution-parser-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    [Fact]
    public void Parse_UppercaseSlnxExtension_UsesSlnxParser()
    {
        var solutionPath = Path.Combine(_rootDir, "Sample.SLNX");
        File.WriteAllText(solutionPath, """
            <Solution>
              <Project Path="src/App/App.csproj" />
            </Solution>
            """);

        var entry = Assert.Single(SolutionParser.Parse(solutionPath));

        Assert.Equal("App", entry.Name);
        Assert.Equal($"src{Path.DirectorySeparatorChar}App{Path.DirectorySeparatorChar}App.csproj", entry.RelativePath);
    }

    [Fact]
    public void ParseContent_UppercaseCsprojExtension_IsAccepted()
    {
        var entries = SolutionParser.ParseContent("""
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src/App/App.CSPROJ", "{12345678-1234-1234-1234-123456789012}"
            EndProject
            """);

        var entry = Assert.Single(entries);

        Assert.Equal("App", entry.Name);
        Assert.EndsWith($"App{Path.DirectorySeparatorChar}App.CSPROJ", entry.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSlnxContent_FolderTypeComparison_IsCaseInsensitive()
    {
        var entries = SolutionParser.ParseSlnxContent("""
            <Solution>
              <Project Type="folder" Path="src" />
              <Project Path="src/App/App.csproj" />
            </Solution>
            """);

        var entry = Assert.Single(entries);

        Assert.Equal("App", entry.Name);
    }
}
