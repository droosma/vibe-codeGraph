using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGraph.Core.IO;

public static class GraphSerializationOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
