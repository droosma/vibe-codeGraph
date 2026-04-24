using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

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

        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            return ParseSlnxContent(content);

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

    public static IReadOnlyList<SolutionProjectEntry> ParseSlnxContent(string content)
    {
        var entries = new List<SolutionProjectEntry>();

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return entries;
        }

        if (doc.Root is null)
            return entries;

        foreach (var project in doc.Root.Descendants("Project"))
        {
            var path = project.Attribute("Path")?.Value;
            if (string.IsNullOrEmpty(path))
                continue;

            // Skip folders (Type="Folder" or no .csproj extension)
            var type = project.Attribute("Type")?.Value;
            if (string.Equals(type, "Folder", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalizedPath = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            // Filter to C# projects only
            if (!normalizedPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = project.Attribute("Name")?.Value
                       ?? Path.GetFileNameWithoutExtension(normalizedPath);

            entries.Add(new SolutionProjectEntry(name, normalizedPath, string.Empty));
        }

        return entries;
    }
}
