using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class BudgetTruncatorTests
{
    [Fact]
    public void EstimateTokens_ApproximatelyCorrect()
    {
        // "hello world" = 11 chars → ~3 tokens
        Assert.Equal(3, BudgetTruncator.EstimateTokens("hello world"));
    }

    [Fact]
    public void Apply_NullBudget_ReturnsUnchanged()
    {
        var input = "some output text";
        Assert.Equal(input, BudgetTruncator.Apply(input, null));
    }

    [Fact]
    public void Apply_WithinBudget_ReturnsUnchanged()
    {
        var input = "short"; // 5 chars = ~2 tokens
        Assert.Equal(input, BudgetTruncator.Apply(input, 100));
    }

    [Fact]
    public void Apply_ExceedsBudget_TruncatesWithHint()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i} with some content here"));
        var result = BudgetTruncator.Apply(input, 10); // very small budget

        Assert.Contains("\u26a0", result);
        Assert.Contains("truncated", result.ToLowerInvariant());
        Assert.True(result.Length < input.Length);
    }

    [Fact]
    public void Apply_TruncatesAtLineBreak()
    {
        var input = "line1\nline2\nline3\nline4\nline5";
        var result = BudgetTruncator.Apply(input, 3); // ~12 chars budget

        // Should end at a line boundary (before the truncation hint)
        var mainContent = result.Split("\u26a0")[0].TrimEnd();
        Assert.DoesNotContain("partial", mainContent);
    }

    [Fact]
    public void Apply_ZeroBudget_ReturnsUnchanged()
    {
        var input = "some text";
        Assert.Equal(input, BudgetTruncator.Apply(input, 0));
    }

    [Fact]
    public void Apply_NegativeBudget_ReturnsUnchanged()
    {
        var input = "some text";
        Assert.Equal(input, BudgetTruncator.Apply(input, -5));
    }
}
