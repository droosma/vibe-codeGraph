using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class QuerySessionTrackerTests
{
    [Fact]
    public void Record_IncreasesQueryCount()
    {
        var tracker = new QuerySessionTracker();

        tracker.Record("OrderService", 1, 5);

        Assert.Equal(1, tracker.QueryCount);
    }

    [Fact]
    public void WasQueried_ReturnsTrueForRecordedPattern()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 1, 5);

        Assert.True(tracker.WasQueried("OrderService"));
    }

    [Fact]
    public void WasQueried_CaseInsensitive()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 1, 5);

        Assert.True(tracker.WasQueried("orderservice"));
    }

    [Fact]
    public void WasQueried_ReturnsFalseForUnrecordedPattern()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 1, 5);

        Assert.False(tracker.WasQueried("UserService"));
    }

    [Fact]
    public void WasQueriedAtDepth_ReturnsTrueWhenQueriedAtHigherDepth()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 3, 5);

        Assert.True(tracker.WasQueriedAtDepth("OrderService", 2));
    }

    [Fact]
    public void WasQueriedAtDepth_ReturnsFalseWhenQueriedAtLowerDepth()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 1, 5);

        Assert.False(tracker.WasQueriedAtDepth("OrderService", 2));
    }

    [Fact]
    public void GetQueriedPatterns_ReturnsDistinctPatterns()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("OrderService", 1, 5);
        tracker.Record("OrderService", 2, 10);
        tracker.Record("UserService", 1, 3);

        var patterns = tracker.GetQueriedPatterns();

        Assert.Equal(2, patterns.Count);
        Assert.Contains("OrderService", patterns);
        Assert.Contains("UserService", patterns);
    }

    [Fact]
    public void QueryCount_TracksAllQueries()
    {
        var tracker = new QuerySessionTracker();
        tracker.Record("A", 1, 1);
        tracker.Record("B", 1, 2);
        tracker.Record("A", 2, 3);

        Assert.Equal(3, tracker.QueryCount);
    }
}
