using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGraph.Query.OutputFormatters;

public static class GraphDiffJsonFormatter
{
    public static string Format(GraphDiffResult result)
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
