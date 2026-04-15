using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGraph.Core.IO;

namespace CodeGraph.Query.OutputFormatters;

public static class JsonFormatter
{
    public static string Format(QueryResult result)
    {
        return JsonSerializer.Serialize(result, Options);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
