using System.Xml.Linq;

namespace CodeGraph.Indexer.Workspace;

public record PackageRef(string Name, string? Version);

public record ProjectInfo
{
    public string ProjectPath { get; init; } = "";
    public string ProjectDirectory { get; init; } = "";
    public string AssemblyName { get; init; } = "";
    public string TargetFramework { get; init; } = "";
    public string RootNamespace { get; init; } = "";
    public string? LangVersion { get; init; }
    public bool NullableEnabled { get; init; }
    public bool IsSdkStyle { get; init; }
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProjectReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PackageRef> PackageReferences { get; init; } = Array.Empty<PackageRef>();
}

public static class ProjectParser
{
    private const string DefaultTargetFramework = "net10.0";
    private static readonly string[] s_knownProperties =
    {
        "TargetFramework",
        "TargetFrameworks",
        "AssemblyName",
        "RootNamespace",
        "LangVersion",
        "Nullable"
    };

    public static ProjectInfo Parse(string csprojPath)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project file not found: {fullPath}");

        var projectDir = Path.GetDirectoryName(fullPath)!;
        return ParseContent(File.ReadAllText(fullPath), fullPath, projectDir);
    }

    public static ProjectInfo ParseContent(string content, string projectPath, string projectDir)
    {
        var document = XDocument.Parse(content);
        var root = document.Root!;
        var ns = root.GetDefaultNamespace();
        var propertyGroups = root.Descendants(ns + "PropertyGroup");
        var inheritedProperties = LoadDirectoryBuildProps(projectDir);
        var centralPackageVersions = LoadDirectoryPackagesProps(projectDir);
        var properties = ReadEffectiveProperties(propertyGroups, ns, inheritedProperties);
        var projectFileName = Path.GetFileNameWithoutExtension(projectPath);
        var isSdkStyle = root.Attribute("Sdk") is not null;

        return new ProjectInfo
        {
            ProjectPath = projectPath,
            ProjectDirectory = projectDir,
            AssemblyName = GetPropertyOrDefault(properties, "AssemblyName", projectFileName),
            TargetFramework = ResolveTargetFramework(
                GetPropertyOrDefault(properties, "TargetFramework"),
                GetPropertyOrDefault(properties, "TargetFrameworks")),
            RootNamespace = GetPropertyOrDefault(properties, "RootNamespace", projectFileName),
            LangVersion = GetOptionalProperty(properties, "LangVersion"),
            NullableEnabled = string.Equals(
                GetPropertyOrDefault(properties, "Nullable"),
                "enable",
                StringComparison.OrdinalIgnoreCase),
            IsSdkStyle = isSdkStyle,
            SourceFiles = ResolveSourceFiles(root, ns, projectDir, isSdkStyle),
            ProjectReferences = GetProjectReferences(root, ns, projectDir),
            PackageReferences = GetPackageReferences(root, ns, centralPackageVersions)
        };
    }

    private static Dictionary<string, string?> ReadEffectiveProperties(
        IEnumerable<XElement> propertyGroups,
        XNamespace ns,
        IReadOnlyDictionary<string, string> inheritedProperties)
    {
        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var propertyName in s_knownProperties)
        {
            properties[propertyName] =
                GetProperty(propertyGroups, ns, propertyName)
                ?? inheritedProperties.GetValueOrDefault(propertyName);
        }

        return properties;
    }

    private static string GetPropertyOrDefault(
        IReadOnlyDictionary<string, string?> properties,
        string name,
        string defaultValue = "")
    {
        return GetOptionalProperty(properties, name) ?? defaultValue;
    }

    private static string? GetOptionalProperty(
        IReadOnlyDictionary<string, string?> properties,
        string name)
    {
        return properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> ResolveSourceFiles(
        XElement root,
        XNamespace ns,
        string projectDir,
        bool isSdkStyle)
    {
        return isSdkStyle
            ? GlobSourceFiles(projectDir)
            : ParseLegacySourceFiles(root, ns, projectDir);
    }

    private static IReadOnlyList<string> GetProjectReferences(XElement root, XNamespace ns, string projectDir)
    {
        return root.Descendants(ns + "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDir, value!)))
            .ToList();
    }

    private static IReadOnlyList<PackageRef> GetPackageReferences(
        XElement root,
        XNamespace ns,
        IReadOnlyDictionary<string, string> centralPackageVersions)
    {
        return root.Descendants(ns + "PackageReference")
            .Select(element => CreatePackageReference(element, ns, centralPackageVersions))
            .Where(static package => package is not null)
            .Select(static package => package!)
            .ToList();
    }

    private static PackageRef? CreatePackageReference(
        XElement element,
        XNamespace ns,
        IReadOnlyDictionary<string, string> centralPackageVersions)
    {
        var name = element.Attribute("Include")?.Value;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var version = element.Attribute("Version")?.Value
            ?? element.Element(ns + "Version")?.Value;

        if (version is null && centralPackageVersions.TryGetValue(name, out var centralVersion))
            version = centralVersion;

        return new PackageRef(name, version);
    }

    private static string ResolveTargetFramework(string? targetFramework, string? targetFrameworks)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
            return targetFramework.Trim();

        if (string.IsNullOrWhiteSpace(targetFrameworks))
            return DefaultTargetFramework;

        return targetFrameworks
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(ExtractVersion)
            .First();
    }

    private static double ExtractVersion(string tfm)
    {
        var digits = new string(tfm.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(
            digits,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var version)
            ? version
            : 0;
    }

    private static string? GetProperty(IEnumerable<XElement> propertyGroups, XNamespace ns, string name)
    {
        return propertyGroups
            .Elements(ns + name)
            .Select(element => element.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static IReadOnlyList<string> GlobSourceFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderBinDirectory(projectDir, file))
            .ToList();
    }

    private static bool IsUnderBinDirectory(string projectDir, string filePath)
    {
        var relativePath = Path.GetRelativePath(projectDir, filePath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseLegacySourceFiles(XElement root, XNamespace ns, string projectDir)
    {
        return root.Descendants(ns + "Compile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => value is not null && value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(value => Path.GetFullPath(Path.Combine(projectDir, value!)))
            .ToList();
    }

    private static Dictionary<string, string> LoadDirectoryBuildProps(string projectDir)
    {
        return LoadNearestFile(
            projectDir,
            "Directory.Build.props",
            static document => ReadBuildProperties(document));
    }

    private static Dictionary<string, string> LoadDirectoryPackagesProps(string projectDir)
    {
        return LoadNearestFile(
            projectDir,
            "Directory.Packages.props",
            static document => ReadCentralPackageVersions(document));
    }

    private static Dictionary<string, string> LoadNearestFile(
        string projectDir,
        string fileName,
        Func<XDocument, Dictionary<string, string>> parser)
    {
        var filePath = FindNearestFile(projectDir, fileName);
        if (filePath is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return parser(XDocument.Load(filePath));
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? FindNearestFile(string projectDir, string fileName)
    {
        for (var currentDir = projectDir; currentDir is not null; currentDir = Path.GetDirectoryName(currentDir))
        {
            var candidate = Path.Combine(currentDir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static Dictionary<string, string> ReadBuildProperties(XDocument document)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = document.Root;
        if (root is null)
            return properties;

        var ns = root.GetDefaultNamespace();
        foreach (var propertyGroup in root.Descendants(ns + "PropertyGroup"))
        {
            foreach (var element in propertyGroup.Elements())
            {
                var key = element.Name.LocalName;
                if (!properties.ContainsKey(key) && !string.IsNullOrWhiteSpace(element.Value))
                    properties[key] = element.Value;
            }
        }

        return properties;
    }

    private static Dictionary<string, string> ReadCentralPackageVersions(XDocument document)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = document.Root;
        if (root is null)
            return versions;

        var ns = root.GetDefaultNamespace();
        foreach (var packageVersion in root.Descendants(ns + "PackageVersion"))
        {
            var name = packageVersion.Attribute("Include")?.Value;
            var version = packageVersion.Attribute("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                versions.TryAdd(name, version);
        }

        return versions;
    }
}
