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

	[Fact]
	public void Generate_ContainsCodeWikiSuffix()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("— Code Wiki", result);
	}

	[Fact]
	public void Generate_ContainsDateAndCommitHash()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("2025-01-01", result);
		Assert.Contains("abc123", result);
		Assert.Contains("Generated from CodeGraph", result);
	}

	[Fact]
	public void Generate_ContainsAssembliesHeader()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("## Assemblies", result);
	}

	[Fact]
	public void Generate_AssemblyTableHeaders_ExactFormat()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("| Assembly | Types | Methods | Total |", result);
		Assert.Contains("|----------|------:|--------:|------:|", result);
	}

	[Fact]
	public void Generate_AssemblyLinks_CorrectMarkdownFormat()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "MyAssembly"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("[MyAssembly](assemblies/MyAssembly.md)", result);
	}

	[Fact]
	public void Generate_MultipleAssemblies_SortedAlphabetically()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Zebra"),
			("B", "TypeB", NodeKind.Type, "Alpha"),
			("C", "TypeC", NodeKind.Type, "Middle"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		var alphaIndex = result.IndexOf("Alpha");
		var middleIndex = result.IndexOf("Middle");
		var zebraIndex = result.IndexOf("Zebra");
		Assert.True(alphaIndex < middleIndex, "Alpha should come before Middle");
		Assert.True(middleIndex < zebraIndex, "Middle should come before Zebra");
	}

	[Fact]
	public void Generate_ExcludesNodesWithEmptyAssembly()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["A"] = new GraphNode { Id = "A", Name = "TypeA", Kind = NodeKind.Type, AssemblyName = "Asm1" },
			["B"] = new GraphNode { Id = "B", Name = "TypeB", Kind = NodeKind.Type, AssemblyName = "" }
		};
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("Asm1", result);
		// Empty assembly should not have a row
		var lines = result.Split('\n');
		var dataRows = lines.Where(l => l.StartsWith("| [")).ToList();
		Assert.Single(dataRows);
	}

	[Fact]
	public void Generate_NavigationSection_Header()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		Assert.Contains("## Navigation", result);
	}

	[Fact]
	public void Generate_MultiAssembly_CorrectRowCounts()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "AsmA"),
			("T2", "Type2", NodeKind.Type, "AsmA"),
			("M1", "Meth1", NodeKind.Method, "AsmA"),
			("T3", "Type3", NodeKind.Type, "AsmB"));
		var edges = new List<GraphEdge>();

		var result = IndexPageGenerator.Generate(nodes, edges, CreateMetadata());

		// AsmA: 2 types, 1 method, 3 total
		Assert.Contains("| [AsmA](assemblies/AsmA.md) | 2 | 1 | 3 |", result);
		// AsmB: 1 type, 0 methods, 1 total
		Assert.Contains("| [AsmB](assemblies/AsmB.md) | 1 | 0 | 1 |", result);
	}
}
