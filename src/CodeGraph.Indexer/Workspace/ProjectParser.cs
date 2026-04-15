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
    public static ProjectInfo Parse(string csprojPath)
    {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException($"Project file not found: {csprojPath}");

        var fullPath = Path.GetFullPath(csprojPath);
        var projectDir = Path.GetDirectoryName(fullPath)!;
        var content = File.ReadAllText(fullPath);

        return ParseContent(content, fullPath, projectDir);
    }

    public static ProjectInfo ParseContent(string content, string projectPath, string projectDir)
    {
        var doc = XDocument.Parse(content);
        var root = doc.Root!;
        var isSdkStyle = root.Attribute("Sdk") != null;

        // Collect properties from Directory.Build.props walking up
        var inheritedProps = LoadDirectoryBuildProps(projectDir);

        // Collect central package versions
        var centralVersions = LoadDirectoryPackagesProps(projectDir);

        var ns = root.GetDefaultNamespace();
        var propertyGroups = root.Descendants(ns + "PropertyGroup");

        string? tfm = GetProperty(propertyGroups, ns, "TargetFramework");
        string? tfms = GetProperty(propertyGroups, ns, "TargetFrameworks");
        string? assemblyName = GetProperty(propertyGroups, ns, "AssemblyName");
        string? rootNamespace = GetProperty(propertyGroups, ns, "RootNamespace");
        string? langVersion = GetProperty(propertyGroups, ns, "LangVersion");
        string? nullable = GetProperty(propertyGroups, ns, "Nullable");

        // Inherit from Directory.Build.props if not set locally
        tfm ??= inheritedProps.GetValueOrDefault("TargetFramework");
        tfms ??= inheritedProps.GetValueOrDefault("TargetFrameworks");
        assemblyName ??= inheritedProps.GetValueOrDefault("AssemblyName");
        rootNamespace ??= inheritedProps.GetValueOrDefault("RootNamespace");
        langVersion ??= inheritedProps.GetValueOrDefault("LangVersion");
        nullable ??= inheritedProps.GetValueOrDefault("Nullable");

        // Multi-targeting: pick highest version
        var targetFramework = ResolveTargetFramework(tfm, tfms);

        // Default assembly name / root namespace to project file name
        var projectFileName = Path.GetFileNameWithoutExtension(projectPath);
        assemblyName ??= projectFileName;
        rootNamespace ??= projectFileName;

        bool nullableEnabled = string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase);

        // Source files
        var sourceFiles = isSdkStyle
            ? GlobSourceFiles(projectDir)
            : ParseLegacySourceFiles(root, ns, projectDir);

        // Project references
        var projectRefs = root.Descendants(ns + "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v != null)
            .Select(v => Path.GetFullPath(Path.Combine(projectDir, v!)))
            .ToList();

        // Package references
        var packageRefs = root.Descendants(ns + "PackageReference")
            .Select(e =>
            {
                var name = e.Attribute("Include")?.Value ?? "";
                var version = e.Attribute("Version")?.Value
                    ?? e.Element(ns + "Version")?.Value;
                // Central package management fallback
                if (version == null && centralVersions.TryGetValue(name, out var centralVer))
                    version = centralVer;
                return new PackageRef(name, version);
            })
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToList();

        return new ProjectInfo
        {
            ProjectPath = projectPath,
            ProjectDirectory = projectDir,
            AssemblyName = assemblyName,
            TargetFramework = targetFramework,
            RootNamespace = rootNamespace,
            LangVersion = langVersion,
            NullableEnabled = nullableEnabled,
            IsSdkStyle = isSdkStyle,
            SourceFiles = sourceFiles,
            ProjectReferences = projectRefs,
            PackageReferences = packageRefs,
        };
    }

    private static string ResolveTargetFramework(string? tfm, string? tfms)
    {
        if (!string.IsNullOrWhiteSpace(tfm))
            return tfm.Trim();

        if (string.IsNullOrWhiteSpace(tfms))
            return "net8.0"; // sensible default

        var frameworks = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Sort descending by numeric version to pick highest
        return frameworks
            .OrderByDescending(f => ExtractVersion(f))
            .First();
    }

    private static double ExtractVersion(string tfm)
    {
        // e.g. "net8.0" -> 8.0, "net6.0" -> 6.0, "netstandard2.1" -> 2.1
        var digits = new string(tfm.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static string? GetProperty(IEnumerable<XElement> propertyGroups, XNamespace ns, string name)
    {
        return propertyGroups
            .Elements(ns + name)
            .Select(e => e.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static IReadOnlyList<string> GlobSourceFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir))
            return Array.Empty<string>();

        var files = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relative = Path.GetRelativePath(projectDir, f);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                // Exclude bin/ but keep obj/ generated files at deeper levels
                return !parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        return files;
    }

    private static IReadOnlyList<string> ParseLegacySourceFiles(XElement root, XNamespace ns, string projectDir)
    {
        return root.Descendants(ns + "Compile")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v != null && v.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(v => Path.GetFullPath(Path.Combine(projectDir, v!)))
            .ToList();
    }

    private static Dictionary<string, string> LoadDirectoryBuildProps(string projectDir)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = projectDir;

        while (dir != null)
        {
            var propsFile = Path.Combine(dir, "Directory.Build.props");
            if (File.Exists(propsFile))
            {
                try
                {
                    var doc = XDocument.Load(propsFile);
                    var ns = doc.Root!.GetDefaultNamespace();
                    foreach (var pg in doc.Root!.Descendants(ns + "PropertyGroup"))
                    {
                        foreach (var el in pg.Elements())
                        {
                            var key = el.Name.LocalName;
                            if (!props.ContainsKey(key) && !string.IsNullOrWhiteSpace(el.Value))
                                props[key] = el.Value;
                        }
                    }
                }
                catch { /* skip malformed props files */ }
                break; // only use nearest Directory.Build.props
            }

            dir = Path.GetDirectoryName(dir);
        }

        return props;
    }

    private static Dictionary<string, string> LoadDirectoryPackagesProps(string projectDir)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = projectDir;

        while (dir != null)
        {
            var propsFile = Path.Combine(dir, "Directory.Packages.props");
            if (File.Exists(propsFile))
            {
                try
                {
                    var doc = XDocument.Load(propsFile);
                    var ns = doc.Root!.GetDefaultNamespace();
                    foreach (var pv in doc.Root!.Descendants(ns + "PackageVersion"))
                    {
                        var name = pv.Attribute("Include")?.Value;
                        var ver = pv.Attribute("Version")?.Value;
                        if (name != null && ver != null)
                            versions.TryAdd(name, ver);
                    }
                }
                catch { /* skip malformed props files */ }
                break;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return versions;
    }
}
