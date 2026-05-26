using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CodeGraph.Indexer.Workspace;

public record SolutionProjectEntry(string Name, string RelativePath, string ProjectGuid);

public static class SolutionParser
{
    private static readonly Regex ProjectLineRegex = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<SolutionProjectEntry> Parse(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var content = File.ReadAllText(solutionPath);
        return solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnxContent(content)
            : ParseContent(content);
    }

    public static IReadOnlyList<SolutionProjectEntry> ParseContent(string content)
    {
        var entries = new List<SolutionProjectEntry>();

        foreach (Match match in ProjectLineRegex.Matches(content))
        {
            var relativePath = NormalizePath(match.Groups[2].Value);
            if (!IsCSharpProject(relativePath))
                continue;

            entries.Add(new SolutionProjectEntry(
                match.Groups[1].Value,
                relativePath,
                match.Groups[3].Value));
        }

        return entries;
    }

    public static IReadOnlyList<SolutionProjectEntry> ParseSlnxContent(string content)
    {
        if (!TryParseDocument(content, out var document) || document.Root is null)
            return Array.Empty<SolutionProjectEntry>();

        return document.Root
            .Descendants("Project")
            .Select(CreateProjectEntry)
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .ToList();
    }

    private static bool TryParseDocument(string content, out XDocument document)
    {
        try
        {
            document = XDocument.Parse(content);
            return true;
        }
        catch (XmlException)
        {
            document = new XDocument();
            return false;
        }
    }

    private static SolutionProjectEntry? CreateProjectEntry(XElement projectElement)
    {
        var path = projectElement.Attribute("Path")?.Value;
        if (string.IsNullOrEmpty(path))
            return null;

        if (string.Equals(projectElement.Attribute("Type")?.Value, "Folder", StringComparison.OrdinalIgnoreCase))
            return null;

        var normalizedPath = NormalizePath(path);
        if (!IsCSharpProject(normalizedPath))
            return null;

        var name = projectElement.Attribute("Name")?.Value
                   ?? Path.GetFileNameWithoutExtension(normalizedPath);

        return new SolutionProjectEntry(name, normalizedPath, string.Empty);
    }

    private static bool IsCSharpProject(string path)
    {
        return path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }
}
