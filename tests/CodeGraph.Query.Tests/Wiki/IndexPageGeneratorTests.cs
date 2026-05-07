using CodeGraph.Core.Models;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests.Wiki;

public class IndexPageGeneratorTests
{
	private static Dictionary<string, GraphNode> CreateNodes(
		params (string id, string name, NodeKind kind, string assembly)[] defs)
	{
		var nodes = new Dictionary<string, GraphNode>();
		foreach (var (id, name, kind, assembly) in defs)
			nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = assembly };
		return nodes;
	}

	private static GraphMetadata CreateMetadata() => new()
	{
		SolutionName = "TestSolution",
		CommitHash = "abc123",
		GeneratedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
	};

	[Fact]
	public void Generate_ContainsAssemblyTable()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "MyAssembly"),
			("B", "MethodB", NodeKind.Method, "MyAssembly"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("| Assembly |", result);
		Assert.Contains("MyAssembly", result);
	}

	[Fact]
	public void Generate_LinksUseRelativePaths()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "MyAssembly"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("assemblies/MyAssembly.md", result);
		Assert.DoesNotContain("http", result);
	}

	[Fact]
	public void Generate_ContainsSolutionName()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("TestSolution", result);
	}

	[Fact]
	public void Generate_ContainsNavigationLinks()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("[Interfaces](INTERFACES.md)", result);
		Assert.Contains("[DI Wiring](DI-WIRING.md)", result);
	}

	[Fact]
	public void Generate_ShowsTypeAndMethodCounts()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "Asm1"),
			("T2", "Type2", NodeKind.Type, "Asm1"),
			("M1", "Method1", NodeKind.Method, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		// 2 types, 1 method, 3 total
		Assert.Contains("| 2 |", result);
		Assert.Contains("| 1 |", result);
		Assert.Contains("| 3 |", result);
	}
}
