using System.Text;
using System.Text.Json;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public enum TestImpactOutputFormat
{
    Context,
    Compact
}

public static class TestImpactFormatter
{
    public static string Format(TestImpactResult result, TestImpactOutputFormat format = TestImpactOutputFormat.Context)
    {
        return format switch
        {
            TestImpactOutputFormat.Compact => FormatCompact(result),
            _ => FormatContext(result)
        };
    }

    public static string FormatJson(TestImpactResult result, JsonSerializerOptions options)
    {
        var payload = new
        {
            pattern = result.Pattern,
            target = result.Target is not null
                ? new
                {
                    id = result.Target.Id,
                    name = result.Target.Name,
                    kind = result.Target.Kind.ToString().ToLowerInvariant(),
                    filePath = EmptyToNull(result.Target.FilePath)
                }
                : null,
            directTests = result.DirectTests.Select(CreateTestPayload),
            transitiveTests = result.IndirectTests.Select(CreateTestPayload),
            totalTests = result.DirectTests.Count + result.IndirectTests.Count,
            hasTests = result.HasTests,
            uncoveredCallers = result.UncoveredCallers.Select(c => new
            {
                callerId = c.Caller.Id,
                displayName = GetDisplayName(c.Caller),
                depth = c.Depth,
                filePath = EmptyToNull(c.Caller.FilePath)
            }),
            suggestedTestCommand = EmptyToNull(result.SuggestedTestCommand)
        };

        return JsonSerializer.Serialize(payload, options);
    }

    private static string FormatContext(TestImpactResult result)
    {
        var sb = new StringBuilder();

        if (result.Target is null)
        {
            sb.AppendLine($"No nodes found matching '{result.Pattern}'.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"Affected tests for {result.Target.Id}:");
        sb.AppendLine();

        if (!result.HasTests)
        {
            sb.AppendLine($"No affected tests found for {result.Target.Id}.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Direct coverage (tests that call this method):");
        AppendContextLines(sb, result.DirectTests, includeVia: false);
        sb.AppendLine();
        sb.AppendLine("Transitive (tests reaching through call chain):");
        AppendContextLines(sb, result.IndirectTests, includeVia: true);

        return sb.ToString().TrimEnd();
    }

    private static string FormatCompact(TestImpactResult result)
    {
        if (result.Target is null)
            return $"No nodes found matching '{result.Pattern}'.";

        if (!result.HasTests)
            return $"{result.Target.Id}{Environment.NewLine}no affected tests";

        var lines = new List<string> { result.Target.Id };
        lines.AddRange(result.DirectTests.Select(test => $"direct: {FormatCompactTest(test, includeVia: false)}"));
        lines.AddRange(result.IndirectTests.Select(test => $"transitive: {FormatCompactTest(test, includeVia: true)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendContextLines(StringBuilder sb, IEnumerable<TestCoverage> tests, bool includeVia)
    {
        var orderedTests = tests.OrderBy(test => GetDisplayName(test.TestNode), StringComparer.OrdinalIgnoreCase).ToList();
        if (orderedTests.Count == 0)
        {
            sb.AppendLine("  (none)");
            return;
        }

        foreach (var test in orderedTests)
        {
            sb.Append("  ");
            sb.Append(GetDisplayName(test.TestNode));

            if (!string.IsNullOrEmpty(test.TestNode.FilePath))
                sb.Append($" [{test.TestNode.FilePath}]");

            var via = includeVia ? FormatVia(test.PathFromTarget) : string.Empty;
            if (!string.IsNullOrEmpty(via))
                sb.Append($" via {via}");

            sb.AppendLine();
        }
    }

    private static string FormatCompactTest(TestCoverage test, bool includeVia)
    {
        var sb = new StringBuilder(GetDisplayName(test.TestNode));

        var via = includeVia ? FormatVia(test.PathFromTarget) : string.Empty;
        if (!string.IsNullOrEmpty(via))
            sb.Append($" via {via}");

        if (!string.IsNullOrEmpty(test.TestNode.FilePath))
            sb.Append($" [{test.TestNode.FilePath}]");

        return sb.ToString();
    }

    private static object CreateTestPayload(TestCoverage test)
    {
        var displayName = GetDisplayName(test.TestNode);
        var via = FormatVia(test.PathFromTarget, " -> ");
        return new
        {
            testId = test.TestNode.Id,
            displayName,
            filePath = EmptyToNull(test.TestNode.FilePath),
            path = test.PathFromTarget.Select(node => node.Id).ToList(),
            via = string.IsNullOrEmpty(via) ? displayName : via
        };
    }

    private static string FormatVia(IReadOnlyList<GraphNode> path, string separator = " → ")
    {
        if (path.Count <= 1)
            return string.Empty;

        return string.Join(separator, path.Select(GetDisplayName));
    }

    private static string GetDisplayName(GraphNode node)
    {
        if (node.Kind == NodeKind.Method)
        {
            if (!string.IsNullOrEmpty(node.ContainingTypeId))
                return $"{GetShortName(node.ContainingTypeId)}.{node.Name}";

            if (!string.IsNullOrEmpty(node.Id))
            {
                var parts = node.Id.Split('.');
                if (parts.Length >= 2)
                    return $"{parts[^2]}.{parts[^1]}";
            }
        }

        return !string.IsNullOrEmpty(node.Name) ? node.Name : GetShortName(node.Id);
    }

    private static string GetShortName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var parts = value.Split('.');
        return parts[^1];
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
