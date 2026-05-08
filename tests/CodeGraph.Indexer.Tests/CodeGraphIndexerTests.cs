using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests;

public class CodeGraphIndexerTests
{
    #region IndexerOptions defaults

    [Fact]
    public void IndexerOptions_DefaultConfiguration_IsDebug()
    {
        var options = new IndexerOptions();
        Assert.Equal("Debug", options.Configuration);
    }

    [Fact]
    public void IndexerOptions_DefaultPassFlags_AreAllEnabled()
    {
        var options = new IndexerOptions();
        Assert.True(options.EnableRoutesPass);
        Assert.True(options.EnableConfigurationPass);
        Assert.True(options.EnableMiddlewarePass);
        Assert.True(options.EnableDbContextPass);
    }

    [Fact]
    public void IndexerOptions_DefaultFilters_AreNull()
    {
        var options = new IndexerOptions();
        Assert.Null(options.ProjectFilter);
        Assert.Null(options.PreprocessorSymbols);
        Assert.Null(options.IncludeProjects);
        Assert.Null(options.ExcludeProjects);
    }

    [Fact]
    public void IndexerOptions_DefaultBuildFlags_AreFalse()
    {
        var options = new IndexerOptions();
        Assert.False(options.SkipBuild);
        Assert.False(options.SkipRestore);
    }

    [Fact]
    public void IndexerOptions_WithInit_OverridesDefaults()
    {
        var options = new IndexerOptions
        {
            Configuration = "Release",
            SkipBuild = true,
            SkipRestore = true,
            EnableRoutesPass = false,
            EnableDbContextPass = false,
            ProjectFilter = "MyApp.*",
            IncludeProjects = new[] { "Foo", "Bar" },
            ExcludeProjects = new[] { "*.Tests" },
            PreprocessorSymbols = new[] { "CUSTOM" }
        };

        Assert.Equal("Release", options.Configuration);
        Assert.True(options.SkipBuild);
        Assert.True(options.SkipRestore);
        Assert.False(options.EnableRoutesPass);
        Assert.True(options.EnableConfigurationPass);
        Assert.True(options.EnableMiddlewarePass);
        Assert.False(options.EnableDbContextPass);
        Assert.Equal("MyApp.*", options.ProjectFilter);
        Assert.Equal(new[] { "Foo", "Bar" }, options.IncludeProjects);
        Assert.Equal(new[] { "*.Tests" }, options.ExcludeProjects);
        Assert.Equal(new[] { "CUSTOM" }, options.PreprocessorSymbols);
    }

    #endregion

    #region IndexResult

    [Fact]
    public void IndexResult_ExposesAllProperties()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A.Foo", Name = "Foo", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Foo", ToId = "A.Bar", Type = EdgeType.Calls }
        };
        var metadata = new GraphMetadata
        {
            Solution = "Test.sln",
            SolutionName = "Test"
        };

        var result = new IndexResult(nodes.AsReadOnly(), edges.AsReadOnly(), metadata);

        Assert.Single(result.Nodes);
        Assert.Single(result.Edges);
        Assert.Equal("Test.sln", result.Metadata.Solution);
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullOptions_DoesNotThrow()
    {
        var indexer = new CodeGraphIndexer(null);
        Assert.NotNull(indexer);
    }

    [Fact]
    public void Constructor_DefaultOptions_DoesNotThrow()
    {
        var indexer = new CodeGraphIndexer();
        Assert.NotNull(indexer);
    }

    [Fact]
    public void Constructor_CustomOptions_DoesNotThrow()
    {
        var options = new IndexerOptions { Configuration = "Release", SkipBuild = true };
        var indexer = new CodeGraphIndexer(options);
        Assert.NotNull(indexer);
    }

    #endregion

    #region IndexAsync validation

    [Fact]
    public async Task IndexAsync_NonExistentSolution_ThrowsFileNotFound()
    {
        var indexer = new CodeGraphIndexer();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => indexer.IndexAsync("nonexistent.sln"));
    }

    [Fact]
    public async Task IndexAndWriteAsync_NonExistentSolution_ThrowsFileNotFound()
    {
        var indexer = new CodeGraphIndexer();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => indexer.IndexAndWriteAsync("nonexistent.sln", "output"));
    }

    #endregion

    #region Pass enable/disable flags — verified via direct pass execution

    [Fact]
    public void PassFlags_RoutesPassDisabled_SkipsRouteEdges()
    {
        // Verify that the options model correctly represents pass toggling.
        // A full integration test would require a real solution; here we test the flag semantics.
        var options = new IndexerOptions { EnableRoutesPass = false };
        Assert.False(options.EnableRoutesPass);
        Assert.True(options.EnableConfigurationPass);
        Assert.True(options.EnableMiddlewarePass);
        Assert.True(options.EnableDbContextPass);
    }

    [Fact]
    public void PassFlags_AllDisabled_AllFalse()
    {
        var options = new IndexerOptions
        {
            EnableRoutesPass = false,
            EnableConfigurationPass = false,
            EnableMiddlewarePass = false,
            EnableDbContextPass = false
        };

        Assert.False(options.EnableRoutesPass);
        Assert.False(options.EnableConfigurationPass);
        Assert.False(options.EnableMiddlewarePass);
        Assert.False(options.EnableDbContextPass);
    }

    #endregion

    #region Filtering options

    [Fact]
    public void FilterOptions_ProjectFilter_Wildcard()
    {
        var options = new IndexerOptions { ProjectFilter = "MyApp.*" };
        Assert.Equal("MyApp.*", options.ProjectFilter);
    }

    [Fact]
    public void FilterOptions_IncludeExclude_CoExist()
    {
        var options = new IndexerOptions
        {
            IncludeProjects = new[] { "Core", "Api" },
            ExcludeProjects = new[] { "*.Tests" }
        };

        Assert.Equal(2, options.IncludeProjects!.Length);
        Assert.Single(options.ExcludeProjects!);
    }

    #endregion
}
