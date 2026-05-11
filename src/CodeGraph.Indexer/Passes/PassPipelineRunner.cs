using CodeGraph.Core.Models;
using CodeGraph.Indexer.Workspace;

namespace CodeGraph.Indexer.Passes;

internal readonly record struct PassPipelineOptions(
    bool EnableRoutesPass = true,
    bool EnableConfigurationPass = true,
    bool EnableMiddlewarePass = true,
    bool EnableDbContextPass = true);

internal static class PassPipelineRunner
{
    public static (List<GraphNode> Nodes, List<GraphEdge> Edges) Execute(
        ProjectCompilation project,
        string solutionRoot,
        PassPipelineOptions options)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        var syntaxPass = new SyntaxPass();
        var semanticPass = new SemanticPass();
        var diPass = new DiPass();
        var testCoveragePass = new TestCoveragePass();
        var routesPass = new RoutesPass();
        var configurationPass = new ConfigurationPass();
        var middlewarePass = new MiddlewarePass();
        var dbContextPass = new DbContextPass();

        var (syntaxNodes, syntaxEdges) = syntaxPass.Execute(project.Compilation, solutionRoot);
        nodes.AddRange(syntaxNodes);
        edges.AddRange(syntaxEdges);

        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
            knownIds.Add(node.Id);

        var (semanticExternalNodes, semanticEdges) = semanticPass.Execute(project.Compilation, solutionRoot, knownIds);
        AppendExternalPassResult(nodes, edges, knownIds, semanticEdges, semanticExternalNodes);

        var (diEdges, diExternalNodes) = diPass.Execute(project.Compilation, solutionRoot, knownIds);
        AppendExternalPassResult(nodes, edges, knownIds, diEdges, diExternalNodes);

        var (testEdges, testExternalNodes) = testCoveragePass.Execute(project.Compilation, solutionRoot, knownIds);
        AppendExternalPassResult(nodes, edges, knownIds, testEdges, testExternalNodes);

        if (options.EnableRoutesPass)
        {
            var (routeEdges, routeExternalNodes) = routesPass.Execute(project.Compilation, solutionRoot, knownIds);
            AppendExternalPassResult(nodes, edges, knownIds, routeEdges, routeExternalNodes);
        }

        if (options.EnableConfigurationPass)
        {
            var (configurationEdges, configurationExternalNodes) = configurationPass.Execute(project.Compilation, solutionRoot, knownIds);
            AppendExternalPassResult(nodes, edges, knownIds, configurationEdges, configurationExternalNodes);
        }

        if (options.EnableMiddlewarePass)
        {
            var (middlewareEdges, middlewareExternalNodes) = middlewarePass.Execute(project.Compilation, solutionRoot, knownIds);
            AppendExternalPassResult(nodes, edges, knownIds, middlewareEdges, middlewareExternalNodes);
        }

        if (options.EnableDbContextPass)
        {
            var (dbEdges, dbExternalNodes) = dbContextPass.Execute(project.Compilation, solutionRoot, knownIds);
            AppendExternalPassResult(nodes, edges, knownIds, dbEdges, dbExternalNodes);
        }

        return (nodes, edges);
    }

    private static void AppendExternalPassResult(
        List<GraphNode> nodes,
        List<GraphEdge> edges,
        HashSet<string> knownIds,
        List<GraphEdge> passEdges,
        List<GraphNode> externalNodes)
    {
        nodes.AddRange(externalNodes);
        edges.AddRange(passEdges);

        foreach (var node in externalNodes)
            knownIds.Add(node.Id);
    }
}
