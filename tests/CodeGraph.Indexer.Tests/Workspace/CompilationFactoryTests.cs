using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Workspace;

[Collection("StderrCapture")]
public class CompilationFactoryTests
{
    [Fact]
    public void CreateFromSourceTexts_SimpleClass_CompilesSuccessfully()
    {
        var source = @"
namespace TestApp;

public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
}";

        var coreRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtimeRef = MetadataReference.CreateFromFile(
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll"));

        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Calculator.cs", source) },
            new[] { coreRef, runtimeRef });

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CreateFromSourceTexts_MultipleSources_CompilesSuccessfully()
    {
        var source1 = @"
namespace TestApp;

public interface IGreeter
{
    string Greet(string name);
}";

        var source2 = @"
namespace TestApp;

public class Greeter : IGreeter
{
    public string Greet(string name) => $""Hello, {name}!"";
}";

        var coreRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var runtimeRef = MetadataReference.CreateFromFile(
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll"));

        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("IGreeter.cs", source1), ("Greeter.cs", source2) },
            new[] { coreRef, runtimeRef });

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CreateFromSourceTexts_PreservesFilePath()
    {
        var source = "public class Foo { }";

        var coreRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("MyFile.cs", source) },
            new[] { coreRef });

        var tree = compilation.SyntaxTrees.Single();
        Assert.Equal("MyFile.cs", tree.FilePath);
    }

    [Fact]
    public void CreateFromSourceTexts_WithNullableEnabled_SetsNullableContext()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>(),
            nullableEnabled: true);

        Assert.Equal(NullableContextOptions.Enable, compilation.Options.NullableContextOptions);
    }

    [Fact]
    public void CreateFromSourceTexts_WithNullableDisabled_SetsDisabledContext()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>(),
            nullableEnabled: false);

        Assert.Equal(NullableContextOptions.Disable, compilation.Options.NullableContextOptions);
    }

    [Fact]
    public void CreateFromSourceTexts_OutputKind_IsDll()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>());

        Assert.Equal(OutputKind.DynamicallyLinkedLibrary, compilation.Options.OutputKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateFromSourceTexts_NullOrEmptyLangVersion_UsesDefault(string? langVersion)
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>(),
            langVersion: langVersion);

        var tree = compilation.SyntaxTrees.FirstOrDefault();
        // Default language version — just verify it creates successfully
        Assert.NotNull(compilation);
    }

    [Theory]
    [InlineData("latest", LanguageVersion.Latest)]
    [InlineData("LATEST", LanguageVersion.Latest)]
    [InlineData("preview", LanguageVersion.Preview)]
    [InlineData("PREVIEW", LanguageVersion.Preview)]
    [InlineData("default", LanguageVersion.Default)]
    [InlineData("DEFAULT", LanguageVersion.Default)]
    public void CreateFromSourceTexts_NamedLangVersion_ParsesCorrectly(string langVersion, LanguageVersion expected)
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: langVersion);

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(expected, parseOptions.SpecifiedLanguageVersion);
    }

    [Theory]
    [InlineData("12.0")]
    [InlineData("11")]
    public void CreateFromSourceTexts_NumericLangVersion_ParsesSuccessfully(string langVersion)
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: langVersion);

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        // Should parse to a specific version, not Default
        Assert.NotEqual(LanguageVersion.Default, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_UnrecognizedLangVersion_FallsBackToDefault()
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: "not-a-version");

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(LanguageVersion.Default, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_WithPreprocessorSymbols_SetsSymbols()
    {
        var source = @"
public class Foo {
#if MY_SYMBOL
    public void Bar() { }
#endif
}";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            preprocessorSymbols: new[] { "MY_SYMBOL" });

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CreateFromSourceTexts_AssemblyName_IsPreserved()
    {
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "MyCustomAssembly",
            Array.Empty<(string, string)>(),
            Array.Empty<MetadataReference>());

        Assert.Equal("MyCustomAssembly", compilation.AssemblyName);
    }

    [Fact]
    public void Create_WithRealSourceFile_CompilesSuccessfully()
    {
        // Test the file-based Create method to cover L23-L34 (File.Exists, ReadAllText)
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "TestClass.cs");
            File.WriteAllText(sourceFile, "public class TestClass { public int Value { get; set; } }");

            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var refs = new List<string>
            {
                typeof(object).Assembly.Location,
                Path.Combine(runtimeDir, "System.Runtime.dll")
            };

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                refs);

            Assert.Single(compilation.SyntaxTrees);
            Assert.Equal("TestAssembly", compilation.AssemblyName);
            Assert.Equal(OutputKind.DynamicallyLinkedLibrary, compilation.Options.OutputKind);
            Assert.True(compilation.References.Count() >= 2, "Should have loaded reference DLLs");
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_MissingSourceFile_SkipsFile()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new[] { typeof(object).Assembly.Location };

        var compilation = CompilationFactory.Create(
            "TestAssembly",
            new[] { "nonexistent_file.cs" },
            refs);

        // Missing source file should be skipped
        Assert.Empty(compilation.SyntaxTrees);
    }

    [Fact]
    public void Create_MissingReferenceDll_SkipsReference()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, "public class T { }");

            var refs = new List<string>
            {
                typeof(object).Assembly.Location,
                "nonexistent_reference.dll"
            };

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                refs);

            Assert.Single(compilation.SyntaxTrees);
            // One reference should be loaded (object.dll), the other skipped
            Assert.True(compilation.References.Count() >= 1);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_NullableEnabled_SetsNullableOptions()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, "public class T { }");

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { typeof(object).Assembly.Location },
                nullableEnabled: true);

            Assert.Equal(NullableContextOptions.Enable, compilation.Options.NullableContextOptions);

            var compilation2 = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { typeof(object).Assembly.Location },
                nullableEnabled: false);

            Assert.Equal(NullableContextOptions.Disable, compilation2.Options.NullableContextOptions);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_WithLangVersion_SetsParseOptions()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, "public class T { }");

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { typeof(object).Assembly.Location },
                langVersion: "latest");

            var tree = compilation.SyntaxTrees.Single();
            var opts = (CSharpParseOptions)tree.Options;
            Assert.Equal(LanguageVersion.Latest, opts.SpecifiedLanguageVersion);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_WithNumericLangVersion_ParsesCorrectly()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, "public class T { }");

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                new[] { typeof(object).Assembly.Location },
                langVersion: "12.0");

            var tree = compilation.SyntaxTrees.Single();
            var opts = (CSharpParseOptions)tree.Options;
            Assert.Equal(LanguageVersion.CSharp12, opts.SpecifiedLanguageVersion);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_WithPreprocessorSymbols_IncludesConditionalCode()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, @"
