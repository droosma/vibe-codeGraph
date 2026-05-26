using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SynonymMapTests
{
    [Theory]
    [InlineData("auth", "authentication", "authorization", "jwt", "bearer", "identity")]
    [InlineData("authentication", "auth", "authorization", "jwt", "bearer", "identity")]
    [InlineData("authorization", "auth", "authentication", "jwt", "bearer", "identity")]
    [InlineData("jwt", "auth", "authentication", "authorization", "bearer", "identity")]
    [InlineData("bearer", "auth", "authentication", "authorization", "jwt", "identity")]
    [InlineData("identity", "auth", "authentication", "authorization", "jwt", "bearer")]
    [InlineData("db", "database", "repository", "entity", "context", "dbcontext")]
    [InlineData("database", "db", "repository", "entity", "context", "dbcontext")]
    [InlineData("repository", "db", "database", "entity", "context", "dbcontext")]
    [InlineData("entity", "db", "database", "repository", "context", "dbcontext")]
    [InlineData("context", "db", "database", "repository", "entity", "dbcontext")]
    [InlineData("dbcontext", "db", "database", "repository", "entity", "context")]
    [InlineData("api", "controller", "endpoint", "route", "handler")]
    [InlineData("controller", "api", "endpoint", "route", "handler")]
    [InlineData("endpoint", "api", "controller", "route", "handler")]
    [InlineData("route", "api", "controller", "endpoint", "handler")]
    [InlineData("handler", "api", "controller", "endpoint", "route")]
    [InlineData("ui", "view", "page", "component", "razor")]
    [InlineData("view", "ui", "page", "component", "razor")]
    [InlineData("page", "ui", "view", "component", "razor")]
    [InlineData("component", "ui", "view", "page", "razor")]
    [InlineData("razor", "ui", "view", "page", "component")]
    [InlineData("async", "task", "await", "concurrent")]
    [InlineData("task", "async", "await", "concurrent")]
    [InlineData("await", "async", "task", "concurrent")]
    [InlineData("concurrent", "async", "task", "await")]
    [InlineData("test", "xunit", "nunit", "mstest", "fact", "theory")]
    [InlineData("xunit", "test", "nunit", "mstest", "fact", "theory")]
    [InlineData("nunit", "test", "xunit", "mstest", "fact", "theory")]
    [InlineData("mstest", "test", "xunit", "nunit", "fact", "theory")]
    [InlineData("fact", "test", "xunit", "nunit", "mstest", "theory")]
    [InlineData("theory", "test", "xunit", "nunit", "mstest", "fact")]
    [InlineData("config", "configuration", "settings", "options", "appsettings")]
    [InlineData("configuration", "config", "settings", "options", "appsettings")]
    [InlineData("settings", "config", "configuration", "options", "appsettings")]
    [InlineData("options", "config", "configuration", "settings", "appsettings")]
    [InlineData("appsettings", "config", "configuration", "settings", "options")]
    [InlineData("di", "dependency", "injection", "service", "container", "register")]
    [InlineData("dependency", "di", "injection", "service", "container", "register")]
    [InlineData("injection", "di", "dependency", "service", "container", "register")]
    [InlineData("container", "di", "dependency", "injection", "service", "register")]
    [InlineData("register", "di", "dependency", "injection", "service", "container")]
    [InlineData("log", "logging", "logger", "serilog", "nlog")]
    [InlineData("logging", "log", "logger", "serilog", "nlog")]
    [InlineData("logger", "log", "logging", "serilog", "nlog")]
    [InlineData("serilog", "log", "logging", "logger", "nlog")]
    [InlineData("nlog", "log", "logging", "logger", "serilog")]
    [InlineData("cache", "caching", "redis", "memory", "distributed")]
    [InlineData("caching", "cache", "redis", "memory", "distributed")]
    [InlineData("redis", "cache", "caching", "memory", "distributed")]
    [InlineData("distributed", "cache", "caching", "redis", "memory")]
    public void Expand_KnownToken_ReturnsExactSynonyms(string token, params string[] expected)
    {
        var result = SynonymMap.Expand(token);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("AUTH", "authentication", "authorization", "jwt", "bearer", "identity")]
    [InlineData("Db", "database", "repository", "entity", "context", "dbcontext")]
    [InlineData("ConFig", "configuration", "settings", "options", "appsettings")]
    public void Expand_KnownTokenWithDifferentCasing_ReturnsExactSynonyms(string token, params string[] expected)
    {
        var result = SynonymMap.Expand(token);

        Assert.Equal(expected, result);
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
        var result = SynonymMap.Expand(string.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Expand_NullToken_ReturnsEmpty()
    {
        var result = SynonymMap.Expand(null!);

        Assert.Empty(result);
    }
}