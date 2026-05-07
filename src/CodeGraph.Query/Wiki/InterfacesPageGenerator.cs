using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Wiki;

public static class InterfacesPageGenerator
{
	public static string Generate(
		Dictionary<string, GraphNode> nodes,
		List<GraphEdge> edges)
	{
		var sb = new StringBuilder();
		sb.AppendLine("# Interfaces");
		sb.AppendLine();
		sb.AppendLine("[← Back to Index](INDEX.md)");
		sb.AppendLine();

		var interfaces = nodes.Values
			.Where(n => n.Kind == NodeKind.Type && n.Name.StartsWith('I') && n.Name.Length > 1 && char.IsUpper(n.Name[1]));

		var implementsEdges = edges
			.Where(e => e.Type == EdgeType.Implements)
			.ToLookup(e => e.ToId);

		var interfaceInfos = interfaces
			.Select(iface => new
			{
				Interface = iface,
				Implementations = implementsEdges[iface.Id]
					.Select(e => nodes.GetValueOrDefault(e.FromId))
					.Where(n => n is not null)
					.ToList()
			})
			.OrderByDescending(x => x.Implementations.Count)
			.ThenBy(x => x.Interface.Name)
			.ToList();

		if (interfaceInfos.Count == 0)
		{
			sb.AppendLine("No interfaces found in the graph.");
			return sb.ToString().TrimEnd();
		}

		sb.AppendLine("| Interface | Implementations | Assembly |");
		sb.AppendLine("|-----------|----------------:|----------|");
		foreach (var info in interfaceInfos)
		{
			var implNames = info.Implementations.Count > 0
				? string.Join(", ", info.Implementations.Select(n => n!.Name))
				: "—";
			sb.AppendLine($"| {info.Interface.Name} | {info.Implementations.Count} | {info.Interface.AssemblyName} |");
		}
		sb.AppendLine();

		// Detail section for interfaces with implementations
		var withImpls = interfaceInfos.Where(x => x.Implementations.Count > 0).ToList();
		if (withImpls.Count > 0)
		{
			sb.AppendLine("## Implementation Details");
			sb.AppendLine();
			foreach (var info in withImpls)
			{
				sb.AppendLine($"### {info.Interface.Name}");
				sb.AppendLine();
				foreach (var impl in info.Implementations)
				{
					var assemblyFile = WikiGenerator.SanitizeFileName(impl!.AssemblyName);
					sb.AppendLine($"- [{impl.Name}](assemblies/{assemblyFile}.md) ({impl.AssemblyName})");
				}
				sb.AppendLine();
			}
		}

		return sb.ToString().TrimEnd();
	}
}
