using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Wiki;

public static class IndexPageGenerator
{
	public static string Generate(
		Dictionary<string, GraphNode> nodes,
		List<GraphEdge> edges,
		GraphMetadata metadata)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"# {metadata.SolutionName} — Code Wiki");
		sb.AppendLine();
		sb.AppendLine($"Generated from CodeGraph | {metadata.GeneratedAt:yyyy-MM-dd} | {metadata.CommitHash}");
		sb.AppendLine();
		sb.AppendLine("## Navigation");
		sb.AppendLine();
		sb.AppendLine("- [Interfaces](INTERFACES.md)");
		sb.AppendLine("- [DI Wiring](DI-WIRING.md)");
		sb.AppendLine();
		sb.AppendLine("## Assemblies");
		sb.AppendLine();

		var assemblies = nodes.Values
			.GroupBy(n => n.AssemblyName)
			.Where(g => !string.IsNullOrEmpty(g.Key))
			.OrderBy(g => g.Key);

		sb.AppendLine("| Assembly | Types | Methods | Total |");
		sb.AppendLine("|----------|------:|--------:|------:|");
		foreach (var g in assemblies)
		{
			var types = g.Count(n => n.Kind == NodeKind.Type);
			var methods = g.Count(n => n.Kind == NodeKind.Method);
			var fileName = WikiGenerator.SanitizeFileName(g.Key);
			sb.AppendLine($"| [{g.Key}](assemblies/{fileName}.md) | {types} | {methods} | {g.Count()} |");
		}

		return sb.ToString().TrimEnd();
	}
}
