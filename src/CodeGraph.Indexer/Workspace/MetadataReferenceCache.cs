using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace CodeGraph.Indexer.Workspace;

public class MetadataReferenceCache
{
    private readonly ConcurrentDictionary<string, MetadataReference> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MetadataReference GetOrCreate(string dllPath)
    {
        return _cache.GetOrAdd(dllPath, static path =>
        {
            return MetadataReference.CreateFromFile(path);
        });
    }
}
