#if NETSTANDARD2_0

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace CodeGraph.Core.Polyfills;

internal static class CollectionPolyfills
{
    /// <summary>Deconstruct a KeyValuePair into key and value.</summary>
    public static void Deconstruct<TKey, TValue>(
        this KeyValuePair<TKey, TValue> pair,
        out TKey key,
        out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    /// <summary>Creates a HashSet from an IEnumerable.</summary>
    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
    {
        return new HashSet<T>(source);
    }
}

#endif
