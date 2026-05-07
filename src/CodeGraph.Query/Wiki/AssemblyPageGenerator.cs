using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Wiki;

public static class AssemblyPageGenerator
{
	public static string Generate(
		string assemblyName,
		Dictionary<string, GraphNode> assemblyNodes,
		List<GraphEdge> assemblyEdges)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"# {assemblyName}");
		sb.AppendLine();
		sb.AppendLine($"[← Back to Index](../INDEX.md)");
		sb.AppendLine();

		// Namespace summary
		var namespaces = assemblyNodes.Values
			.Where(n => !string.IsNullOrEmpty(n.ContainingNamespaceId))
			.GroupBy(n => n.ContainingNamespaceId!)
			.OrderBy(g => g.Key);

		if (namespaces.Any())
		{
			sb.AppendLine("## Namespaces");
			sb.AppendLine();
			sb.AppendLine("| Namespace | Types | Methods |");
			sb.AppendLine("|-----------|------:|--------:|");
			foreach (var ns in namespaces)
			{
				var types = ns.Count(n => n.Kind == NodeKind.Type);
				var methods = ns.Count(n => n.Kind == NodeKind.Method);
				sb.AppendLine($"| {ns.Key} | {types} | {methods} |");
			}
			sb.AppendLine();
		}

		// Types sorted by degree
		var typeNodes = assemblyNodes.Values
			.Where(n => n.Kind == NodeKind.Type)
			.Select(n => new
			{
				Node = n,
				InDegree = assemblyEdges.Count(e => e.ToId == n.Id),
				OutDegree = assemblyEdges.Count(e => e.FromId == n.Id)
			})
			.OrderByDescending(t => t.InDegree + t.OutDegree)
			.ToList();

		if (typeNodes.Count > 0)
		{
			sb.AppendLine("## Types");
			sb.AppendLine();
			sb.AppendLine("| Type | In | Out | File |");
			sb.AppendLine("|------|---:|----:|------|");
			foreach (var t in typeNodes)
			{
				var file = string.IsNullOrEmpty(t.Node.FilePath) ? "" : t.Node.FilePath;
				sb.AppendLine($"| {t.Node.Name} | {t.InDegree} | {t.OutDegree} | {file} |");
			}
			sb.AppendLine();
		}

		// Cross-assembly references
		var crossRefs = assemblyEdges
			.Where(e => !assemblyNodes.ContainsKey(e.FromId) || !assemblyNodes.ContainsKey(e.ToId))
			.Select(e => assemblyNodes.ContainsKey(e.FromId) ? e.ToId : e.FromId)
			.Distinct()
			.ToList();

		if (crossRefs.Count > 0)
		{
			sb.AppendLine("## Cross-Assembly References");
			sb.AppendLine();
			sb.AppendLine($"This assembly has {crossRefs.Count} cross-boundary connections.");
		}

		return sb.ToString().TrimEnd();
	}
}
