using CodeGraph.Core.Models;

namespace CodeGraph.Query.Search;

/// <summary>
/// A single search result with its relevance score and the reasons it matched.
/// </summary>
public record SearchResult(GraphNode Node, double Score, IReadOnlyList<string> MatchReasons);

/// <summary>
/// Scores graph nodes against a query using token matching, synonym expansion,
/// and doc-comment search. Produces deterministically ordered results.
/// </summary>
public class SemanticSearchEngine
{
    private const double ExactNameScore = 10.0;
    private const double TokenInNameScore = 5.0;
    private const double TokenInDocCommentScore = 3.0;
    private const double SynonymMatchScore = 2.0;
    private const double TokenInNamespaceOrPathScore = 1.0;
    private const double TypeMethodBoost = 1.0;
    private const double ShortNameMaxBonus = 0.5;
    private const int ShortNameThreshold = 100;

    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, IReadOnlyList<string>> _tokenCache;

    public SemanticSearchEngine(Dictionary<string, GraphNode> nodes)
    {
        _nodes = nodes;

        // Pre-tokenize all nodes
        _tokenCache = new Dictionary<string, IReadOnlyList<string>>(nodes.Count);
        foreach (var (id, node) in nodes)
        {
            _tokenCache[id] = SearchTokenizer.TokenizeSymbol(node);
        }
    }

    /// <summary>
    /// Search for nodes matching the query. Returns results ordered by descending score.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(string query, int top = 20, NodeKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var queryTokens = SearchTokenizer.TokenizeQuery(query);
        if (queryTokens.Count == 0)
            return [];

        // Build the set of synonym tokens for each query token
        var synonymSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var qt in queryTokens)
        {
            var expanded = SynonymMap.Expand(qt);
            if (expanded.Count > 0)
                synonymSets[qt] = new HashSet<string>(expanded, StringComparer.OrdinalIgnoreCase);
        }

        var scored = new List<SearchResult>();

        foreach (var (id, node) in _nodes)
        {
            if (kindFilter.HasValue && node.Kind != kindFilter.Value)
                continue;

            var (score, reasons) = ScoreNode(node, query, queryTokens, synonymSets);
            if (score > 0)
                scored.Add(new SearchResult(node, score, reasons));
        }

        // Deterministic ordering: score desc, then type/method boost, then shorter name, then name alpha
        scored.Sort((a, b) =>
        {
            var cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;

            cmp = a.Node.Name.Length.CompareTo(b.Node.Name.Length);
            if (cmp != 0) return cmp;

            return string.Compare(a.Node.Name, b.Node.Name, StringComparison.OrdinalIgnoreCase);
        });

        if (scored.Count > top)
            scored = scored.GetRange(0, top);

        return scored;
    }

    private (double Score, List<string> Reasons) ScoreNode(
        GraphNode node,
        string rawQuery,
        IReadOnlyList<string> queryTokens,
        Dictionary<string, HashSet<string>> synonymSets)
    {
        double score = 0;
        var reasons = new List<string>();

        // Exact name match (case-insensitive)
        if (node.Name.Equals(rawQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactNameScore;
            reasons.Add("exact name match");
        }

        var symbolTokens = _tokenCache[node.Id];
        var nameTokens = GetNameTokens(node.Name);
        var docTokens = GetDocCommentTokens(node.DocComment);
        var nsPathTokens = GetNamespacePathTokens(node);

        foreach (var qt in queryTokens)
        {
            // Token match in name
            if (nameTokens.Contains(qt))
            {
                score += TokenInNameScore;
                reasons.Add($"token '{qt}' in name");
            }

            // Token match in doc comment
            if (docTokens.Contains(qt))
            {
                score += TokenInDocCommentScore;
                reasons.Add($"token '{qt}' in doc comment");
            }

            // Synonym match: check if any symbol token is a synonym of the query token
            if (synonymSets.TryGetValue(qt, out var synonyms))
            {
                foreach (var syn in synonyms)
                {
                    if (symbolTokens.Any(st => st.Equals(syn, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += SynonymMatchScore;
                        reasons.Add($"synonym '{syn}' of '{qt}'");
                        break; // count each synonym group once per query token
                    }
                }
            }

            // Token match in namespace/path
            if (nsPathTokens.Contains(qt))
            {
                score += TokenInNamespaceOrPathScore;
                reasons.Add($"token '{qt}' in namespace/path");
            }
        }

        if (score > 0)
        {
            // Kind boost: types and methods rank higher than fields/properties/events
            if (node.Kind is NodeKind.Type or NodeKind.Method)
            {
                score += TypeMethodBoost;
                reasons.Add("type/method boost");
            }

            // Shorter names get a small bonus (tie-breaker within same base score)
            var nameLen = Math.Min(node.Name.Length, ShortNameThreshold);
            score += ShortNameMaxBonus * (1.0 - (double)nameLen / ShortNameThreshold);
        }

        return (score, reasons);
    }

    private static HashSet<string> GetNameTokens(string name)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = SearchTokenizer.TokenizeQuery(name);
        foreach (var p in parts)
            tokens.Add(p);
        return tokens;
    }

    private static HashSet<string> GetDocCommentTokens(string? docComment)
    {
        if (string.IsNullOrEmpty(docComment))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Create a minimal node just to extract doc comment tokens
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allTokens = SearchTokenizer.TokenizeQuery(
            System.Text.RegularExpressions.Regex.Replace(docComment, @"<[^>]+>", " "));
        foreach (var t in allTokens)
            tokens.Add(t);
        return tokens;
    }

    private static HashSet<string> GetNamespacePathTokens(GraphNode node)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(node.ContainingNamespaceId))
        {
            foreach (var t in SearchTokenizer.TokenizeQuery(node.ContainingNamespaceId))
                tokens.Add(t);
        }

        if (!string.IsNullOrEmpty(node.FilePath))
        {
            foreach (var t in SearchTokenizer.TokenizeQuery(node.FilePath))
                tokens.Add(t);
        }

        return tokens;
    }
}
