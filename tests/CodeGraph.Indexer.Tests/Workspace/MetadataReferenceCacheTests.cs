using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;

namespace CodeGraph.Indexer.Tests.Workspace;

public class MetadataReferenceCacheTests
{
    [Fact]
    public void GetOrCreate_ReturnsSameInstance_ForSamePath()
    {
        var cache = new MetadataReferenceCache();
        var path = typeof(object).Assembly.Location;

        var ref1 = cache.GetOrCreate(path);
        var ref2 = cache.GetOrCreate(path);

        Assert.Same(ref1, ref2);
    }

    [Fact]
    public void GetOrCreate_ReturnsDifferentInstances_ForDifferentPaths()
    {
        var cache = new MetadataReferenceCache();
        var path1 = typeof(object).Assembly.Location;
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var path2 = Path.Combine(runtimeDir, "System.Runtime.dll");

        var ref1 = cache.GetOrCreate(path1);
        var ref2 = cache.GetOrCreate(path2);

        Assert.NotSame(ref1, ref2);
    }

    [Fact]
    public void GetOrCreate_IsCaseInsensitive()
    {
        var cache = new MetadataReferenceCache();
        var path = typeof(object).Assembly.Location;
        var upperPath = path.ToUpperInvariant();

        var ref1 = cache.GetOrCreate(path);
        var ref2 = cache.GetOrCreate(upperPath);

        Assert.Same(ref1, ref2);
    }

    [Fact]
    public void GetOrCreate_ReturnsValidMetadataReference()
    {
        var cache = new MetadataReferenceCache();
        var path = typeof(object).Assembly.Location;

        var reference = cache.GetOrCreate(path);

        Assert.NotNull(reference);
        Assert.IsAssignableFrom<MetadataReference>(reference);
    }
}
