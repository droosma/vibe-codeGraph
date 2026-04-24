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
        var parseOptions = new CSharpParseOptions(
            languageVersion: ParseLangVersion(langVersion),
            preprocessorSymbols: preprocessorSymbols);

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var file in sourceFiles)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Warning: Source file not found, skipping: {file}");
                continue;
            }

            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(
                text,
                parseOptions,
                path: file);
            syntaxTrees.Add(tree);
        }

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
                if (referenceCache != null)
                {
                    references.Add(referenceCache.GetOrCreate(dllPath));
                }
                else
                {
                    references.Add(MetadataReference.CreateFromFile(dllPath));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not load reference {dllPath}: {ex.Message}");
            }
        }

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullableEnabled
                ? NullableContextOptions.Enable
                : NullableContextOptions.Disable);

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
        var parseOptions = new CSharpParseOptions(
            languageVersion: ParseLangVersion(langVersion),
            preprocessorSymbols: preprocessorSymbols);

        var syntaxTrees = sources.Select(s =>
            CSharpSyntaxTree.ParseText(s.SourceText, parseOptions, path: s.FileName));

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: nullableEnabled
                ? NullableContextOptions.Enable
                : NullableContextOptions.Disable);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            compilationOptions);
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

        // Try parsing numeric versions like "12.0", "11"
        var cleaned = langVersion.Replace(".0", "");
        if (Enum.TryParse<LanguageVersion>($"CSharp{cleaned.Replace(".", "")}", true, out var parsed))
            return parsed;

        return LanguageVersion.Default;
    }
}
