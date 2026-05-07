using CodeGraph.Query.Metrics;

namespace CodeGraph.Query.Tests.Metrics;

public class MetricsFormatterTests
{
    [Fact]
    public void AppendMetrics_ContainsCompressionRatio()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 50,
            RawFileTokens = 500,
            NodeCount = 10,
            EdgeCount = 15,
            FilesReferenced = 3
        };
        var result = MetricsFormatter.AppendMetrics("original", metrics);

        Assert.Contains("10 nodes", result);
        Assert.Contains("15 edges", result);
        Assert.Contains("3 files", result);
        Assert.Contains("~50 tokens", result);
        Assert.Contains("~500 raw", result);
        Assert.Contains("compression)", result);
    }

    [Fact]
    public void AppendMetrics_ContainsNodeAndEdgeCounts()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 100,
            RawFileTokens = 200,
            NodeCount = 5,
            EdgeCount = 8,
            FilesReferenced = 2
        };
        var result = MetricsFormatter.AppendMetrics("output", metrics);

        Assert.Contains("5 nodes", result);
        Assert.Contains("8 edges", result);
        Assert.Contains("2 files", result);
    }

    [Fact]
    public void AppendMetrics_PreservesOriginalOutput()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 10,
            RawFileTokens = 100,
            NodeCount = 1,
            EdgeCount = 0,
            FilesReferenced = 1
        };
        var result = MetricsFormatter.AppendMetrics("my original output", metrics);

        Assert.StartsWith("my original output", result);
    }

    [Fact]
    public void AppendMetrics_ContainsSeparator()
    {
        var metrics = new QueryMetrics
        {
            OutputTokens = 10,
            RawFileTokens = 50,
            NodeCount = 1,
            EdgeCount = 0,
            FilesReferenced = 1
        };
        var result = MetricsFormatter.AppendMetrics("output", metrics);

        Assert.Contains("---", result);
    }
}
