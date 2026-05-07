using CodeGraph.Core.Models;

namespace CodeGraph.Query.Wiki;

public static class WikiGenerator
{
	public static void Generate(
		string outputDir,
		Dictionary<string, GraphNode> nodes,
		List<GraphEdge> edges,
		GraphMetadata metadata)
	{
		Directory.CreateDirectory(outputDir);

		// Generate index page
		var indexContent = IndexPageGenerator.Generate(nodes, edges, metadata);
		File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), indexContent);

		// Generate per-assembly pages
		var assemblies = nodes.Values
			.GroupBy(n => n.AssemblyName)
			.Where(g => !string.IsNullOrEmpty(g.Key));

		var assemblyDir = Path.Combine(outputDir, "assemblies");
		Directory.CreateDirectory(assemblyDir);

		foreach (var assembly in assemblies)
		{
			var assemblyNodes = assembly.ToDictionary(n => n.Id, n => n);
			var assemblyEdges = edges.Where(e =>
				assemblyNodes.ContainsKey(e.FromId) || assemblyNodes.ContainsKey(e.ToId)).ToList();

			var content = AssemblyPageGenerator.Generate(assembly.Key, assemblyNodes, assemblyEdges);
			var fileName = SanitizeFileName(assembly.Key) + ".md";
			File.WriteAllText(Path.Combine(assemblyDir, fileName), content);
		}

		// Generate interfaces page
		var interfacesContent = InterfacesPageGenerator.Generate(nodes, edges);
		File.WriteAllText(Path.Combine(outputDir, "INTERFACES.md"), interfacesContent);

		// Generate DI wiring page
		var diContent = DiWiringPageGenerator.Generate(nodes, edges);
		File.WriteAllText(Path.Combine(outputDir, "DI-WIRING.md"), diContent);
	}

	internal static string SanitizeFileName(string name)
	{
		var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
		// Chars that are valid on Linux but problematic in filenames, URLs, and markdown links
		foreach (var c in new[] { '<', '>', ':', '"', '|', '?', '*' })
			invalid.Add(c);
		return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
	}
}
