using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class GraphSerializationOptionsTests
{
    [Fact]
    public void Default_SerializesIndentedCamelCaseJsonWithStringEnums()
    {
        var edge = new GraphEdge
        {
            FromId = "A.Source",
            ToId = "B.Target",
            Type = EdgeType.Calls,
            Confidence = EdgeConfidence.Inferred,
            Metadata = new Dictionary<string, string> { ["callSite"] = "line 42" }
        };

        var json = JsonSerializer.Serialize(edge, GraphSerializationOptions.Default);

        Assert.Contains(Environment.NewLine, json);
        Assert.Contains("\"fromId\": \"A.Source\"", json);
        Assert.Contains("\"toId\": \"B.Target\"", json);
        Assert.Contains("\"type\": \"calls\"", json);
        Assert.Contains("\"confidence\": \"inferred\"", json);
        Assert.DoesNotContain("\"FromId\"", json);
    }
}
