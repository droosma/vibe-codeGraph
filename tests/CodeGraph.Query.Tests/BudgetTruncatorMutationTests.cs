using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

/// <summary>
/// Mutation-killing tests for BudgetTruncator.
/// Verifies exact calculations, boundary conditions, and truncation logic.
/// </summary>
public class BudgetTruncatorMutationTests
{
    #region EstimateTokens - exact formula: (text.Length + 3) / 4

    [Theory]
    [InlineData("", 0)]           // (0+3)/4 = 0
    [InlineData("a", 1)]          // (1+3)/4 = 1
    [InlineData("ab", 1)]         // (2+3)/4 = 1
    [InlineData("abc", 1)]        // (3+3)/4 = 1
    [InlineData("abcd", 1)]       // (4+3)/4 = 1
    [InlineData("abcde", 2)]      // (5+3)/4 = 2
    [InlineData("abcdef", 2)]     // (6+3)/4 = 2
    [InlineData("abcdefg", 2)]    // (7+3)/4 = 2
    [InlineData("abcdefgh", 2)]   // (8+3)/4 = 2
    [InlineData("abcdefghi", 3)]  // (9+3)/4 = 3
    public void EstimateTokens_ExactFormula(string input, int expected)
    {
        Assert.Equal(expected, BudgetTruncator.EstimateTokens(input));
    }

    [Fact]
    public void EstimateTokens_LargeInput_CorrectCalculation()
    {
        var input = new string('x', 100);
        // (100 + 3) / 4 = 25
        Assert.Equal(25, BudgetTruncator.EstimateTokens(input));
    }

    [Fact]
    public void EstimateTokens_Formula_Uses3NotOtherConstant()
    {
        // Verify the +3 in the formula by checking boundary
        // With length 5: (5+3)/4=2, but (5+2)/4=1, (5+4)/4=2
        Assert.Equal(2, BudgetTruncator.EstimateTokens("12345"));
        // With length 4: (4+3)/4=1
        Assert.Equal(1, BudgetTruncator.EstimateTokens("1234"));
    }

    #endregion

    #region Apply - null/zero/negative budget returns unchanged

    [Fact]
    public void Apply_NullBudget_ReturnsExactInput()
    {
        var input = "test content\nwith lines";
        var result = BudgetTruncator.Apply(input, null);
        Assert.Same(input, result);
    }

    [Fact]
    public void Apply_ZeroBudget_ReturnsExactInput()
    {
        var input = "some text";
        var result = BudgetTruncator.Apply(input, 0);
        Assert.Same(input, result);
    }

    [Fact]
    public void Apply_NegativeBudget_ReturnsExactInput()
    {
        var input = "some text";
        var result = BudgetTruncator.Apply(input, -10);
        Assert.Same(input, result);
    }

    #endregion

    #region Apply - within budget returns unchanged

