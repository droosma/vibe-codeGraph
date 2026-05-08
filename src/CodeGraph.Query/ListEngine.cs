using CodeGraph.Core.Models;

namespace CodeGraph.Query;

public class ListEngine
{
	private readonly Dictionary<string, GraphNode> _nodes;
	private readonly List<GraphEdge> _edges;

	public ListEngine(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
	{
		_nodes = nodes;
		_edges = edges;
	}

	/// <summary>List all assemblies with type/method counts.</summary>
	public List<AssemblyInfo> ListAssemblies()
	{
		return _nodes.Values
			.GroupBy(n => n.AssemblyName)
			.Where(g => !string.IsNullOrEmpty(g.Key))
			.Select(g => new AssemblyInfo
			{
				Name = g.Key,
				TypeCount = g.Count(n => n.Kind == NodeKind.Type),
				MethodCount = g.Count(n => n.Kind == NodeKind.Method),
				TotalNodeCount = g.Count()
			})
			.OrderByDescending(a => a.TypeCount)
			.ToList();
	}

	/// <summary>List types, optionally filtered by assembly, with pagination and name filtering.</summary>
	public ListTypesResult ListTypes(string? assemblyFilter = null, int top = 20, int skip = 0, string? filter = null)
	{
		var types = _nodes.Values.Where(n => n.Kind == NodeKind.Type);

		if (!string.IsNullOrEmpty(assemblyFilter))
			types = types.Where(n => n.AssemblyName.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrEmpty(filter))
			types = types.Where(n => n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

		var ranked = types.Select(t => new TypeInfo
			{
				Id = t.Id,
				Name = t.Name,
				Assembly = t.AssemblyName,
				InDegree = _edges.Count(e => e.ToId == t.Id),
				OutDegree = _edges.Count(e => e.FromId == t.Id)
			})
			.OrderByDescending(t => t.InDegree + t.OutDegree)
			.ToList();

		var totalCount = ranked.Count;
		var page = ranked.Skip(skip).Take(top).ToList();

		return new ListTypesResult
		{
			Types = page,
			TotalCount = totalCount,
			Skip = skip,
			Top = top
		};
	}

	/// <summary>List all interfaces with implementation counts.</summary>
	public List<InterfaceInfo> ListInterfaces(string? assemblyFilter = null)
	{
		var interfaces = _nodes.Values
			.Where(n => n.Kind == NodeKind.Type && n.Name.StartsWith("I") && n.Name.Length > 1 && char.IsUpper(n.Name[1]));

		if (!string.IsNullOrEmpty(assemblyFilter))
			interfaces = interfaces.Where(n => n.AssemblyName.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase));

		return interfaces.Select(i => new InterfaceInfo
			{
				Id = i.Id,
				Name = i.Name,
				Assembly = i.AssemblyName,
				ImplementationCount = _edges.Count(e => e.ToId == i.Id && e.Type == EdgeType.Implements)
			})
			.OrderByDescending(i => i.ImplementationCount)
			.ToList();
	}

	/// <summary>List namespaces with type counts.</summary>
	public List<NamespaceInfo> ListNamespaces(string? assemblyFilter = null)
	{
		var nodes = _nodes.Values.AsEnumerable();
		if (!string.IsNullOrEmpty(assemblyFilter))
			nodes = nodes.Where(n => n.AssemblyName.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase));

		return nodes
			.Where(n => !string.IsNullOrEmpty(n.ContainingNamespaceId))
			.GroupBy(n => n.ContainingNamespaceId!)
			.Select(g => new NamespaceInfo
			{
				Name = g.Key,
				TypeCount = g.Count(n => n.Kind == NodeKind.Type),
				MethodCount = g.Count(n => n.Kind == NodeKind.Method),
				TotalCount = g.Count()
			})
			.OrderByDescending(ns => ns.TypeCount)
			.ToList();
	}
}

public record AssemblyInfo
{
	public string Name { get; init; } = string.Empty;
	public int TypeCount { get; init; }
	public int MethodCount { get; init; }
	public int TotalNodeCount { get; init; }
}

public record TypeInfo
{
	public string Id { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string Assembly { get; init; } = string.Empty;
	public int InDegree { get; init; }
	public int OutDegree { get; init; }
}

public record InterfaceInfo
{
	public string Id { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string Assembly { get; init; } = string.Empty;
	public int ImplementationCount { get; init; }
}

public record NamespaceInfo
{
	public string Name { get; init; } = string.Empty;
	public int TypeCount { get; init; }
	public int MethodCount { get; init; }
	public int TotalCount { get; init; }
}

public record ListTypesResult
{
	public List<TypeInfo> Types { get; init; } = [];
	public int TotalCount { get; init; }
	public int Skip { get; init; }
	public int Top { get; init; }
}
