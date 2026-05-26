using CodeGraph.Core.Models;
using CodeGraph.Query.Metrics;

namespace CodeGraph.Query.Tests.Metrics;

public class MetricsMutationCoverageTests
{
    [Fact]
    public void Calculate_ComputesExactRawTokenCountAcrossMultipleFiles()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>
            {
                ["A.Type"] = new()
                {
                    Id = "A.Type",
                    Name = "Type",
                    Kind = NodeKind.Type,
                    FilePath = "src/A.cs",
                    StartLine = 5,
                    EndLine = 9
                },
                ["A.Method"] = new()
                {
                    Id = "A.Method",
                    Name = "Method",
                    Kind = NodeKind.Method,
                    FilePath = "src/A.cs",
                    StartLine = 20,
                    EndLine = 20
                },
                ["B.Type"] = new()
                {
                    Id = "B.Type",
                    Name = "Type",
                    Kind = NodeKind.Type,
                    FilePath = "src/B.cs",
                    StartLine = 1,
                    EndLine = 3
                }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata()
        };

        var metrics = CompressionCalculator.Calculate(result, "12345678");

        Assert.Equal(2, metrics.OutputTokens);
        Assert.Equal(90, metrics.RawFileTokens);
        Assert.Equal(2, metrics.FilesReferenced);
        Assert.Equal(3, metrics.NodeCount);
        Assert.Equal(0, metrics.EdgeCount);
    }

    [Fact]
    public void Calculate_WhenEndLinePrecedesStartLine_UsesMinimumSingleLine()
    {
        var result = new QueryResult
        {
            Nodes = new Dictionary<string, GraphNode>
            {
                ["Broken.Range"] = new()
                {
                    Id = "Broken.Range",
                    Name = "Range",
                    Kind = NodeKind.Method,
                    FilePath = "src/Broken.cs",
                    StartLine = 10,
                    EndLine = 8
                }
            },
            Edges = new List<GraphEdge>(),
            Metadata = new GraphMetadata()
        };

        var metrics = CompressionCalculator.Calculate(result, "abcd");

        Assert.Equal(10, metrics.RawFileTokens);
    }

    [Fact]
    public void AppendMetrics_UsesExactFooterSpacingAndFormatting()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 25,
            RawFileTokens = 100,
            NodeCount = 3,
            EdgeCount = 4,
            FilesReferenced = 2
        };

        var output = MetricsFormatter.AppendMetrics("body", metrics);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "body",
            string.Empty,
            "---",
            "📊 3 nodes, 4 edges, 2 files",
            $"📦 ~25 tokens (vs ~100 raw → {metrics.CompressionRatio:F1}× compression)",
            string.Empty
        });

        Assert.Equal(expected, output);
    }

    [Fact]
    public void CompressionRatio_UsesRawDividedByOutputTokens()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 25,
            RawFileTokens = 100
        };

        Assert.Equal(4.0, metrics.CompressionRatio, 10);
    }
}
