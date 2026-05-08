using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Workspace;

/// <summary>
/// Additional mutation-killing tests for CompilationFactory.
/// Targets: Create() method (file-based), missing source files,
/// missing reference DLLs, reference cache usage, lang version parsing.
/// </summary>
[Collection("StderrCapture")]
public class CompilationFactoryMutationTests : IDisposable
{
    private readonly string _testDir;

    public CompilationFactoryMutationTests()
    {
        _testDir = Path.Combine(Directory.GetCurrentDirectory(),
            "_comp_factory_mut_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    // ── Create: missing source file is skipped (not crash) ──

    [Fact]
    public void Create_MissingSourceFile_SkipsItGracefully()
    {
        var existingFile = Path.Combine(_testDir, "Good.cs");
        File.WriteAllText(existingFile, "public class Good { }");
        var missingFile = Path.Combine(_testDir, "Missing.cs");

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new[] { typeof(object).Assembly.Location, Path.Combine(runtimeDir, "System.Runtime.dll") }
            .Where(File.Exists);

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { existingFile, missingFile },
            refs);

        // Should have 1 syntax tree (the existing file), not 2
        Assert.Single(compilation.SyntaxTrees);
        Assert.Contains(compilation.SyntaxTrees, t => t.FilePath == existingFile);
    }

    // ── Create: missing reference DLL is skipped ──

    [Fact]
    public void Create_MissingReferenceDll_SkipsItGracefully()
    {
        var sourceFile = Path.Combine(_testDir, "Source.cs");
        File.WriteAllText(sourceFile, "public class Src { }");

        var goodRef = typeof(object).Assembly.Location;
        var badRef = Path.Combine(_testDir, "NonExistent.dll");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            new[] { goodRef, badRef });

        // Should still have references from the good DLL
        Assert.NotEmpty(compilation.References);
    }

    // ── Create: uses MetadataReferenceCache when provided ──

    [Fact]
    public void Create_WithReferenceCache_UsesCache()
    {
        var sourceFile = Path.Combine(_testDir, "CacheTest.cs");
        File.WriteAllText(sourceFile, "public class CacheTest { }");

        var cache = new MetadataReferenceCache();
        var refPath = typeof(object).Assembly.Location;

        var comp1 = CompilationFactory.Create(
            "Assembly1",
            new[] { sourceFile },
            new[] { refPath },
            referenceCache: cache);

        var comp2 = CompilationFactory.Create(
            "Assembly2",
            new[] { sourceFile },
            new[] { refPath },
            referenceCache: cache);

        // Both compilations should have references
        Assert.NotEmpty(comp1.References);
        Assert.NotEmpty(comp2.References);
    }

    // ── Create: nullableEnabled = true sets Enable ──

    [Fact]
    public void Create_NullableEnabled_SetsNullableContextEnable()
    {
        var sourceFile = Path.Combine(_testDir, "Nullable.cs");
        File.WriteAllText(sourceFile, "public class N { }");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            Array.Empty<string>(),
            nullableEnabled: true);

        Assert.Equal(NullableContextOptions.Enable, compilation.Options.NullableContextOptions);
    }

    // ── Create: nullableEnabled = false sets Disable ──

    [Fact]
    public void Create_NullableDisabled_SetsNullableContextDisable()
    {
        var sourceFile = Path.Combine(_testDir, "NoNullable.cs");
        File.WriteAllText(sourceFile, "public class NN { }");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            Array.Empty<string>(),
            nullableEnabled: false);

        Assert.Equal(NullableContextOptions.Disable, compilation.Options.NullableContextOptions);
    }

    // ── Create: preprocessor symbols are passed through ──

    [Fact]
    public void Create_WithPreprocessorSymbols_AreAvailableInTree()
    {
        var sourceFile = Path.Combine(_testDir, "Preprocessor.cs");
        File.WriteAllText(sourceFile, @"
public class P {
#if MY_FLAG
    public void Flagged() { }
#endif
}");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            new[] { typeof(object).Assembly.Location },
            preprocessorSymbols: new[] { "MY_FLAG" });

        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var methods = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .ToList();
        Assert.Single(methods);
        Assert.Equal("Flagged", methods[0].Identifier.Text);
    }

    // ── Create: assembly name is set correctly ──

    [Fact]
    public void Create_AssemblyName_IsPreserved()
    {
        var sourceFile = Path.Combine(_testDir, "Named.cs");
        File.WriteAllText(sourceFile, "public class Named { }");

        var compilation = CompilationFactory.Create(
            "MySpecialAssembly",
            new[] { sourceFile },
            Array.Empty<string>());

        Assert.Equal("MySpecialAssembly", compilation.AssemblyName);
    }

    // ── Create: file paths are preserved in syntax trees ──

    [Fact]
    public void Create_PreservesFilePaths()
    {
        var sourceFile = Path.Combine(_testDir, "MyFile.cs");
        File.WriteAllText(sourceFile, "public class MyFile { }");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            Array.Empty<string>());

        var tree = compilation.SyntaxTrees.Single();
        Assert.Equal(sourceFile, tree.FilePath);
    }

    // ── ParseLangVersion: exact version numbers ──

    [Theory]
    [InlineData("12.0", LanguageVersion.CSharp12)]
    [InlineData("11", LanguageVersion.CSharp11)]
    [InlineData("10.0", LanguageVersion.CSharp10)]
    public void Create_NumericLangVersion_ParsesExactVersion(string langVersion, LanguageVersion expected)
    {
        var sourceFile = Path.Combine(_testDir, $"Lang{langVersion.Replace(".", "")}.cs");
        File.WriteAllText(sourceFile, "public class LangTest { }");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            Array.Empty<string>(),
            langVersion: langVersion);

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(expected, parseOptions.SpecifiedLanguageVersion);
    }

    // ── CreateFromSourceTexts: empty sources produces empty trees ──

    [Fact]
    public void CreateFromSourceTexts_EmptySources_ProducesEmptyTreeList()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "Empty",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>());

        Assert.Empty(compilation.SyntaxTrees);
    }

    // ── CreateFromSourceTexts: multiple sources all get file paths ──

    [Fact]
    public void CreateFromSourceTexts_MultipleSources_AllHaveFilePaths()
    {
        var sources = new[]
        {
            ("File1.cs", "public class A { }"),
            ("File2.cs", "public class B { }"),
            ("File3.cs", "public class C { }")
        };

        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            sources,
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        Assert.Equal(3, compilation.SyntaxTrees.Count());
        Assert.Contains(compilation.SyntaxTrees, t => t.FilePath == "File1.cs");
        Assert.Contains(compilation.SyntaxTrees, t => t.FilePath == "File2.cs");
        Assert.Contains(compilation.SyntaxTrees, t => t.FilePath == "File3.cs");
    }

    // ── OutputKind is DynamicallyLinkedLibrary for both methods ──

    [Fact]
    public void Create_OutputKind_IsDll()
    {
        var sourceFile = Path.Combine(_testDir, "DllCheck.cs");
        File.WriteAllText(sourceFile, "public class DllCheck { }");

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { sourceFile },
            Array.Empty<string>());

        Assert.Equal(OutputKind.DynamicallyLinkedLibrary, compilation.Options.OutputKind);
    }
}
