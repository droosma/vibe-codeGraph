using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.Tests;

public class SearchTests
{
	private static QueryEngine BuildTestEngine()
	{
		var nodes = new Dictionary<string, GraphNode>
		{
			["MyApp.Services.OrderService"] = new GraphNode
			{
				Id = "MyApp.Services.OrderService", Name = "OrderService",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services",
				FilePath = "src/Services/OrderService.cs"
			},
			["MyApp.Services.IOrderService"] = new GraphNode
			{
				Id = "MyApp.Services.IOrderService", Name = "IOrderService",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services",
				FilePath = "src/Services/IOrderService.cs"
			},
			["MyApp.Services.OrderService.PlaceOrder"] = new GraphNode
			{
				Id = "MyApp.Services.OrderService.PlaceOrder", Name = "PlaceOrder",
				Kind = NodeKind.Method, AssemblyName = "MyApp.Services",
				ContainingNamespaceId = "MyApp.Services",
				ContainingTypeId = "MyApp.Services.OrderService",
				FilePath = "src/Services/OrderService.cs"
			},
			["MyApp.Data.SqlOrderRepository"] = new GraphNode
			{
				Id = "MyApp.Data.SqlOrderRepository", Name = "SqlOrderRepository",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Data",
				ContainingNamespaceId = "MyApp.Data",
				FilePath = "src/Data/SqlOrderRepository.cs"
			},
			["MyApp.Financial.PaymentProcessor"] = new GraphNode
			{
				Id = "MyApp.Financial.PaymentProcessor", Name = "PaymentProcessor",
				Kind = NodeKind.Type, AssemblyName = "MyApp.Financial",
				ContainingNamespaceId = "MyApp.Financial",
				FilePath = "src/Financial/PaymentProcessor.cs"
			},
			["MyApp.Financial.PaymentProcessor.Process"] = new GraphNode
			{
				Id = "MyApp.Financial.PaymentProcessor.Process", Name = "Process",
				Kind = NodeKind.Method, AssemblyName = "MyApp.Financial",
				ContainingNamespaceId = "MyApp.Financial",
				ContainingTypeId = "MyApp.Financial.PaymentProcessor",
				FilePath = "src/Financial/PaymentProcessor.cs"
			}
		};

		var edges = new List<GraphEdge>
		{
			new() { FromId = "MyApp.Services.OrderService", ToId = "MyApp.Services.IOrderService", Type = EdgeType.Implements },
			new() { FromId = "MyApp.Services.OrderService.PlaceOrder", ToId = "MyApp.Financial.PaymentProcessor.Process", Type = EdgeType.Calls }
		};

		var metadata = new GraphMetadata();
		return new QueryEngine(nodes, edges, metadata);
	}

	[Fact]
	public void Search_ByName_FindsMatchingNodes()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Order");

		Assert.True(results.Count >= 3);
		Assert.Contains(results, n => n.Name == "OrderService");
		Assert.Contains(results, n => n.Name == "IOrderService");
		Assert.Contains(results, n => n.Name == "SqlOrderRepository");
	}

	[Fact]
	public void Search_IsCaseInsensitive()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("order");

		Assert.True(results.Count >= 3);
		Assert.Contains(results, n => n.Name == "OrderService");
	}

	[Fact]
	public void Search_ByNamespace_FindsNodesInNamespace()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Financial");

		Assert.True(results.Count >= 2);
		Assert.Contains(results, n => n.Name == "PaymentProcessor");
		Assert.Contains(results, n => n.Name == "Process");
	}

	[Fact]
	public void Search_ByFilePath_FindsNodesInFile()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("PaymentProcessor.cs");

		Assert.True(results.Count >= 1);
		Assert.Contains(results, n => n.Name == "PaymentProcessor");
	}

	[Fact]
	public void Search_TypesAppearFirst()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Order");

		// Types should come before methods
		var firstMethod = results.FindIndex(n => n.Kind == NodeKind.Method);
		var lastType = results.FindLastIndex(n => n.Kind == NodeKind.Type);
		if (firstMethod >= 0 && lastType >= 0)
			Assert.True(lastType < firstMethod, "Types should appear before methods");
	}

	[Fact]
	public void Search_RespectsMaxResults()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("MyApp", maxResults: 2);

		Assert.Equal(2, results.Count);
	}

	[Fact]
	public void Search_WithKindFilter_ReturnsOnlyMatchingKind()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Order", kindFilter: NodeKind.Method);

		Assert.All(results, n => Assert.Equal(NodeKind.Method, n.Kind));
		Assert.Contains(results, n => n.Name == "PlaceOrder");
	}

	[Fact]
	public void Search_WithKindFilterType_ExcludesMethods()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Order", kindFilter: NodeKind.Type);

		Assert.All(results, n => Assert.Equal(NodeKind.Type, n.Kind));
		Assert.DoesNotContain(results, n => n.Name == "PlaceOrder");
	}

	[Fact]
	public void Search_EmptyQuery_ReturnsEmpty()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("");

		Assert.Empty(results);
	}

	[Fact]
	public void Search_WhitespaceQuery_ReturnsEmpty()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("   ");

		Assert.Empty(results);
	}

	[Fact]
	public void Search_NoMatch_ReturnsEmpty()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("XYZNonexistent");

		Assert.Empty(results);
	}

	[Fact]
	public void Search_ShorterNamesRankedHigher()
	{
		var engine = BuildTestEngine();

		var results = engine.Search("Order", kindFilter: NodeKind.Type);

		// Among types, shorter names should come first
		for (int i = 0; i < results.Count - 1; i++)
		{
			Assert.True(results[i].Name.Length <= results[i + 1].Name.Length,
				$"Expected {results[i].Name} (len {results[i].Name.Length}) <= {results[i + 1].Name} (len {results[i + 1].Name.Length})");
		}
	}
}
