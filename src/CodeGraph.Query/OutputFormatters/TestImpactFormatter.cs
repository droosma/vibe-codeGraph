using System.Text;

namespace CodeGraph.Query.OutputFormatters;

public static class TestImpactFormatter
{
    public static string Format(TestImpactResult result)
    {
        var sb = new StringBuilder();

        if (result.Target is null)
        {
            sb.AppendLine($"No nodes found matching '{result.Pattern}'.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"# Test impact for {result.Target.Id}");
        sb.AppendLine();

        sb.AppendLine($"## Direct coverage ({result.DirectTests.Count} test{Pluralize(result.DirectTests.Count)})");
        if (result.DirectTests.Count == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var test in result.DirectTests)
                sb.AppendLine($"  \u2713 {test.TestNode.Id}");
        sb.AppendLine();

        sb.AppendLine($"## Indirect coverage ({result.IndirectTests.Count} test{Pluralize(result.IndirectTests.Count)})");
        if (result.IndirectTests.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var test in result.IndirectTests)
            {
                sb.AppendLine($"  \u26a0 {test.TestNode.Id}");
                if (test.PathFromTarget.Count > 0)
                {
                    var via = string.Join(" \u2192 ", test.PathFromTarget.Select(n => n.Id));
                    sb.AppendLine($"    via: {via}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine($"## Uncovered callers ({result.UncoveredCallers.Count} path{Pluralize(result.UncoveredCallers.Count)})");
        if (result.UncoveredCallers.Count == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var caller in result.UncoveredCallers)
                sb.AppendLine($"  \u2717 {caller.Caller.Id} [depth: {caller.Depth}]");
        sb.AppendLine();

        sb.AppendLine("## Suggested test command");
        if (string.IsNullOrEmpty(result.SuggestedTestCommand))
            sb.AppendLine("  (no tests found)");
        else
            sb.AppendLine(result.SuggestedTestCommand);

        return sb.ToString().TrimEnd();
    }

    private static string Pluralize(int count) => count == 1 ? "" : "s";
}