    [Fact]
    public void Apply_ExactlyAtBudget_ReturnsUnchanged()
    {
        // 20 chars, budget=5 → maxChars=20, length==maxChars → no truncation
        var input = new string('a', 20);
        var result = BudgetTruncator.Apply(input, 5);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_OneBelowBudget_ReturnsUnchanged()
    {
        // 19 chars, budget=5 → maxChars=20, 19<=20 → no truncation
        var input = new string('a', 19);
        var result = BudgetTruncator.Apply(input, 5);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_OneAboveBudget_Truncates()
    {
        // 21 chars, budget=5 → maxChars=20, 21>20 → truncation
        var input = "line1\nline2\nline3\nend"; // 20 chars total
        var padded = input + "X"; // 21 chars
        var result = BudgetTruncator.Apply(padded, 5);
        Assert.Contains("⚠", result);
    }

    #endregion

    #region Apply - truncation at newline boundary

    [Fact]
    public void Apply_TruncatesAtLastNewlineBeforeMax()
    {
        // budget=3 → maxChars=12
        // "abcde\nfghij\nklmno\npqrst"
        // LastIndexOf('\n', min(12, len-1)) searches backward from index 12
        var input = "abcde\nfghij\nklmno\npqrst";
        var result = BudgetTruncator.Apply(input, 3);

        // Content before the warning should end at a newline boundary
        var warningIdx = result.IndexOf("\n\n⚠");
        var content = result[..warningIdx];
        // Content should be "abcde\nfghij" (last \n before index 12 is at index 11)
        Assert.Equal("abcde\nfghij", content);
    }

    [Fact]
    public void Apply_NoNewlineInRange_TruncatesAtMaxChars()
    {
        // Content with no newlines
        var input = new string('x', 50);
        var result = BudgetTruncator.Apply(input, 2); // maxChars=8

        var warningIdx = result.IndexOf("\n\n⚠");
        var content = result[..warningIdx];
        Assert.Equal(8, content.Length);
    }

    [Fact]
    public void Apply_NewlineAtPositionZero_UsesMaxChars()
    {
        // Only newline is at position 0 → LastIndexOf finds it at 0 → truncateAt <= 0 → use maxChars
        var input = "\n" + new string('x', 50);
        var result = BudgetTruncator.Apply(input, 2); // maxChars=8

        var warningIdx = result.IndexOf("\n\n⚠");
        var content = result[..warningIdx];
        Assert.Equal(8, content.Length);
    }

    #endregion

    #region Apply - truncation hint format

    [Fact]
    public void Apply_TruncationHint_ContainsBudgetValue()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}"));
        var result = BudgetTruncator.Apply(input, 7);

        Assert.Contains("7 token budget", result);
    }

    [Fact]
    public void Apply_TruncationHint_ContainsWarningSymbol()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}"));
        var result = BudgetTruncator.Apply(input, 7);

        Assert.Contains("⚠ Output truncated at ~", result);
    }

    [Fact]
    public void Apply_TruncationHint_ContainsDepthSuggestion()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}"));
        var result = BudgetTruncator.Apply(input, 7);

        Assert.Contains("--depth 0", result);
    }

    [Fact]
    public void Apply_TruncationHint_ContainsProjectSuggestion()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}"));
        var result = BudgetTruncator.Apply(input, 7);

        Assert.Contains("--project", result);
    }

    [Fact]
    public void Apply_TruncationHint_ContainsNamespaceSuggestion()
    {
        var input = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i}"));
        var result = BudgetTruncator.Apply(input, 7);

        Assert.Contains("--namespace", result);
    }

    [Fact]
    public void Apply_TruncationHint_ExactFormat()
    {
        var input = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10";
        var result = BudgetTruncator.Apply(input, 2); // maxChars=8

        // The hint comes after the content
        Assert.Contains("\n\n⚠ Output truncated at ~2 token budget. Use --depth 0 or add --project/--namespace filters to narrow results.", result);
    }

    #endregion

    #region Apply - maxChars calculation uses budget * 4

    [Fact]
    public void Apply_BudgetMultipliedByFour()
    {
        // budget=10 → maxChars=40
        var input = new string('a', 40); // exactly 40 chars, should not truncate
        var result = BudgetTruncator.Apply(input, 10);
        Assert.Equal(input, result);

        // 41 chars should truncate
        var inputLonger = new string('a', 41);
        var resultLonger = BudgetTruncator.Apply(inputLonger, 10);
        Assert.Contains("⚠", resultLonger);
    }

    #endregion

    #region Apply - truncated content is strictly shorter

    [Fact]
    public void Apply_TruncatedContent_ShorterThanOriginal()
    {
        var input = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Line {i} with more text"));
        var result = BudgetTruncator.Apply(input, 5);

        // The truncated portion (before the hint) should be shorter than input
        var hintStart = result.IndexOf("\n\n⚠");
        Assert.True(hintStart < input.Length);
    }

    #endregion
}
