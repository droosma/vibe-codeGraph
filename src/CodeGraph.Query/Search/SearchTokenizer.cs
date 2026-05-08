using System.Text.RegularExpressions;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Search;

/// <summary>
/// Splits symbol names, paths, and doc comments into searchable tokens.
/// </summary>
public static partial class SearchTokenizer
{
    private static readonly char[] Separators = ['.', '/', '\\', '_', '-', ' ', ',', ';', ':', '(', ')', '<', '>', '{', '}', '[', ']'];

    /// <summary>
    /// Tokenize all searchable fields of a <see cref="GraphNode"/>.
    /// Returns distinct lowercase tokens from the name, ID, namespace, file path, and doc comment.
    /// </summary>
    public static IReadOnlyList<string> TokenizeSymbol(GraphNode node)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        AddPascalCaseTokens(tokens, node.Name);
        AddSeparatorTokens(tokens, node.Id);
        AddSeparatorTokens(tokens, node.ContainingNamespaceId);
        AddPathTokens(tokens, node.FilePath);
        AddDocCommentTokens(tokens, node.DocComment);

        tokens.Remove(string.Empty);
        return tokens.ToList();
    }

    /// <summary>
    /// Tokenize a user query string into lowercase search tokens.
    /// </summary>
    public static IReadOnlyList<string> TokenizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var tokens = new HashSet<string>(StringComparer.Ordinal);

        // Split on whitespace first, then handle each word
        var words = query.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            AddPascalCaseTokens(tokens, word);
        }

        tokens.Remove(string.Empty);
        return tokens.ToList();
    }

    private static void AddPascalCaseTokens(HashSet<string> tokens, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        // Add the full value as a token
        tokens.Add(value.ToLowerInvariant());

        // Split PascalCase / camelCase
        var parts = PascalCaseRegex().Split(value);
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
                tokens.Add(part.ToLowerInvariant());
        }
    }

    private static void AddSeparatorTokens(HashSet<string> tokens, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var parts = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            AddPascalCaseTokens(tokens, part);
        }
    }

    private static void AddPathTokens(HashSet<string> tokens, string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        // Extract meaningful segments from the path (skip extension)
        var withoutExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrEmpty(withoutExt))
            AddPascalCaseTokens(tokens, withoutExt);

        // Also split the full path on separators
        AddSeparatorTokens(tokens, filePath);
    }

    private static void AddDocCommentTokens(HashSet<string> tokens, string? docComment)
    {
        if (string.IsNullOrEmpty(docComment))
            return;

        // Strip XML tags
        var text = XmlTagRegex().Replace(docComment, " ");

        // Split into words
        var words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            // Skip very short noise words
            if (lower.Length >= 2)
                tokens.Add(lower);
        }
    }

    // Splits on transitions: lowercase→uppercase, uppercase→uppercase+lowercase (acronyms),
    // and letter→digit or digit→letter boundaries.
    // Examples:
    //   "OrderService" → ["Order", "Service"]
    //   "HTTPClient"   → ["HTTP", "Client"]
    //   "getItem2"     → ["get", "Item", "2"]
    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[a-zA-Z])(?=[0-9])|(?<=[0-9])(?=[a-zA-Z])")]
    private static partial Regex PascalCaseRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex XmlTagRegex();
}
