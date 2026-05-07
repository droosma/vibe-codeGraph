using CodeGraph.Core.Models;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests.Wiki;

public class AssemblyPageGeneratorTests
{
	private static Dictionary<string, GraphNode> CreateNodes(
		params (string id, string name, NodeKind kind, string assembly, string ns, string? filePath)[] defs)
	{
		var nodes = new Dictionary<string, GraphNode>();
		foreach (var (id, name, kind, assembly, ns, filePath) in defs)
			nodes[id] = new GraphNode
			{
				Id = id,
				Name = name,
				Kind = kind,
				AssemblyName = assembly,
				ContainingNamespaceId = ns,
				FilePath = filePath ?? string.Empty
			};
		return nodes;
	}

	[Fact]
	public void Generate_ContainsAssemblyHeader()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "MyAssembly", "NS1", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("MyAssembly", nodes, edges);

		Assert.StartsWith("# MyAssembly", result);
	}

	[Fact]
	public void Generate_ContainsBackToIndexLink()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm", "NS1", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("[← Back to Index](../INDEX.md)", result);
	}

	[Fact]
	public void Generate_ContainsNamespacesSection_WhenNodesHaveNamespaces()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "Asm", "Alpha", null),
			("M1", "Method1", NodeKind.Method, "Asm", "Alpha", null),
			("T2", "Type2", NodeKind.Type, "Asm", "Beta", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("## Namespaces", result);
		Assert.Contains("| Namespace | Types | Methods |", result);
		Assert.Contains("|-----------|------:|--------:|", result);
	}

	[Fact]
	public void Generate_NamespaceTable_ShowsCorrectCounts()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "Asm", "Alpha", null),
			("T2", "Type2", NodeKind.Type, "Asm", "Alpha", null),
			("M1", "Method1", NodeKind.Method, "Asm", "Alpha", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("| Alpha | 2 | 1 |", result);
	}

	[Fact]
	public void Generate_NamespaceTable_SortsByNamespace()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "Asm", "Zebra", null),
			("T2", "Type2", NodeKind.Type, "Asm", "Alpha", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		var alphaIndex = result.IndexOf("Alpha");
		var zebraIndex = result.IndexOf("Zebra");
		Assert.True(alphaIndex < zebraIndex, "Namespaces should be sorted alphabetically");
	}

	[Fact]
	public void Generate_OmitsNamespaceSection_WhenNoNamespaces()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["A"] = new GraphNode { Id = "A", Name = "TypeA", Kind = NodeKind.Type, AssemblyName = "Asm" }
		};
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.DoesNotContain("## Namespaces", result);
	}

	[Fact]
	public void Generate_ContainsTypesSection_WhenTypesExist()
	{
		var nodes = CreateNodes(("T1", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("## Types", result);
		Assert.Contains("| Type | In | Out | File |", result);
		Assert.Contains("|------|---:|----:|------|", result);
	}

	[Fact]
	public void Generate_TypesTable_ShowsCorrectDegrees()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null),
			("B", "TypeB", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "B", Type = EdgeType.Calls },
			new() { FromId = "A", ToId = "B", Type = EdgeType.DependsOn }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		// TypeA: In=0, Out=2
		Assert.Contains("| TypeA | 0 | 2 |", result);
		// TypeB: In=2, Out=0
		Assert.Contains("| TypeB | 2 | 0 |", result);
	}

	[Fact]
	public void Generate_TypesTable_ShowsFilePath()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", "src/TypeA.cs"));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("src/TypeA.cs", result);
	}

	[Fact]
	public void Generate_TypesTable_EmptyFilePath_ShowsBlank()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("| TypeA | 0 | 0 |  |", result);
	}

	[Fact]
	public void Generate_TypesTable_SortedByTotalDegreeDescending()
	{
		var nodes = CreateNodes(
			("A", "LowDegree", NodeKind.Type, "Asm", "NS", null),
			("B", "HighDegree", NodeKind.Type, "Asm", "NS", null),
			("C", "Helper", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "B", ToId = "A", Type = EdgeType.Calls },
			new() { FromId = "B", ToId = "C", Type = EdgeType.DependsOn },
			new() { FromId = "C", ToId = "B", Type = EdgeType.Calls }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		// HighDegree (B): In=1, Out=2, Total=3
		// LowDegree (A): In=1, Out=0, Total=1
		var typesSection = result.Substring(result.IndexOf("## Types"));
		var highIndex = typesSection.IndexOf("HighDegree");
		var lowIndex = typesSection.IndexOf("LowDegree");
		Assert.True(highIndex < lowIndex, "Higher-degree types should appear first");
	}

	[Fact]
	public void Generate_OmitsTypesSection_WhenNoTypes()
	{
		var nodes = CreateNodes(
			("M1", "Method1", NodeKind.Method, "Asm", "NS", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.DoesNotContain("## Types", result);
	}

	[Fact]
	public void Generate_CrossAssemblyReferences_ShownWhenPresent()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "ExternalId", Type = EdgeType.Calls }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("## Cross-Assembly References", result);
		Assert.Contains("This assembly has 1 cross-boundary connections.", result);
	}

	[Fact]
	public void Generate_CrossAssemblyReferences_CorrectCount()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "Ext1", Type = EdgeType.Calls },
			new() { FromId = "A", ToId = "Ext2", Type = EdgeType.DependsOn },
			new() { FromId = "Ext3", ToId = "A", Type = EdgeType.Calls }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("This assembly has 3 cross-boundary connections.", result);
	}

	[Fact]
	public void Generate_CrossAssemblyReferences_DeduplicatesExternalIds()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "Ext1", Type = EdgeType.Calls },
			new() { FromId = "A", ToId = "Ext1", Type = EdgeType.DependsOn }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("This assembly has 1 cross-boundary connections.", result);
	}

	[Fact]
	public void Generate_NoCrossAssemblyReferences_SectionOmitted()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null),
			("B", "TypeB", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.DoesNotContain("## Cross-Assembly References", result);
	}

	[Fact]
	public void Generate_EmptyGraph_ContainsOnlyHeaderAndBackLink()
	{
		var nodes = new Dictionary<string, GraphNode>();
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("EmptyAsm", nodes, edges);

		Assert.Contains("# EmptyAsm", result);
		Assert.Contains("[← Back to Index](../INDEX.md)", result);
		Assert.DoesNotContain("## Namespaces", result);
		Assert.DoesNotContain("## Types", result);
		Assert.DoesNotContain("## Cross-Assembly References", result);
	}

	[Fact]
	public void Generate_MultipleNamespaces_EachHasOwnRow()
	{
		var nodes = CreateNodes(
			("T1", "Type1", NodeKind.Type, "Asm", "NS.Alpha", null),
			("T2", "Type2", NodeKind.Type, "Asm", "NS.Beta", null),
			("T3", "Type3", NodeKind.Type, "Asm", "NS.Gamma", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("| NS.Alpha | 1 | 0 |", result);
		Assert.Contains("| NS.Beta | 1 | 0 |", result);
		Assert.Contains("| NS.Gamma | 1 | 0 |", result);
	}

	[Fact]
	public void Generate_CrossRef_FromExternalToInternal_DetectedCorrectly()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm", "NS", null));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "ExternalNode", ToId = "A", Type = EdgeType.Calls }
		};

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		Assert.Contains("## Cross-Assembly References", result);
		Assert.Contains("1 cross-boundary connections", result);
	}

	[Fact]
	public void Generate_MethodsNotInTypesTable()
	{
		var nodes = CreateNodes(
			("T1", "TypeA", NodeKind.Type, "Asm", "NS", null),
			("M1", "MethodA", NodeKind.Method, "Asm", "NS", null));
		var edges = new List<GraphEdge>();

		var result = AssemblyPageGenerator.Generate("Asm", nodes, edges);

		// Types table should only include TypeA
		Assert.Contains("| TypeA |", result);
		// MethodA should not appear in the Types table rows
		var typesSection = result.Substring(result.IndexOf("## Types"));
		Assert.DoesNotContain("| MethodA |", typesSection);
	}
}
