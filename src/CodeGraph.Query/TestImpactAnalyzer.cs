using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class TestImpactAnalyzer
{
    private readonly Dictionary<string, GraphNode> _nodes;
    private readonly Dictionary<string, List<GraphEdge>> _incoming;
    private readonly Dictionary<string, List<GraphEdge>> _outgoing;

    public TestImpactAnalyzer(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _incoming = new Dictionary<string, List<GraphEdge>>();
        _outgoing = new Dictionary<string, List<GraphEdge>>();

        foreach (var edge in edges)
        {
            if (!_incoming.TryGetValue(edge.ToId, out var inList))
            { inList = new List<GraphEdge>(); _incoming[edge.ToId] = inList; }
            inList.Add(edge);

            if (!_outgoing.TryGetValue(edge.FromId, out var outList))
            { outList = new List<GraphEdge>(); _outgoing[edge.FromId] = outList; }
            outList.Add(edge);
        }
    }

    public TestImpactResult Analyze(string pattern, int maxDepth = 3)
    {
        var targetNodes = _nodes.Values
            .Where(n => n.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                || n.Id.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)
                || n.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetNodes.Count == 0)
            return new TestImpactResult(pattern, null, new List<TestCoverage>(),
                new List<TestCoverage>(), new List<UncoveredCaller>(), "");

        var target = targetNodes[0];
        var directTests = new List<TestCoverage>();
        var indirectTests = new List<TestCoverage>();
        var uncoveredCallers = new List<UncoveredCaller>();

        // Direct coverage: CoveredBy edges from the target node
        if (_outgoing.TryGetValue(target.Id, out var targetOutEdges))
        {
            foreach (var edge in targetOutEdges.Where(e => e.Type == EdgeType.CoveredBy))
            {
                if (_nodes.TryGetValue(edge.ToId, out var testNode))
                    directTests.Add(new TestCoverage(testNode, new List<GraphNode> { target }));
            }
        }

        // BFS backward through Calls edges, checking CoveredBy at each level
        var visited = new HashSet<string> { target.Id };
        var currentLevel = new HashSet<string> { target.Id };
        var parentMap = new Dictionary<string, string>();

        for (var depth = 1; depth <= maxDepth; depth++)
        {
            var nextLevel = new HashSet<string>();

            foreach (var nodeId in currentLevel)
            {
                if (!_incoming.TryGetValue(nodeId, out var inEdges)) continue;

                foreach (var edge in inEdges.Where(e => e.Type == EdgeType.Calls))
                {
                    if (!visited.Add(edge.FromId)) continue;
                    nextLevel.Add(edge.FromId);
                    parentMap[edge.FromId] = nodeId;
                }
            }

            if (nextLevel.Count == 0) break;

            foreach (var callerId in nextLevel)
            {
                if (!_nodes.TryGetValue(callerId, out var callerNode)) continue;

                var hasCoverage = false;
                if (_outgoing.TryGetValue(callerId, out var callerOutEdges))
                {
                    foreach (var edge in callerOutEdges.Where(e => e.Type == EdgeType.CoveredBy))
                    {
                        if (_nodes.TryGetValue(edge.ToId, out var testNode))
                        {
                            var path = BuildPath(callerId, target.Id, parentMap);
                            indirectTests.Add(new TestCoverage(testNode, path));
                            hasCoverage = true;
                        }
                    }
                }

                if (!hasCoverage)
                    uncoveredCallers.Add(new UncoveredCaller(callerNode, depth));
            }

            currentLevel = nextLevel;
        }

        var testCommand = BuildTestCommand(directTests, indirectTests);
        return new TestImpactResult(pattern, target, directTests, indirectTests, uncoveredCallers, testCommand);
    }

    private List<GraphNode> BuildPath(string fromId, string targetId, Dictionary<string, string> parentMap)
    {
        var path = new List<GraphNode>();
        var current = fromId;

        while (current != targetId)
        {
            if (_nodes.TryGetValue(current, out var node))
                path.Add(node);
            if (!parentMap.TryGetValue(current, out var next))
                break;
            current = next;
        }

        if (_nodes.TryGetValue(targetId, out var targetNode))
            path.Add(targetNode);

        return path;
    }

    private static string BuildTestCommand(List<TestCoverage> directTests, List<TestCoverage> indirectTests)
    {
        var allTests = directTests.Concat(indirectTests).ToList();
        if (allTests.Count == 0) return "";

        var classNames = new HashSet<string>();
        foreach (var test in allTests)
        {
            var className = GetTestClassName(test.TestNode);
            if (!string.IsNullOrEmpty(className))
                classNames.Add(className);
        }

        if (classNames.Count == 0) return "";

        var filter = string.Join("|", classNames.Select(c => $"FullyQualifiedName~{c}"));
        return $"dotnet test --filter \"{filter}\"";
    }

    private static string GetTestClassName(GraphNode testNode)
    {
        if (testNode.Kind == NodeKind.Method && !string.IsNullOrEmpty(testNode.ContainingTypeId))
        {
            var parts = testNode.ContainingTypeId.Split('.');
            return parts[^1];
        }
        return testNode.Name;
    }
}

public record TestImpactResult(
    string Pattern,
    GraphNode? Target,
    List<TestCoverage> DirectTests,
    List<TestCoverage> IndirectTests,
    List<UncoveredCaller> UncoveredCallers,
    string SuggestedTestCommand);

public record TestCoverage(
    GraphNode TestNode,
    List<GraphNode> PathFromTarget);

public record UncoveredCaller(
    GraphNode Caller,
    int Depth);
