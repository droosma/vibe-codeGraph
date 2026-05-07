using CodeGraph.Core.Models;
using CodeGraph.Query.Metrics;

namespace CodeGraph.Query.Tests.Metrics;

public class CompressionCalculatorTests
{
    [Fact]
    public void EstimateTokens_ReturnsApproximateCount()
    {
        // 12 chars / 4 = 3 tokens
        Assert.Equal(3, CompressionCalculator.EstimateTokens("hello world!"));
    }

    [Fact]
    public void EstimateTokens_RoundsUp()
    {
        // 5 chars → ceil(5/4) = 2
        Assert.Equal(2, CompressionCalculator.EstimateTokens("hello"));
    }

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, CompressionCalculator.EstimateTokens(""));
    }

    [Fact]
    public void Calculate_ProducesPositiveCompression()
    {
        var result = BuildResultWithFiles();
        var output = "Short formatted output";
        var metrics = CompressionCalculator.Calculate(result, output);

        Assert.True(metrics.CompressionRatio > 1.0);
        Assert.True(metrics.OutputTokens > 0);
        Assert.True(metrics.RawFileTokens > 0);
    }

    [Fact]
    public void Calculate_CountsFilesCorrectly()
    {
        var result = BuildResultWithFiles();
        var metrics = CompressionCalculator.Calculate(result, "output");

        Assert.Equal(2, metrics.FilesReferenced);
    }

    [Fact]
    public void Calculate_CountsNodesAndEdges()
    {
        var result = BuildResultWithFiles();
        var metrics = CompressionCalculator.Calculate(result, "output");

        Assert.Equal(3, metrics.NodeCount);
        Assert.Equal(1, metrics.EdgeCount);
    }

    [Fact]
    public void Calculate_EmptyResult_ZeroCompression()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>(),
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata()
        };
        var metrics = CompressionCalculator.Calculate(result, "");

        Assert.Equal(0, metrics.CompressionRatio);
        Assert.Equal(0, metrics.FilesReferenced);
        Assert.Equal(0, metrics.NodeCount);
        Assert.Equal(0, metrics.EdgeCount);
    }

    private static QueryResult BuildResultWithFiles()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.ClassA"] = new GraphNode
            {
                Id = "A.ClassA",
                Name = "ClassA",
                Kind = NodeKind.Type,
                FilePath = "src/ClassA.cs",
                StartLine = 1,
                EndLine = 50
            },
            ["A.ClassA.Method1"] = new GraphNode
            {
                Id = "A.ClassA.Method1",
                Name = "Method1",
                Kind = NodeKind.Method,
                FilePath = "src/ClassA.cs",
                StartLine = 10,
                EndLine = 30
            },
            ["B.ClassB"] = new GraphNode
            {
                Id = "B.ClassB",
                Name = "ClassB",
                Kind = NodeKind.Type,
                FilePath = "src/ClassB.cs",
                StartLine = 1,
                EndLine = 40
            }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.ClassA.Method1", ToId = "B.ClassB", Type = EdgeType.Calls }
        };

        return new QueryResult
        {
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata()
        };
    }
}
