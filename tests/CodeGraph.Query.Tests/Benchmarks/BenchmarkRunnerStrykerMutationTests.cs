using System.Reflection;
using System.Text.Json.Nodes;
using CodeGraph.Core.Models;
using CodeGraph.Query.Benchmarks;

namespace CodeGraph.Query.Tests.Benchmarks;

public class BenchmarkRunnerStrykerMutationTests
{
    private static GraphNode Node(string id, string name, string assembly = "Asm") => new()
    {
        Id = id,
        Name = name,
        Kind = NodeKind.Type,
        AssemblyName = assembly,
        ContainingNamespaceId = assembly
    };

    private static BenchmarkRunner CreateRunner(Dictionary<string, GraphNode> nodes, List<GraphEdge>? edges = null)
    {
        return new BenchmarkRunner(new QueryEngine(nodes, edges ?? [], new GraphMetadata()));
    }

    private static (int Nodes, int Edges, int TokenEstimate) InvokePrivate(BenchmarkRunner runner, string methodName, JsonObject args)
    {
        var method = typeof(BenchmarkRunner).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ((int Nodes, int Edges, int TokenEstimate))method!.Invoke(runner, new object[] { args })!;
    }

    [Fact]
    public void ExecuteSearch_MissingQuery_DoesNotUseMutationSentinel()
    {
        var runner = CreateRunner(new Dictionary<string, GraphNode>
        {
            ["Stryker was here!"] = Node("Stryker was here!", "Stryker was here!")
        });

        var result = InvokePrivate(runner, "ExecuteSearch", new JsonObject());

        Assert.Equal(0, result.Nodes);
        Assert.Equal(0, result.TokenEstimate);
    }

    [Fact]
    public void ExecuteSearch_MultipleResults_CountsNewlineSeparatorsInTokenEstimate()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["N0"] = Node("N0", "Node0"),
            ["N1"] = Node("N1", "Node1"),
            ["N2"] = Node("N2", "Node2"),
            ["N3"] = Node("N3", "Node3")
        };
        var engine = new QueryEngine(nodes, [], new GraphMetadata());
        var runner = new BenchmarkRunner(engine);
        var expectedOutput = string.Join("\n", engine.Search("Node", 10).Select(node => $"{node.Kind}: {node.Id}"));

        var result = InvokePrivate(runner, "ExecuteSearch", new JsonObject { ["query"] = "Node", ["top"] = 10 });

        Assert.Equal((expectedOutput.Length + 3) / 4, result.TokenEstimate);
    }

    [Fact]
    public void ExecuteListAssemblies_MultipleAssemblies_CountsNewlineSeparatorsInTokenEstimate()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Type"] = Node("A.Type", "Type", "A"),
            ["B.Type"] = Node("B.Type", "Type", "B"),
            ["C.Type"] = Node("C.Type", "Type", "C"),
            ["D.Type"] = Node("D.Type", "Type", "D"),
            ["E.Type"] = Node("E.Type", "Type", "E")
        };
        var listEngine = new ListEngine(nodes, []);
        var expectedOutput = string.Join("\n", listEngine.ListAssemblies().Select(assembly => $"{assembly.Name}: {assembly.TypeCount} types, {assembly.MethodCount} methods"));
        var runner = CreateRunner(nodes);

        var result = InvokePrivate(runner, "ExecuteList", new JsonObject { ["scope"] = "assemblies" });

        Assert.Equal((expectedOutput.Length + 3) / 4, result.TokenEstimate);
    }

    [Fact]
    public void ExecuteListTypes_MultipleRows_CountsNewlineSeparatorsInTokenEstimate()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A"] = Node("A", "A"),
            ["B"] = Node("B", "B"),
            ["C"] = Node("C", "C"),
            ["D"] = Node("D", "D"),
            ["E"] = Node("E", "E")
        };
        var listEngine = new ListEngine(nodes, []);
        var expectedOutput = string.Join("\n", listEngine.ListTypes(top: 5).Types.Select(type => $"{type.Name}: in={type.InDegree} out={type.OutDegree}"));
        var runner = CreateRunner(nodes);

        var result = InvokePrivate(runner, "ExecuteList", new JsonObject { ["scope"] = "types", ["top"] = 5 });

        Assert.Equal((expectedOutput.Length + 3) / 4, result.TokenEstimate);
    }
}
