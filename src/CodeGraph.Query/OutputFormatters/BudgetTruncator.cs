namespace CodeGraph.Query.OutputFormatters;

/// <summary>
/// Truncates formatted output to fit within a token budget.
/// </summary>
public static class BudgetTruncator
{
    /// <summary>
    /// Estimates token count using ~4 chars per token heuristic.
    /// </summary>
    public static int EstimateTokens(string text) => (text.Length + 3) / 4;

    /// <summary>
    /// Truncates output to fit within the token budget, appending a continuation hint.
    /// </summary>
    public static string Apply(string output, int? budget)
    {
        if (budget is null || budget.Value <= 0)
            return output;

        var maxChars = budget.Value * 4;
        if (output.Length <= maxChars)
            return output;

        // Find a good break point (end of line)
        var truncateAt = output.LastIndexOf('\n', Math.Min(maxChars, output.Length - 1));
        if (truncateAt <= 0)
            truncateAt = maxChars;

        var truncated = output[..truncateAt];
        return truncated + "\n\n⚠ Output truncated at ~" + budget.Value + " token budget. " +
               "Use --depth 0 or add --project/--namespace filters to narrow results.";
    }
}
