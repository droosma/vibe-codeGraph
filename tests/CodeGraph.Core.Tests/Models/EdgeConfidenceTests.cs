using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.Models;

public class EdgeConfidenceTests
{
    [Fact]
    public void GraphEdge_DefaultConfidence_IsVerified()
    {
        var edge = new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls };
        Assert.Equal(EdgeConfidence.Verified, edge.Confidence);
    }

    [Fact]
    public void GraphEdge_JsonRoundtrip_PreservesConfidence()
    {
        var edge = new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.Calls, Confidence = EdgeConfidence.Inferred };
        var json = JsonSerializer.Serialize(edge, GraphSerializationOptions.Default);
        var deserialized = JsonSerializer.Deserialize<GraphEdge>(json, GraphSerializationOptions.Default);
        Assert.Equal(EdgeConfidence.Inferred, deserialized!.Confidence);
    }

    [Fact]
    public void GraphEdge_JsonDeserialization_MissingConfidence_DefaultsToVerified()
    {
        var json = """{"fromId":"A","toId":"B","type":"calls","isExternal":false}""";
        var edge = JsonSerializer.Deserialize<GraphEdge>(json, GraphSerializationOptions.Default);
        Assert.Equal(EdgeConfidence.Verified, edge!.Confidence);
    }

    [Theory]
    [InlineData(EdgeConfidence.Verified)]
    [InlineData(EdgeConfidence.Inferred)]
    [InlineData(EdgeConfidence.Unresolved)]
    public void GraphEdge_JsonRoundtrip_AllConfidenceLevels(EdgeConfidence confidence)
    {
        var edge = new GraphEdge { FromId = "A", ToId = "B", Type = EdgeType.DependsOn, Confidence = confidence };
        var json = JsonSerializer.Serialize(edge, GraphSerializationOptions.Default);
        var deserialized = JsonSerializer.Deserialize<GraphEdge>(json, GraphSerializationOptions.Default);
        Assert.Equal(confidence, deserialized!.Confidence);
    }

    [Fact]
    public void EdgeConfidence_Ordering_VerifiedIsLowest()
    {
        Assert.True(EdgeConfidence.Verified < EdgeConfidence.Inferred);
        Assert.True(EdgeConfidence.Inferred < EdgeConfidence.Unresolved);
    }
}
