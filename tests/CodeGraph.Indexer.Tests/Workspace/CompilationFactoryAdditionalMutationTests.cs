using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Workspace;

[Collection("StderrCapture")]
public sealed class CompilationFactoryAdditionalMutationTests : IDisposable
{
    private readonly string _testDir;

    public CompilationFactoryAdditionalMutationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "compilation-factory-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Create_MissingSourceFile_WritesExactWarningAndSkipsFile()
    {
        var sourceFile = Path.Combine(_testDir, "Existing.cs");
        var missingFile = Path.Combine(_testDir, "Missing.cs");
        File.WriteAllText(sourceFile, "public class Existing { }");

        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);

        try
        {
            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile, missingFile },
                Array.Empty<string>());

            var tree = Assert.Single(compilation.SyntaxTrees);
            Assert.Equal(sourceFile, tree.FilePath);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal($"Warning: Source file not found, skipping: {missingFile}{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public void Create_MissingReferenceDll_WritesExactWarningAndKeepsValidReference()
    {
        var sourceFile = Path.Combine(_testDir, "Existing.cs");
        var missingDll = Path.Combine(_testDir, "Missing.dll");
        File.WriteAllText(sourceFile, "public class Existing { }");

        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);

        try
        {
            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { typeof(object).Assembly.Location, missingDll });

            Assert.Contains(compilation.References, r => string.Equals(r.Display, typeof(object).Assembly.Location, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal($"Warning: Reference DLL not found, skipping: {missingDll}{Environment.NewLine}", writer.ToString());
    }

    [Fact]
    public void Create_LockedReferenceDll_WritesLoadWarning()
    {
        var sourceFile = Path.Combine(_testDir, "Existing.cs");
        var lockedDll = Path.Combine(_testDir, "Locked.dll");
        File.WriteAllText(sourceFile, "public class Existing { }");
        File.WriteAllText(lockedDll, "not a dll");

        using var lockStream = new FileStream(lockedDll, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var originalError = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);

        try
        {
            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { lockedDll });

            Assert.Empty(compilation.References);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains($"Warning: Could not load reference {lockedDll}:", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromSourceTexts_ExactLanguageVersionAndSymbols_ArePreserved()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Sample.cs", "public class Sample { }") },
            Array.Empty<MetadataReference>(),
            langVersion: "12.0",
            preprocessorSymbols: new[] { "A", "B" });

        var tree = Assert.Single(compilation.SyntaxTrees);
        var options = Assert.IsType<CSharpParseOptions>(tree.Options);
        Assert.Equal(LanguageVersion.CSharp12, options.SpecifiedLanguageVersion);
        Assert.Equal(new[] { "A", "B" }, options.PreprocessorSymbolNames.ToArray());
    }
}
