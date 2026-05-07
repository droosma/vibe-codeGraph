using CodeGraph.Core.Models;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests.Wiki;

public class DiWiringPageGeneratorTests
{
	private static Dictionary<string, GraphNode> CreateNodes(
		params (string id, string name)[] defs)
	{
		var nodes = new Dictionary<string, GraphNode>();
		foreach (var (id, name) in defs)
			nodes[id] = new GraphNode
			{
				Id = id,
				Name = name,
				Kind = NodeKind.Type,
				AssemblyName = "Asm"
			};
		return nodes;
	}

	[Fact]
	public void Generate_ContainsHeader()
	{
		var nodes = CreateNodes();
		var edges = new List<GraphEdge>();

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.StartsWith("# DI Wiring", result);
	}

	[Fact]
	public void Generate_ContainsBackToIndexLink()
	{
		var nodes = CreateNodes();
		var edges = new List<GraphEdge>();

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("[← Back to Index](INDEX.md)", result);
	}

	[Fact]
	public void Generate_NoResolvesTo_ShowsMessage()
	{
		var nodes = CreateNodes(("A", "TypeA"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("No DI wiring (ResolvesTo edges) found in the graph.", result);
		Assert.DoesNotContain("| Interface |", result);
	}

	[Fact]
	public void Generate_EmptyEdges_ShowsNoWiringMessage()
	{
		var nodes = CreateNodes();
		var edges = new List<GraphEdge>();

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("No DI wiring (ResolvesTo edges) found in the graph.", result);
	}

	[Fact]
	public void Generate_WithResolvesTo_ShowsTable()
	{
		var nodes = CreateNodes(("IFoo", "IFoo"), ("FooImpl", "FooImpl"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "IFoo", ToId = "FooImpl", Type = EdgeType.ResolvesTo, Resolution = "Singleton" }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("| Interface | Implementation | Resolution |", result);
		Assert.Contains("|-----------|----------------|------------|", result);
		Assert.Contains("| IFoo | FooImpl | Singleton |", result);
	}

	[Fact]
	public void Generate_ResolvesTo_UsesNodeNamesNotIds()
	{
		var nodes = CreateNodes(
			("Ns.IService", "IService"),
			("Ns.ServiceImpl", "ServiceImpl"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "Ns.IService", ToId = "Ns.ServiceImpl", Type = EdgeType.ResolvesTo, Resolution = "Scoped" }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("| IService | ServiceImpl | Scoped |", result);
		Assert.DoesNotContain("Ns.IService", result);
		Assert.DoesNotContain("Ns.ServiceImpl", result);
	}

	[Fact]
	public void Generate_ResolvesTo_FallsBackToIdWhenNodeMissing()
	{
		var nodes = new Dictionary<string, GraphNode>();
		var edges = new List<GraphEdge>
		{
			new() { FromId = "Unknown.IFoo", ToId = "Unknown.Foo", Type = EdgeType.ResolvesTo, Resolution = "Transient" }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("| Unknown.IFoo | Unknown.Foo | Transient |", result);
	}

	[Fact]
	public void Generate_ResolvesTo_NoResolution_ShowsConfidence()
	{
		var nodes = CreateNodes(("IBar", "IBar"), ("BarImpl", "BarImpl"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "IBar", ToId = "BarImpl", Type = EdgeType.ResolvesTo, Confidence = EdgeConfidence.Inferred }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("| IBar | BarImpl | Inferred |", result);
	}

	[Fact]
	public void Generate_ResolvesTo_EmptyResolution_ShowsConfidence()
	{
		var nodes = CreateNodes(("IBar", "IBar"), ("BarImpl", "BarImpl"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "IBar", ToId = "BarImpl", Type = EdgeType.ResolvesTo, Resolution = "", Confidence = EdgeConfidence.Verified }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.Contains("| IBar | BarImpl | Verified |", result);
	}

	[Fact]
	public void Generate_MultipleResolvesTo_SortedByFromName()
	{
		var nodes = CreateNodes(
			("IZebra", "IZebra"),
			("IAlpha", "IAlpha"),
			("ZebraImpl", "ZebraImpl"),
			("AlphaImpl", "AlphaImpl"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "IZebra", ToId = "ZebraImpl", Type = EdgeType.ResolvesTo, Resolution = "Singleton" },
			new() { FromId = "IAlpha", ToId = "AlphaImpl", Type = EdgeType.ResolvesTo, Resolution = "Scoped" }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		var alphaIndex = result.IndexOf("IAlpha");
		var zebraIndex = result.IndexOf("IZebra");
		Assert.True(alphaIndex < zebraIndex, "Table should be sorted by interface name");
	}

	[Fact]
	public void Generate_MixedEdgeTypes_OnlyShowsResolvesTo()
	{
		var nodes = CreateNodes(("IFoo", "IFoo"), ("Foo", "Foo"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "IFoo", ToId = "Foo", Type = EdgeType.ResolvesTo, Resolution = "Singleton" },
			new() { FromId = "IFoo", ToId = "Foo", Type = EdgeType.Implements },
			new() { FromId = "IFoo", ToId = "Foo", Type = EdgeType.Calls }
		};

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		// Only one data row for the ResolvesTo edge
		var tableRows = result.Split('\n')
			.Where(l => l.StartsWith("| ") && !l.StartsWith("| Interface") && !l.StartsWith("|---"))
			.ToList();
		Assert.Single(tableRows);
	}

	[Fact]
	public void Generate_DoesNotContainTableHeaders_WhenNoResolvesTo()
	{
		var nodes = CreateNodes(("A", "TypeA"));
		var edges = new List<GraphEdge>();

		var result = DiWiringPageGenerator.Generate(nodes, edges);

		Assert.DoesNotContain("| Interface | Implementation | Resolution |", result);
		Assert.DoesNotContain("|-----------|----------------|------------|", result);
	}
}
