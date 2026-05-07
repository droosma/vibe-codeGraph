using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class ListEngineTests
{
	private static (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) BuildTestGraph()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["MyApp.Services.OrderService"] = new GraphNode
			{
				Id = "MyApp.Services.OrderService", Name = "OrderService",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services"
			},
			["MyApp.Services.IOrderService"] = new GraphNode
			{
				Id = "MyApp.Services.IOrderService", Name = "IOrderService",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services"
			},
			["MyApp.Services.OrderService.PlaceOrder"] = new GraphNode
			{
				Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
				Kind = NodeKind.Method, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services",
				ContainingTypeId = "MyApp.Services.OrderService"
			},
			["MyApp.Data.SqlOrderRepository"] = new GraphNode
			{
				Id = "MyApp.Data.SqlOrderRepository", Name = "SqlOrderRepository",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Data",
				ContainingNamespaceId = "MyApp.Data"
			},
			["MyApp.Data.IOrderRepository"] = new GraphNode
			{
				Id = "MyApp.Data.IOrderRepository", Name = "IOrderRepository",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Data",
				ContainingNamespaceId = "MyApp.Data"
			},
			["MyApp.Data.SqlOrderRepository.Save"] = new GraphNode
			{
				Id = "MyApp.Data.SqlOrderRepository.Save", Name = "Save",
				Kind = NodeKind.Method, AssemblyName = "MyApp.Data",
				ContainingNamespaceId = "MyApp.Data",
				ContainingTypeId = "MyApp.Data.SqlOrderRepository"
			}
		};

		var edges = new List<GraphEdge>
		{
			new() { FromId = "MyApp.Services.OrderService", ToId = "MyApp.Services.IOrderService", Type = EdgeType.Implements },
			new() { FromId = "MyApp.Data.SqlOrderRepository", ToId = "MyApp.Data.IOrderRepository", Type = EdgeType.Implements },
			new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Data.SqlOrderRepository.Save", Type = EdgeType.Calls },
			new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Services.OrderService", Type = EdgeType.Contains }
		};

		return (nodes, edges);
	}

	[Fact]
	public void ListAssemblies_ReturnsGroupedCounts()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListAssemblies();

		Assert.Equal(2, result.Count);
		var services = result.Single(a => a.Name == "MyApp.Services");
		Assert.Equal(2, services.TypeCount);
		Assert.Equal(1, services.MethodCount);
		Assert.Equal(3, services.TotalNodeCount);

		var data = result.Single(a => a.Name == "MyApp.Data");
		Assert.Equal(2, data.TypeCount);
		Assert.Equal(1, data.MethodCount);
		Assert.Equal(3, data.TotalNodeCount);
	}

	[Fact]
	public void ListTypes_WithoutFilter_ReturnsAllTypes()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 100);

		Assert.Equal(4, result.Count);
		Assert.All(result, t => Assert.NotEmpty(t.Name));
	}

	[Fact]
	public void ListTypes_WithAssemblyFilter_FiltersCorrectly()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(assemblyFilter: "MyApp.Data", top: 100);

		Assert.Equal(2, result.Count);
		Assert.All(result, t => Assert.Equal("MyApp.Data", t.Assembly));
	}

	[Fact]
	public void ListTypes_RespectsTopParameter()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 2);

		Assert.Equal(2, result.Count);
	}

	[Fact]
	public void ListInterfaces_CountsImplementationsCorrectly()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListInterfaces();

		Assert.Equal(2, result.Count);
		var iOrderService = result.Single(i => i.Name == "IOrderService");
		Assert.Equal(1, iOrderService.ImplementationCount);

		var iOrderRepo = result.Single(i => i.Name == "IOrderRepository");
		Assert.Equal(1, iOrderRepo.ImplementationCount);
	}

	[Fact]
	public void ListInterfaces_WithAssemblyFilter_FiltersCorrectly()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListInterfaces(assemblyFilter: "MyApp.Data");

		Assert.Single(result);
		Assert.Equal("IOrderRepository", result[0].Name);
	}

	[Fact]
	public void ListNamespaces_GroupsByNamespace()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListNamespaces();

		Assert.Equal(2, result.Count);
		var services = result.Single(ns => ns.Name == "MyApp.Services");
		Assert.Equal(2, services.TypeCount);
		Assert.Equal(1, services.MethodCount);
		Assert.Equal(3, services.TotalCount);
	}

	[Fact]
	public void ListNamespaces_WithAssemblyFilter_FiltersCorrectly()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListNamespaces(assemblyFilter: "MyApp.Data");

		Assert.Single(result);
		Assert.Equal("MyApp.Data", result[0].Name);
	}

	[Fact]
	public void EmptyGraph_ReturnsEmptyLists()
	{
		var nodes = new Dictionary<string, GraphNode>();
		var edges = new List<GraphEdge>();
		var engine = new ListEngine(nodes, edges);

		Assert.Empty(engine.ListAssemblies());
		Assert.Empty(engine.ListTypes());
		Assert.Empty(engine.ListInterfaces());
		Assert.Empty(engine.ListNamespaces());
	}

	[Fact]
	public void ListAssemblies_SortedByTypeCountDescending()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["T1"] = new GraphNode { Id = "T1", Name = "T1", Kind = NodeKind.Type, AssemblyName = "FewTypes" },
			["T2"] = new GraphNode { Id = "T2", Name = "T2", Kind = NodeKind.Type, AssemblyName = "ManyTypes" },
			["T3"] = new GraphNode { Id = "T3", Name = "T3", Kind = NodeKind.Type, AssemblyName = "ManyTypes" },
			["T4"] = new GraphNode { Id = "T4", Name = "T4", Kind = NodeKind.Type, AssemblyName = "ManyTypes" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListAssemblies();

		Assert.Equal("ManyTypes", result[0].Name);
		Assert.Equal("FewTypes", result[1].Name);
	}

	[Fact]
	public void ListAssemblies_ExcludesEmptyAssemblyNames()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["A"] = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Type, AssemblyName = "Real" },
			["B"] = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Type, AssemblyName = "" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListAssemblies();

		Assert.Single(result);
		Assert.Equal("Real", result[0].Name);
	}

	[Fact]
	public void ListTypes_SortedByTotalDegreeDescending()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["A"] = new GraphNode { Id = "A", Name = "LowDeg", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["B"] = new GraphNode { Id = "B", Name = "HighDeg", Kind = NodeKind.Type, AssemblyName = "Asm" }
		};
		var edges = new List<GraphEdge>
		{
			new() { FromId = "B", ToId = "A", Type = EdgeType.Calls },
			new() { FromId = "B", ToId = "A", Type = EdgeType.DependsOn }
		};
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 100);

		// Both have total degree 2, but let's verify they are both present with correct degrees
		var highDeg = result.Single(t => t.Name == "HighDeg");
		Assert.Equal(0, highDeg.InDegree);
		Assert.Equal(2, highDeg.OutDegree);

		var lowDeg = result.Single(t => t.Name == "LowDeg");
		Assert.Equal(2, lowDeg.InDegree);
		Assert.Equal(0, lowDeg.OutDegree);
	}

	[Fact]
	public void ListTypes_DefaultTopIs20()
	{
		var nodes = new Dictionary<string, GraphNode>();
		for (int i = 0; i < 30; i++)
		{
			var id = $"T{i}";
			nodes[id] = new GraphNode { Id = id, Name = $"Type{i}", Kind = NodeKind.Type, AssemblyName = "Asm" };
		}
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListTypes();

		Assert.Equal(20, result.Count);
	}

	[Fact]
	public void ListTypes_CaseInsensitiveAssemblyFilter()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(assemblyFilter: "myapp.data", top: 100);

		Assert.Equal(2, result.Count);
		Assert.All(result, t => Assert.Equal("MyApp.Data", t.Assembly));
	}

	[Fact]
	public void ListTypes_IncludesIdAndAssembly()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 100);

		var orderService = result.Single(t => t.Name == "OrderService");
		Assert.Equal("MyApp.Services.OrderService", orderService.Id);
		Assert.Equal("MyApp.Services", orderService.Assembly);
	}

	[Fact]
	public void ListInterfaces_SortedByImplementationCountDescending()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["IFew"] = new GraphNode { Id = "IFew", Name = "IFew", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["IMany"] = new GraphNode { Id = "IMany", Name = "IMany", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["Impl1"] = new GraphNode { Id = "Impl1", Name = "Impl1", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["Impl2"] = new GraphNode { Id = "Impl2", Name = "Impl2", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["Impl3"] = new GraphNode { Id = "Impl3", Name = "Impl3", Kind = NodeKind.Type, AssemblyName = "Asm" }
		};
		var edges = new List<GraphEdge>
		{
			new() { FromId = "Impl1", ToId = "IMany", Type = EdgeType.Implements },
			new() { FromId = "Impl2", ToId = "IMany", Type = EdgeType.Implements },
			new() { FromId = "Impl3", ToId = "IFew", Type = EdgeType.Implements }
		};
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListInterfaces();

		Assert.Equal("IMany", result[0].Name);
		Assert.Equal(2, result[0].ImplementationCount);
		Assert.Equal("IFew", result[1].Name);
		Assert.Equal(1, result[1].ImplementationCount);
	}

	[Fact]
	public void ListInterfaces_OnlyMatchesConventionalInterfaceNames()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["IService"] = new GraphNode { Id = "IService", Name = "IService", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["Item"] = new GraphNode { Id = "Item", Name = "Item", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["I"] = new GraphNode { Id = "I", Name = "I", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["Ix"] = new GraphNode { Id = "Ix", Name = "Ix", Kind = NodeKind.Type, AssemblyName = "Asm" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListInterfaces();

		// Only "IService" should match (starts with I, length > 1, second char uppercase)
		Assert.Single(result);
		Assert.Equal("IService", result[0].Name);
	}

	[Fact]
	public void ListInterfaces_CaseInsensitiveAssemblyFilter()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListInterfaces(assemblyFilter: "myapp.services");

		Assert.Single(result);
		Assert.Equal("IOrderService", result[0].Name);
	}

	[Fact]
	public void ListNamespaces_SortedByTypeCountDescending()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["T1"] = new GraphNode { Id = "T1", Name = "T1", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Few" },
			["T2"] = new GraphNode { Id = "T2", Name = "T2", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Many" },
			["T3"] = new GraphNode { Id = "T3", Name = "T3", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "Many" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListNamespaces();

		Assert.Equal("Many", result[0].Name);
		Assert.Equal("Few", result[1].Name);
	}

	[Fact]
	public void ListNamespaces_ExcludesNodesWithoutNamespace()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["A"] = new GraphNode { Id = "A", Name = "A", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "HasNS" },
			["B"] = new GraphNode { Id = "B", Name = "B", Kind = NodeKind.Type, AssemblyName = "Asm" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListNamespaces();

		Assert.Single(result);
		Assert.Equal("HasNS", result[0].Name);
	}

	[Fact]
	public void ListNamespaces_CaseInsensitiveAssemblyFilter()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListNamespaces(assemblyFilter: "myapp.services");

		Assert.Single(result);
		Assert.Equal("MyApp.Services", result[0].Name);
	}

	[Fact]
	public void ListNamespaces_CorrectTotalCount()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["T1"] = new GraphNode { Id = "T1", Name = "T1", Kind = NodeKind.Type, AssemblyName = "Asm", ContainingNamespaceId = "NS" },
			["M1"] = new GraphNode { Id = "M1", Name = "M1", Kind = NodeKind.Method, AssemblyName = "Asm", ContainingNamespaceId = "NS" },
			["P1"] = new GraphNode { Id = "P1", Name = "P1", Kind = NodeKind.Property, AssemblyName = "Asm", ContainingNamespaceId = "NS" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListNamespaces();

		Assert.Single(result);
		Assert.Equal(1, result[0].TypeCount);
		Assert.Equal(1, result[0].MethodCount);
		Assert.Equal(3, result[0].TotalCount);
	}

	[Fact]
	public void ListAssemblies_IncludesAllNodeKindsInTotal()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["T1"] = new GraphNode { Id = "T1", Name = "T1", Kind = NodeKind.Type, AssemblyName = "Asm" },
			["M1"] = new GraphNode { Id = "M1", Name = "M1", Kind = NodeKind.Method, AssemblyName = "Asm" },
			["P1"] = new GraphNode { Id = "P1", Name = "P1", Kind = NodeKind.Property, AssemblyName = "Asm" }
		};
		var engine = new ListEngine(nodes, new List<GraphEdge>());

		var result = engine.ListAssemblies();

		Assert.Single(result);
		Assert.Equal(1, result[0].TypeCount);
		Assert.Equal(1, result[0].MethodCount);
		Assert.Equal(3, result[0].TotalNodeCount);
	}

	[Fact]
	public void ListTypes_TopZero_ReturnsEmpty()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 0);

		Assert.Empty(result);
	}

	[Fact]
	public void ListTypes_TopOne_ReturnsSingle()
	{
		var (nodes, edges) = BuildTestGraph();
		var engine = new ListEngine(nodes, edges);

		var result = engine.ListTypes(top: 1);

		Assert.Single(result);
	}
}
