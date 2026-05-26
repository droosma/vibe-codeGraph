using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Workspace;

public static class CompilationFactory
{
    public static CSharpCompilation Create(
        string assemblyName,
        IEnumerable<string> sourceFiles,
        IEnumerable<string> referenceDllPaths,
        string? langVersion = null,
        bool nullableEnabled = true,
        string[]? preprocessorSymbols = null,
        MetadataReferenceCache? referenceCache = null)
    {
        var parseOptions = CreateParseOptions(langVersion, preprocessorSymbols);
        var syntaxTrees = LoadSyntaxTrees(sourceFiles, parseOptions);
        var references = LoadMetadataReferences(referenceDllPaths, referenceCache);
        var compilationOptions = CreateCompilationOptions(nullableEnabled);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            compilationOptions);
    }

    public static CSharpCompilation CreateFromSourceTexts(
        string assemblyName,
        IEnumerable<(string FileName, string SourceText)> sources,
        IEnumerable<MetadataReference> references,
        string? langVersion = null,
        bool nullableEnabled = true,
        string[]? preprocessorSymbols = null)
    {
        var parseOptions = CreateParseOptions(langVersion, preprocessorSymbols);
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.SourceText, parseOptions, path: source.FileName));

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            CreateCompilationOptions(nullableEnabled));
    }

    private static CSharpParseOptions CreateParseOptions(string? langVersion, string[]? preprocessorSymbols)
    {
        return new CSharpParseOptions(
            languageVersion: ParseLangVersion(langVersion),
            preprocessorSymbols: preprocessorSymbols);
    }

    private static CSharpCompilationOptions CreateCompilationOptions(bool nullableEnabled)
    {
        return new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullableEnabled
                ? NullableContextOptions.Enable
                : NullableContextOptions.Disable);
    }

    private static List<SyntaxTree> LoadSyntaxTrees(
        IEnumerable<string> sourceFiles,
        CSharpParseOptions parseOptions)
    {
        var syntaxTrees = new List<SyntaxTree>();

        foreach (var sourceFile in sourceFiles)
        {
            if (!File.Exists(sourceFile))
            {
                Console.Error.WriteLine($"Warning: Source file not found, skipping: {sourceFile}");
                continue;
            }

            var sourceText = File.ReadAllText(sourceFile);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(sourceText, parseOptions, path: sourceFile));
        }

        return syntaxTrees;
    }

    private static List<MetadataReference> LoadMetadataReferences(
        IEnumerable<string> referenceDllPaths,
        MetadataReferenceCache? referenceCache)
    {
        var references = new List<MetadataReference>();

        foreach (var dllPath in referenceDllPaths)
        {
            if (!File.Exists(dllPath))
            {
                Console.Error.WriteLine($"Warning: Reference DLL not found, skipping: {dllPath}");
                continue;
            }

            try
            {
                references.Add(referenceCache is null
                    ? MetadataReference.CreateFromFile(dllPath)
                    : referenceCache.GetOrCreate(dllPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not load reference {dllPath}: {ex.Message}");
            }
        }

        return references;
    }

    private static LanguageVersion ParseLangVersion(string? langVersion)
    {
        if (string.IsNullOrWhiteSpace(langVersion))
            return LanguageVersion.Default;

        if (langVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return LanguageVersion.Latest;

        if (langVersion.Equals("preview", StringComparison.OrdinalIgnoreCase))
            return LanguageVersion.Preview;

        if (langVersion.Equals("default", StringComparison.OrdinalIgnoreCase))
            return LanguageVersion.Default;

        var cleanedVersion = langVersion.Replace(".0", "", StringComparison.Ordinal);
        return Enum.TryParse<LanguageVersion>(
            $"CSharp{cleanedVersion.Replace(".", "", StringComparison.Ordinal)}",
            ignoreCase: true,
            out var parsed)
            ? parsed
            : LanguageVersion.Default;
    }
}
