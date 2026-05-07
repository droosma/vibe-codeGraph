using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Wiki;

public static class DiWiringPageGenerator
{
	public static string Generate(
		Dictionary<string, GraphNode> nodes,
		List<GraphEdge> edges)
	{
		var sb = new StringBuilder();
		sb.AppendLine("# DI Wiring");
		sb.AppendLine();
		sb.AppendLine("[← Back to Index](INDEX.md)");
		sb.AppendLine();

		var resolvesTo = edges
			.Where(e => e.Type == EdgeType.ResolvesTo)
			.ToList();

		if (resolvesTo.Count == 0)
		{
			sb.AppendLine("No DI wiring (ResolvesTo edges) found in the graph.");
			return sb.ToString().TrimEnd();
		}

		sb.AppendLine("| Interface | Implementation | Resolution |");
		sb.AppendLine("|-----------|----------------|------------|");
		foreach (var edge in resolvesTo.OrderBy(e => nodes.GetValueOrDefault(e.FromId)?.Name ?? e.FromId))
		{
			var fromNode = nodes.GetValueOrDefault(edge.FromId);
			var toNode = nodes.GetValueOrDefault(edge.ToId);
			var fromName = fromNode?.Name ?? edge.FromId;
			var toName = toNode?.Name ?? edge.ToId;
			var resolution = !string.IsNullOrEmpty(edge.Resolution) ? edge.Resolution : edge.Confidence.ToString();
			sb.AppendLine($"| {fromName} | {toName} | {resolution} |");
		}

		return sb.ToString().TrimEnd();
	}
}
