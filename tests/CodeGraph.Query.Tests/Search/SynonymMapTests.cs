using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SynonymMapTests
{
    [Theory]
    [InlineData("auth", new[] { "authentication", "authorization", "jwt", "bearer", "identity" })]
    [InlineData("db", new[] { "database", "repository", "entity", "context", "dbcontext" })]
    [InlineData("api", new[] { "controller", "endpoint", "route", "handler" })]
    [InlineData("ui", new[] { "view", "page", "component", "razor" })]
    [InlineData("log", new[] { "logging", "logger", "serilog", "nlog" })]
    [InlineData("cache", new[] { "caching", "redis", "memory", "distributed" })]
    [InlineData("config", new[] { "configuration", "settings", "options", "appsettings" })]
    [InlineData("di", new[] { "dependency", "injection", "service", "container", "register" })]
    [InlineData("test", new[] { "xunit", "nunit", "mstest", "fact", "theory" })]
    [InlineData("async", new[] { "task", "await", "concurrent" })]
    public void Expand_KnownSynonym_ReturnsExpectedSynonyms(string token, string[] expected)
    {
        var result = SynonymMap.Expand(token);

        foreach (var syn in expected)
        {
            Assert.Contains(syn, result);
        }
    }

    [Theory]
    [InlineData("authentication", "auth")]
    [InlineData("database", "db")]
    [InlineData("controller", "api")]
    [InlineData("logging", "log")]
    [InlineData("caching", "cache")]
    [InlineData("configuration", "config")]
    [InlineData("dependency", "di")]
    [InlineData("xunit", "test")]
    [InlineData("task", "async")]
    public void Expand_ReverseLookup_Works(string token, string expectedSynonym)
    {
        var result = SynonymMap.Expand(token);

        Assert.Contains(expectedSynonym, result);
    }

    [Fact]
    public void Expand_UnknownToken_ReturnsEmpty()
    {
        var result = SynonymMap.Expand("xyzunknown");

        Assert.Empty(result);
    }

    [Fact]
    public void Expand_EmptyString_ReturnsEmpty()
    {
        var result = SynonymMap.Expand("");

        Assert.Empty(result);
    }

    [Fact]
    public void Expand_NullToken_ReturnsEmpty()
    {
        var result = SynonymMap.Expand(null!);

        Assert.Empty(result);
    }

    [Fact]
    public void Expand_IsCaseInsensitive()
    {
        var lower = SynonymMap.Expand("auth");
        var upper = SynonymMap.Expand("AUTH");
        var mixed = SynonymMap.Expand("Auth");

        Assert.Equal(lower.Count, upper.Count);
        Assert.Equal(lower.Count, mixed.Count);
    }

    [Fact]
    public void Expand_DoesNotReturnSelf()
    {
        var result = SynonymMap.Expand("auth");

        Assert.DoesNotContain("auth", result);
    }
}
