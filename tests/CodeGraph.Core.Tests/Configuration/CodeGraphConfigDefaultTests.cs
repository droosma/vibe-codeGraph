using CodeGraph.Core.Configuration;

namespace CodeGraph.Core.Tests.Configuration;

public class CodeGraphConfigDefaultTests
{
    [Fact]
    public void Default_Solution_IsNull()
    {
        var config = new CodeGraphConfig();
        Assert.Null(config.Solution);
    }

    [Fact]
    public void Default_Output_IsCodegraph()
    {
        var config = new CodeGraphConfig();
        Assert.Equal(".codegraph", config.Output);
    }

    [Fact]
    public void Default_SplitBy_IsProject()
    {
        var config = new CodeGraphConfig();
        Assert.Equal("project", config.SplitBy);
    }

    [Fact]
    public void Default_Index_IsNotNull()
    {
        var config = new CodeGraphConfig();
        Assert.NotNull(config.Index);
    }

    [Fact]
    public void Default_Ioc_IsNotNull()
    {
        var config = new CodeGraphConfig();
        Assert.NotNull(config.Ioc);
    }

    [Fact]
    public void Default_Tests_IsNotNull()
    {
        var config = new CodeGraphConfig();
        Assert.NotNull(config.Tests);
    }

    [Fact]
    public void Default_Docs_IsNotNull()
    {
        var config = new CodeGraphConfig();
        Assert.NotNull(config.Docs);
    }

    [Fact]
    public void Default_Query_IsNotNull()
    {
        var config = new CodeGraphConfig();
        Assert.NotNull(config.Query);
    }
}

public class IndexConfigDefaultTests
{
    [Fact]
    public void Default_IncludeProjects_ContainsStar()
    {
        var config = new IndexConfig();
        Assert.Equal(new[] { "*" }, config.IncludeProjects);
    }

    [Fact]
    public void Default_ExcludeProjects_IsEmpty()
    {
        var config = new IndexConfig();
        Assert.Empty(config.ExcludeProjects);
    }

    [Fact]
    public void Default_IncludeExternalPackages_IsEmpty()
    {
        var config = new IndexConfig();
        Assert.Empty(config.IncludeExternalPackages);
    }

    [Fact]
    public void Default_ExcludeExternalPackages_HasDefaults()
    {
        var config = new IndexConfig();
        Assert.Equal(new[] { "Microsoft.*", "System.*" }, config.ExcludeExternalPackages);
    }

    [Fact]
    public void Default_MaxDepthForExternals_IsOne()
    {
        var config = new IndexConfig();
        Assert.Equal(1, config.MaxDepthForExternals);
    }

    [Fact]
    public void Default_Configuration_IsDebug()
    {
        var config = new IndexConfig();
        Assert.Equal("Debug", config.Configuration);
    }

    [Fact]
    public void Default_PreprocessorSymbols_IsEmpty()
    {
        var config = new IndexConfig();
        Assert.Empty(config.PreprocessorSymbols);
    }
}

public class IocConfigDefaultTests
{
    [Fact]
    public void Default_Enabled_IsTrue()
    {
        var config = new IocConfig();
        Assert.True(config.Enabled);
    }

    [Fact]
    public void Default_EntryPoints_IsEmpty()
    {
        var config = new IocConfig();
        Assert.Empty(config.EntryPoints);
    }

    [Fact]
    public void Default_AdditionalEntryPoints_IsEmpty()
    {
        var config = new IocConfig();
        Assert.Empty(config.AdditionalEntryPoints);
    }

    [Fact]
    public void Default_RegistrationMethodPatterns_HasDefaults()
    {
        var config = new IocConfig();
        Assert.Equal(new[] { "Add*", "Register*", "Bind*", "Map*" }, config.RegistrationMethodPatterns);
    }

    [Fact]
    public void Default_IgnoreMethodPatterns_HasDefaults()
    {
        var config = new IocConfig();
        Assert.Equal(new[] { "AddLogging", "AddOptions" }, config.IgnoreMethodPatterns);
    }

    [Fact]
    public void Default_InferSingleImplementations_IsTrue()
    {
        var config = new IocConfig();
        Assert.True(config.InferSingleImplementations);
    }

    [Fact]
    public void Default_ScanAssemblyRegistrations_IsTrue()
    {
        var config = new IocConfig();
        Assert.True(config.ScanAssemblyRegistrations);
    }

    [Fact]
    public void Default_FollowExtensionMethodDepth_IsOne()
    {
        var config = new IocConfig();
        Assert.Equal(1, config.FollowExtensionMethodDepth);
    }
}

public class TestConfigDefaultTests
{
    [Fact]
    public void Default_Enabled_IsTrue()
    {
        var config = new TestConfig();
        Assert.True(config.Enabled);
    }

    [Fact]
    public void Default_TestAttributePatterns_HasDefaults()
    {
        var config = new TestConfig();
        Assert.Equal(
            new[] { "*Fact", "*Theory", "*Test", "*TestCase", "*TestMethod" },
            config.TestAttributePatterns);
    }

    [Fact]
    public void Default_SetupAttributePatterns_HasDefaults()
    {
        var config = new TestConfig();
        Assert.Equal(
            new[] { "*SetUp", "*Initialize", "*ClassInitialize" },
            config.SetupAttributePatterns);
    }

    [Fact]
    public void Default_IncludeSetupMethods_IsTrue()
    {
        var config = new TestConfig();
        Assert.True(config.IncludeSetupMethods);
    }
}

public class DocConfigDefaultTests
{
    [Fact]
    public void Default_Enabled_IsTrue()
    {
        var config = new DocConfig();
        Assert.True(config.Enabled);
    }

    [Fact]
    public void Default_MarkdownDirs_HasDefaults()
    {
        var config = new DocConfig();
        Assert.Equal(new[] { "docs/", "README.md" }, config.MarkdownDirs);
    }

    [Fact]
    public void Default_IncludeXmlDocs_IsTrue()
    {
        var config = new DocConfig();
        Assert.True(config.IncludeXmlDocs);
    }
}

public class QueryConfigDefaultTests
{
    [Fact]
    public void Default_DefaultDepth_IsOne()
    {
        var config = new QueryConfig();
        Assert.Equal(1, config.DefaultDepth);
    }

    [Fact]
    public void Default_DefaultFormat_IsContext()
    {
        var config = new QueryConfig();
        Assert.Equal("context", config.DefaultFormat);
    }

    [Fact]
    public void Default_MaxNodes_IsFifty()
    {
        var config = new QueryConfig();
        Assert.Equal(50, config.MaxNodes);
    }
}
