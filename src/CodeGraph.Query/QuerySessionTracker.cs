namespace CodeGraph.Query;

/// <summary>
/// Tracks queries within a session to enable context-aware suggestions.
/// </summary>
public class QuerySessionTracker
{
    private readonly List<(string Pattern, int Depth, int ResultCount)> _history = new();

    public void Record(string pattern, int depth, int resultCount)
    {
        _history.Add((pattern, depth, resultCount));
    }

    public bool WasQueried(string pattern)
    {
        return _history.Any(h => h.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public bool WasQueriedAtDepth(string pattern, int depth)
    {
        return _history.Any(h =>
            h.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase) && h.Depth >= depth);
    }

    public IReadOnlyList<string> GetQueriedPatterns()
    {
        return _history.Select(h => h.Pattern).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int QueryCount => _history.Count;
}