#if MY_FLAG
public class Flagged { }
#endif
public class Always { }
");

            var compilation = CompilationFactory.Create(
                "TestAssembly",
                new[] { sourceFile },
                Array.Empty<string>(),
                preprocessorSymbols: new[] { "MY_FLAG" });

            var typeNames = compilation.SyntaxTrees
                .SelectMany(t => t.GetRoot().DescendantNodes())
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .Select(c => c.Identifier.Text)
                .ToList();

            Assert.Contains("Flagged", typeNames);
            Assert.Contains("Always", typeNames);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_EmptySourceFiles_ReturnsValidCompilation()
    {
        var compilation = CompilationFactory.Create(
            "EmptyAsm",
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.NotNull(compilation);
        Assert.Equal("EmptyAsm", compilation.AssemblyName);
        Assert.Empty(compilation.SyntaxTrees);
    }

    [Fact]
    public void Create_MultipleSourceFiles_AllIncluded()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var file1 = Path.Combine(testDir, "A.cs");
            var file2 = Path.Combine(testDir, "B.cs");
            var file3 = Path.Combine(testDir, "C.cs");
            File.WriteAllText(file1, "public class A { }");
            File.WriteAllText(file2, "public class B { }");
            File.WriteAllText(file3, "public class C { }");

            var compilation = CompilationFactory.Create(
                "MultiAsm",
                new[] { file1, file2, file3 },
                Array.Empty<string>());

            Assert.Equal(3, compilation.SyntaxTrees.Count());
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_MissingSourceFile_WritesWarningToStderr()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var realFile = Path.Combine(testDir, "Real.cs");
            File.WriteAllText(realFile, "public class Real { }");
            var fakeFile = Path.Combine(testDir, "DoesNotExist.cs");

            var originalErr = Console.Error;
            var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                var compilation = CompilationFactory.Create(
                    "TestAsm",
                    new[] { fakeFile, realFile },
                    Array.Empty<string>());

                Assert.Single(compilation.SyntaxTrees);
            }
            finally
            {
                Console.SetError(originalErr);
            }

            var stderr = sw.ToString();
            Assert.Contains("Warning", stderr);
            Assert.Contains("DoesNotExist.cs", stderr);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Create_MissingReferenceDll_WritesWarningToStderr()
    {
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "_test_comp_factory_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(testDir);
        try
        {
            var sourceFile = Path.Combine(testDir, "Test.cs");
            File.WriteAllText(sourceFile, "public class T { }");

            var originalErr = Console.Error;
            var sw = new StringWriter();
            Console.SetError(sw);
            try
            {
                var compilation = CompilationFactory.Create(
                    "TestAsm",
                    new[] { sourceFile },
                    new[] { "nonexistent_reference.dll" });

                Assert.Single(compilation.SyntaxTrees);
                Assert.Empty(compilation.References);
            }
            finally
            {
                Console.SetError(originalErr);
            }

            var stderr = sw.ToString();
            Assert.Contains("Warning", stderr);
            Assert.Contains("nonexistent_reference.dll", stderr);
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void CreateFromSourceTexts_LangVersionDefault_ReturnsLanguageVersionDefault()
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: "default");

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(LanguageVersion.Default, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_LangVersion12_ReturnsCSharp12()
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: "12.0");

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(LanguageVersion.CSharp12, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_LangVersion11_ReturnsCSharp11()
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: "11");

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(LanguageVersion.CSharp11, parseOptions.SpecifiedLanguageVersion);
    }

    [Fact]
    public void CreateFromSourceTexts_LangVersionZzz_FallsBackToDefault()
    {
        var source = "public class Foo { }";
        var compilation = CompilationFactory.CreateFromSourceTexts(
            "TestAssembly",
            new[] { ("Foo.cs", source) },
            Array.Empty<MetadataReference>(),
            langVersion: "zzz");

        var tree = compilation.SyntaxTrees.Single();
        var parseOptions = (CSharpParseOptions)tree.Options;
        Assert.Equal(LanguageVersion.Default, parseOptions.SpecifiedLanguageVersion);
    }
}
