namespace CodeGraph.Query.Report;

public static class QuerySuggester
{
    public static List<string> Suggest(List<HubAnalyzer.HubNode> hubs, List<ClusterAnalyzer.ClusterInfo> clusters)
    {
        var suggestions = new List<string>();

        foreach (var hub in hubs.Take(3))
        {
            suggestions.Add($"codegraph query {hub.Name} --depth 1 --mode focused");
            suggestions.Add($"codegraph query {hub.Name} --depth 2 --kind calls");
        }

        foreach (var cluster in clusters.Where(c => c.ExternalEdges > c.InternalEdges).Take(2))
        {
            suggestions.Add($"codegraph list types --assembly {cluster.Assembly}");
        }

        return suggestions;
    }
}
