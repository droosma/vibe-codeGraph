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
}
