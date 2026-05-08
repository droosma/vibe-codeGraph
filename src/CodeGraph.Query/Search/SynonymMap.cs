namespace CodeGraph.Query.Search;

/// <summary>
/// Maps common .NET concept abbreviations and terms to their synonyms
/// so that searching for "auth" also matches "authentication", "jwt", etc.
/// </summary>
public static class SynonymMap
{
    private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auth"]           = ["authentication", "authorization", "jwt", "bearer", "identity"],
        ["authentication"] = ["auth", "authorization", "jwt", "bearer", "identity"],
        ["authorization"]  = ["auth", "authentication", "jwt", "bearer", "identity"],
        ["jwt"]            = ["auth", "authentication", "authorization", "bearer", "identity"],
        ["bearer"]         = ["auth", "authentication", "authorization", "jwt", "identity"],
        ["identity"]       = ["auth", "authentication", "authorization", "jwt", "bearer"],

        ["db"]         = ["database", "repository", "entity", "context", "dbcontext"],
        ["database"]   = ["db", "repository", "entity", "context", "dbcontext"],
        ["repository"] = ["db", "database", "entity", "context", "dbcontext"],
        ["entity"]     = ["db", "database", "repository", "context", "dbcontext"],
        ["context"]    = ["db", "database", "repository", "entity", "dbcontext"],
        ["dbcontext"]  = ["db", "database", "repository", "entity", "context"],

        ["api"]        = ["controller", "endpoint", "route", "handler"],
        ["controller"] = ["api", "endpoint", "route", "handler"],
        ["endpoint"]   = ["api", "controller", "route", "handler"],
        ["route"]      = ["api", "controller", "endpoint", "handler"],
        ["handler"]    = ["api", "controller", "endpoint", "route"],

        ["ui"]        = ["view", "page", "component", "razor"],
        ["view"]      = ["ui", "page", "component", "razor"],
        ["page"]      = ["ui", "view", "component", "razor"],
        ["component"] = ["ui", "view", "page", "razor"],
        ["razor"]     = ["ui", "view", "page", "component"],

        ["async"]      = ["task", "await", "concurrent"],
        ["task"]       = ["async", "await", "concurrent"],
        ["await"]      = ["async", "task", "concurrent"],
        ["concurrent"] = ["async", "task", "await"],

        ["test"]   = ["xunit", "nunit", "mstest", "fact", "theory"],
        ["xunit"]  = ["test", "nunit", "mstest", "fact", "theory"],
        ["nunit"]  = ["test", "xunit", "mstest", "fact", "theory"],
        ["mstest"] = ["test", "xunit", "nunit", "fact", "theory"],
        ["fact"]   = ["test", "xunit", "nunit", "mstest", "theory"],
        ["theory"] = ["test", "xunit", "nunit", "mstest", "fact"],

        ["config"]        = ["configuration", "settings", "options", "appsettings"],
        ["configuration"] = ["config", "settings", "options", "appsettings"],
        ["settings"]      = ["config", "configuration", "options", "appsettings"],
        ["options"]       = ["config", "configuration", "settings", "appsettings"],
        ["appsettings"]   = ["config", "configuration", "settings", "options"],

        ["di"]         = ["dependency", "injection", "service", "container", "register"],
        ["dependency"] = ["di", "injection", "service", "container", "register"],
        ["injection"]  = ["di", "dependency", "service", "container", "register"],
        ["container"]  = ["di", "dependency", "injection", "service", "register"],
        ["register"]   = ["di", "dependency", "injection", "service", "container"],

        ["log"]     = ["logging", "logger", "serilog", "nlog"],
        ["logging"] = ["log", "logger", "serilog", "nlog"],
        ["logger"]  = ["log", "logging", "serilog", "nlog"],
        ["serilog"] = ["log", "logging", "logger", "nlog"],
        ["nlog"]    = ["log", "logging", "logger", "serilog"],

        ["cache"]       = ["caching", "redis", "memory", "distributed"],
        ["caching"]     = ["cache", "redis", "memory", "distributed"],
        ["redis"]       = ["cache", "caching", "memory", "distributed"],
        ["distributed"] = ["cache", "caching", "redis", "memory"],
    };

    /// <summary>
    /// Returns synonyms for the given token, excluding the token itself.
    /// Returns an empty list if no synonyms are known.
    /// </summary>
    public static IReadOnlyList<string> Expand(string token)
    {
        if (string.IsNullOrEmpty(token))
            return [];

        if (Groups.TryGetValue(token, out var synonyms))
            return synonyms;

        return [];
    }
}
