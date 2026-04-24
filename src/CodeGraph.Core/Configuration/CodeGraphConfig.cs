namespace CodeGraph.Core.Configuration;

public class CodeGraphConfig
{
    public string? Solution { get; set; }
    public SolutionEntry[] Solutions { get; set; } = Array.Empty<SolutionEntry>();
    public string Output { get; set; } = ".codegraph";
    public string SplitBy { get; set; } = "project";

    public IndexConfig Index { get; set; } = new();
    public IocConfig Ioc { get; set; } = new();
    public TestConfig Tests { get; set; } = new();
    public DocConfig Docs { get; set; } = new();
    public QueryConfig Query { get; set; } = new();

    /// <summary>
    /// Returns the effective list of solutions. If <see cref="Solutions"/> is populated, returns those.
    /// If only <see cref="Solution"/> is set, returns a single-element list wrapping it.
    /// </summary>
    public SolutionEntry[] GetEffectiveSolutions()
    {
        if (Solutions.Length > 0)
            return Solutions;

        if (!string.IsNullOrEmpty(Solution))
            return new[] { new SolutionEntry { Path = Solution! } };

        return Array.Empty<SolutionEntry>();
    }
}

public class SolutionEntry
{
    public string Path { get; set; } = string.Empty;
    public IocConfig? Ioc { get; set; }
    public IndexConfig? Index { get; set; }
    public TestConfig? Tests { get; set; }
    public DocConfig? Docs { get; set; }
}

public class IndexConfig
{
    public string[] IncludeProjects { get; set; } = new[] { "*" };
    public string[] ExcludeProjects { get; set; } = Array.Empty<string>();
    public string[] IncludeExternalPackages { get; set; } = Array.Empty<string>();
    public string[] ExcludeExternalPackages { get; set; } = new[] { "Microsoft.*", "System.*" };
    public int MaxDepthForExternals { get; set; } = 1;
    public string Configuration { get; set; } = "Debug";
    public string[] PreprocessorSymbols { get; set; } = Array.Empty<string>();
}

public class IocConfig
{
    public bool Enabled { get; set; } = true;
    public string[] EntryPoints { get; set; } = Array.Empty<string>();
    public string[] AdditionalEntryPoints { get; set; } = Array.Empty<string>();
    public string[] RegistrationMethodPatterns { get; set; } = new[] { "Add*", "Register*", "Bind*", "Map*" };
    public string[] IgnoreMethodPatterns { get; set; } = new[] { "AddLogging", "AddOptions" };
    public bool InferSingleImplementations { get; set; } = true;
    public bool ScanAssemblyRegistrations { get; set; } = true;
    public int FollowExtensionMethodDepth { get; set; } = 1;
}

public class TestConfig
{
    public bool Enabled { get; set; } = true;
    public string[] TestAttributePatterns { get; set; } = new[] { "*Fact", "*Theory", "*Test", "*TestCase", "*TestMethod" };
    public string[] SetupAttributePatterns { get; set; } = new[] { "*SetUp", "*Initialize", "*ClassInitialize" };
    public bool IncludeSetupMethods { get; set; } = true;
}

public class DocConfig
{
    public bool Enabled { get; set; } = true;
    public string[] MarkdownDirs { get; set; } = new[] { "docs/", "README.md" };
    public bool IncludeXmlDocs { get; set; } = true;
}

public class QueryConfig
{
    public int DefaultDepth { get; set; } = 1;
    public string DefaultFormat { get; set; } = "context";
    public int MaxNodes { get; set; } = 50;
}
