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

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZeroish()
    {
        Assert.Equal(0, BudgetTruncator.EstimateTokens(""));
    }

    [Fact]
    public void EstimateTokens_SingleChar_ReturnsOne()
    {
        Assert.Equal(1, BudgetTruncator.EstimateTokens("a"));
    }

    [Fact]
    public void EstimateTokens_FourChars_ReturnsOne()
    {
        Assert.Equal(1, BudgetTruncator.EstimateTokens("abcd"));
    }

    [Fact]
    public void EstimateTokens_FiveChars_ReturnsTwo()
    {
        Assert.Equal(2, BudgetTruncator.EstimateTokens("abcde"));
    }

    [Fact]
    public void EstimateTokens_EightChars_ReturnsTwo()
    {
        Assert.Equal(2, BudgetTruncator.EstimateTokens("abcdefgh"));
    }

    [Fact]
    public void Apply_BudgetOfOne_TruncatesLongInput()
    {
        var input = "line1\nline2\nline3\nline4\nline5\nline6\nline7";
        var result = BudgetTruncator.Apply(input, 1);

        Assert.Contains("⚠", result);
        // The main content before the hint should be shorter than the input
        Assert.DoesNotContain("line7", result.Split("⚠")[0]);
    }

    [Fact]
    public void Apply_ExactBudgetFits_ReturnsUnchanged()
    {
        // 16 chars / 4 = 4 tokens; budget=4 should fit exactly
        var input = "0123456789abcdef";
        var result = BudgetTruncator.Apply(input, 4);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_VeryLargeBudget_ReturnsUnchanged()
    {
        var input = "some content\nwith lines\nhere";
        var result = BudgetTruncator.Apply(input, 999999);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_TruncatedOutput_ContainsBudgetValueInHint()
    {
        var input = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Line {i} content"));
        var result = BudgetTruncator.Apply(input, 15);

        Assert.Contains("15 token budget", result);
    }

    [Fact]
    public void Apply_TruncatedOutput_ContainsFilterSuggestion()
    {
        var input = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Line {i} content"));
        var result = BudgetTruncator.Apply(input, 15);

        Assert.Contains("--depth 0", result);
        Assert.Contains("--project", result);
        Assert.Contains("--namespace", result);
    }

    [Fact]
    public void Apply_NoNewlineInContent_TruncatesAtMaxChars()
    {
        // Content with no newlines at all
        var input = new string('x', 100);
        var result = BudgetTruncator.Apply(input, 2); // 8 chars max

        Assert.Contains("⚠", result);
        Assert.True(result.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }
}
