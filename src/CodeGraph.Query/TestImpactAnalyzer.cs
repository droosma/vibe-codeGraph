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
            {
                inList = new List<GraphEdge>();
                _incoming[edge.ToId] = inList;
            }

            inList.Add(edge);

            if (!_outgoing.TryGetValue(edge.FromId, out var outList))
            {
                outList = new List<GraphEdge>();
                _outgoing[edge.FromId] = outList;
            }

            outList.Add(edge);
        }
    }

    public TestImpactResult Analyze(string pattern, int maxDepth = 3)
    {
        var targetNodes = _nodes.Values
            .Where(n => n.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                || n.Id.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase)
                || n.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetNodes.Count == 0)
            return new TestImpactResult(pattern, null, new List<TestCoverage>(), new List<TestCoverage>(), new List<UncoveredCaller>(), string.Empty);

        var target = targetNodes[0];
        var directTests = new List<TestCoverage>();
        var indirectTests = new List<TestCoverage>();
        var uncoveredCallers = new List<UncoveredCaller>();
        var seenDirectTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIndirectTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_outgoing.TryGetValue(target.Id, out var targetOutEdges))
        {
            foreach (var edge in targetOutEdges.Where(e => e.Type == EdgeType.CoveredBy).OrderBy(e => e.ToId, StringComparer.OrdinalIgnoreCase))
            {
                if (!_nodes.TryGetValue(edge.ToId, out var testNode) || !seenDirectTests.Add(testNode.Id))
                    continue;

                directTests.Add(new TestCoverage(testNode, new List<GraphNode> { target }));
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.Id };
        var currentLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.Id };
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var depth = 1; depth <= maxDepth; depth++)
        {
            var nextLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var nodeId in currentLevel)
            {
                if (!_incoming.TryGetValue(nodeId, out var inEdges))
                    continue;

                foreach (var edge in inEdges.Where(e => e.Type == EdgeType.Calls).OrderBy(e => e.FromId, StringComparer.OrdinalIgnoreCase))
                {
                    if (!visited.Add(edge.FromId))
                        continue;

                    nextLevel.Add(edge.FromId);
                    parentMap[edge.FromId] = nodeId;
                }
            }

            if (nextLevel.Count == 0)
                break;

            foreach (var callerId in nextLevel.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (!_nodes.TryGetValue(callerId, out var callerNode))
                    continue;

                var hasCoverage = false;
                if (_outgoing.TryGetValue(callerId, out var callerOutEdges))
                {
                    foreach (var edge in callerOutEdges.Where(e => e.Type == EdgeType.CoveredBy).OrderBy(e => e.ToId, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!_nodes.TryGetValue(edge.ToId, out var testNode))
                            continue;

                        if (seenDirectTests.Contains(testNode.Id) || !seenIndirectTests.Add(testNode.Id))
                        {
                            hasCoverage = true;
                            continue;
                        }

                        indirectTests.Add(new TestCoverage(testNode, BuildPath(callerId, target.Id, parentMap)));
                        hasCoverage = true;
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
        if (allTests.Count == 0)
            return string.Empty;

        var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var test in allTests)
        {
            var className = GetTestClassName(test.TestNode);
            if (!string.IsNullOrEmpty(className))
                classNames.Add(className);
        }

        if (classNames.Count == 0)
            return string.Empty;

        var filter = string.Join("|", classNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Select(c => $"FullyQualifiedName~{c}"));
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
    string SuggestedTestCommand)
{
    public bool HasTests => DirectTests.Count > 0 || IndirectTests.Count > 0;
};

public record TestCoverage(
    GraphNode TestNode,
    List<GraphNode> PathFromTarget);

public record UncoveredCaller(
    GraphNode Caller,
    int Depth);
