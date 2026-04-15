using System.Text.RegularExpressions;

namespace CodeGraph.Indexer.Workspace;

public record SolutionProjectEntry(string Name, string RelativePath, string ProjectGuid);

public static class SolutionParser
{
    // Matches: Project("{TypeGuid}") = "Name", "Path", "{ProjectGuid}"
    private static readonly Regex ProjectLineRegex = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<SolutionProjectEntry> Parse(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var content = File.ReadAllText(solutionPath);
        return ParseContent(content);
    }

    public static IReadOnlyList<SolutionProjectEntry> ParseContent(string content)
    {
        var entries = new List<SolutionProjectEntry>();

        foreach (Match match in ProjectLineRegex.Matches(content))
        {
            var name = match.Groups[1].Value;
            var relativePath = match.Groups[2].Value
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var guid = match.Groups[3].Value;

            // Filter to C# projects only
            if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new SolutionProjectEntry(name, relativePath, guid));
            }
        }

        return entries;
    }
}
