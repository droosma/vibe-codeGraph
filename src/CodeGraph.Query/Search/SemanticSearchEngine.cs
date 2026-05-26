using System.Text.RegularExpressions;
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

    private static readonly Regex XmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, IReadOnlyList<string>> _tokenCache;

    public SemanticSearchEngine(Dictionary<string, GraphNode> nodes)
    {
        _nodes = nodes;

        _tokenCache = new Dictionary<string, IReadOnlyList<string>>(nodes.Count);
        foreach (var (id, node) in nodes)
            _tokenCache[id] = SearchTokenizer.TokenizeSymbol(node);
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

        var synonymSets = BuildSynonymSets(queryTokens);
        var scoredResults = new List<SearchResult>();

        foreach (var node in EnumerateCandidates(kindFilter))
        {
            var (score, reasons) = ScoreNode(node, query, queryTokens, synonymSets);
            if (score > 0)
                scoredResults.Add(new SearchResult(node, score, reasons));
        }

        SortResults(scoredResults);
        if (scoredResults.Count > top)
            scoredResults = scoredResults.GetRange(0, top);

        return scoredResults;
    }

    private IEnumerable<GraphNode> EnumerateCandidates(NodeKind? kindFilter)
    {
        IEnumerable<GraphNode> candidates = _nodes.Values;
        if (kindFilter.HasValue)
            candidates = candidates.Where(node => node.Kind == kindFilter.Value);

        return candidates;
    }

    private static Dictionary<string, HashSet<string>> BuildSynonymSets(IReadOnlyList<string> queryTokens)
    {
        var synonymSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var queryToken in queryTokens)
        {
            var expanded = SynonymMap.Expand(queryToken);
            if (expanded.Count > 0)
                synonymSets[queryToken] = new HashSet<string>(expanded, StringComparer.OrdinalIgnoreCase);
        }

        return synonymSets;
    }

    private (double Score, List<string> Reasons) ScoreNode(
        GraphNode node,
        string rawQuery,
        IReadOnlyList<string> queryTokens,
        Dictionary<string, HashSet<string>> synonymSets)
    {
        var score = 0.0;
        var reasons = new List<string>();

        if (node.Name.Equals(rawQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactNameScore;
            reasons.Add("exact name match");
        }

        var symbolTokens = _tokenCache[node.Id];
        var nameTokens = GetNameTokens(node.Name);
        var docTokens = GetDocCommentTokens(node.DocComment);
        var namespacePathTokens = GetNamespacePathTokens(node);

        foreach (var queryToken in queryTokens)
        {
            score += ScoreQueryToken(
                queryToken,
                symbolTokens,
                nameTokens,
                docTokens,
                namespacePathTokens,
                synonymSets,
                reasons);
        }

        if (score > 0)
            ApplyResultBoosts(node, ref score, reasons);

        return (score, reasons);
    }

    private static double ScoreQueryToken(
        string queryToken,
        IReadOnlyList<string> symbolTokens,
        HashSet<string> nameTokens,
        HashSet<string> docTokens,
        HashSet<string> namespacePathTokens,
        Dictionary<string, HashSet<string>> synonymSets,
        List<string> reasons)
    {
        var score = 0.0;

        if (nameTokens.Contains(queryToken))
        {
            score += TokenInNameScore;
            reasons.Add($"token '{queryToken}' in name");
        }

        if (docTokens.Contains(queryToken))
        {
            score += TokenInDocCommentScore;
            reasons.Add($"token '{queryToken}' in doc comment");
        }

        if (TryFindSynonymMatch(queryToken, synonymSets, symbolTokens, out var synonym))
        {
            score += SynonymMatchScore;
            reasons.Add($"synonym '{synonym}' of '{queryToken}'");
        }

        if (namespacePathTokens.Contains(queryToken))
        {
            score += TokenInNamespaceOrPathScore;
            reasons.Add($"token '{queryToken}' in namespace/path");
        }

        return score;
    }

    private static bool TryFindSynonymMatch(
        string queryToken,
        Dictionary<string, HashSet<string>> synonymSets,
        IReadOnlyList<string> symbolTokens,
        out string synonym)
    {
        synonym = string.Empty;
        if (!synonymSets.TryGetValue(queryToken, out var synonyms))
            return false;

        foreach (var candidateSynonym in synonyms)
        {
            if (symbolTokens.Any(symbolToken => symbolToken.Equals(candidateSynonym, StringComparison.OrdinalIgnoreCase)))
            {
                synonym = candidateSynonym;
                return true;
            }
        }

        return false;
    }

    private static void ApplyResultBoosts(GraphNode node, ref double score, List<string> reasons)
    {
        if (node.Kind is NodeKind.Type or NodeKind.Method)
        {
            score += TypeMethodBoost;
            reasons.Add("type/method boost");
        }

        var nameLength = Math.Min(node.Name.Length, ShortNameThreshold);
        score += ShortNameMaxBonus * (1.0 - (double)nameLength / ShortNameThreshold);
    }

    private static void SortResults(List<SearchResult> results)
    {
        results.Sort((left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
                return scoreComparison;

            var nameLengthComparison = left.Node.Name.Length.CompareTo(right.Node.Name.Length);
            if (nameLengthComparison != 0)
                return nameLengthComparison;

            return string.Compare(left.Node.Name, right.Node.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static HashSet<string> GetNameTokens(string name)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in SearchTokenizer.TokenizeQuery(name))
            tokens.Add(token);

        return tokens;
    }

    private static HashSet<string> GetDocCommentTokens(string? docComment)
    {
        if (string.IsNullOrEmpty(docComment))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = XmlTagPattern.Replace(docComment, " ");
        foreach (var token in SearchTokenizer.TokenizeQuery(text))
            tokens.Add(token);

        return tokens;
    }

    private static HashSet<string> GetNamespacePathTokens(GraphNode node)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(node.ContainingNamespaceId))
        {
            foreach (var token in SearchTokenizer.TokenizeQuery(node.ContainingNamespaceId))
                tokens.Add(token);
        }

        if (!string.IsNullOrEmpty(node.FilePath))
        {
            foreach (var token in SearchTokenizer.TokenizeQuery(node.FilePath))
                tokens.Add(token);
        }

        return tokens;
    }
}
