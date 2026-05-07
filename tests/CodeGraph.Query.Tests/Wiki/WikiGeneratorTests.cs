using CodeGraph.Core.Models;
using CodeGraph.Query.Wiki;

namespace CodeGraph.Query.Tests.Wiki;

public class WikiGeneratorTests : IDisposable
{
	private readonly string _outputDir;

	public WikiGeneratorTests()
	{
		_outputDir = Path.Combine(Path.GetTempPath(), "codegraph-wiki-test-" + Guid.NewGuid().ToString("N")[..8]);
	}

	public void Dispose()
	{
		if (Directory.Exists(_outputDir))
			Directory.Delete(_outputDir, true);
	}

	private static Dictionary<string, GraphNode> CreateNodes(
		params (string id, string name, NodeKind kind, string assembly)[] defs)
	{
		var nodes = new Dictionary<string, GraphNode>();
		foreach (var (id, name, kind, assembly) in defs)
			nodes[id] = new GraphNode { Id = id, Name = name, Kind = kind, AssemblyName = assembly };
		return nodes;
	}

	private static GraphMetadata CreateMetadata() => new()
	{
		SolutionName = "TestSolution",
		CommitHash = "abc123",
		GeneratedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
	};

	[Fact]
	public void Generate_CreatesIndexFile()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		Assert.True(File.Exists(Path.Combine(_outputDir, "INDEX.md")));
	}

	[Fact]
	public void Generate_CreatesAssemblyDirectory()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		Assert.True(Directory.Exists(Path.Combine(_outputDir, "assemblies")));
		Assert.True(File.Exists(Path.Combine(_outputDir, "assemblies", "Asm1.md")));
	}

	[Fact]
	public void Generate_CreatesInterfacesFile()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		Assert.True(File.Exists(Path.Combine(_outputDir, "INTERFACES.md")));
	}

	[Fact]
	public void Generate_CreatesDiWiringFile()
	{
		var nodes = CreateNodes(("A", "TypeA", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		Assert.True(File.Exists(Path.Combine(_outputDir, "DI-WIRING.md")));
	}

	[Fact]
	public void Generate_IndexContainsRelativeLinks()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "MyAssembly"),
			("B", "MethodB", NodeKind.Method, "MyAssembly"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		var index = File.ReadAllText(Path.Combine(_outputDir, "INDEX.md"));
		Assert.Contains("assemblies/MyAssembly.md", index);
		Assert.Contains("INTERFACES.md", index);
		Assert.Contains("DI-WIRING.md", index);
	}

	[Fact]
	public void Generate_AssemblyPageContainsTypeListing()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm1"),
			("B", "TypeB", NodeKind.Type, "Asm1"));
		var edges = new List<GraphEdge>
		{
			new() { FromId = "A", ToId = "B", Type = EdgeType.Calls }
		};

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		var assemblyPage = File.ReadAllText(Path.Combine(_outputDir, "assemblies", "Asm1.md"));
		Assert.Contains("TypeA", assemblyPage);
		Assert.Contains("TypeB", assemblyPage);
		Assert.Contains("## Types", assemblyPage);
	}

	[Fact]
	public void Generate_CreatesMultipleAssemblyPages()
	{
		var nodes = CreateNodes(
			("A", "TypeA", NodeKind.Type, "Asm1"),
			("B", "TypeB", NodeKind.Type, "Asm2"));
		var edges = new List<GraphEdge>();

		WikiGenerator.Generate(_outputDir, nodes, edges, CreateMetadata());

		Assert.True(File.Exists(Path.Combine(_outputDir, "assemblies", "Asm1.md")));
		Assert.True(File.Exists(Path.Combine(_outputDir, "assemblies", "Asm2.md")));
	}

	[Fact]
	public void SanitizeFileName_RemovesInvalidChars()
	{
		var result = WikiGenerator.SanitizeFileName("My<Assembly>Name");

		Assert.DoesNotContain("<", result);
		Assert.DoesNotContain(">", result);
		Assert.Contains("My", result);
		Assert.Contains("Assembly", result);
		Assert.Contains("Name", result);
	}
}
