using CodeGraph.Core.IO;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;

namespace CodeGraph.Integration.Tests;

/// <summary>
/// End-to-end tests for cross-solution graph merging.
/// Verifies that multi-solution indexing produces a unified graph with
/// cross-solution edges tagged as <see cref="EdgeConfidence.Inferred"/>.
/// </summary>
public class CrossSolutionTests : IDisposable
{
    private readonly string _workDir;

    public CrossSolutionTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"codegraph-xsln-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    private static GraphMetadata MakeMeta(string slnName) => new()
    {
        SchemaVersion = 1,
        CommitHash = "abc",
        Branch = "main",
        GeneratedAt = DateTimeOffset.UtcNow,
        IndexerVersion = "1.0.0",
        Solution = $"{slnName}.sln",
        SolutionName = slnName,
        ProjectsIndexed = new[] { slnName }
    };

    [Fact]
    public async Task FullPipeline_TwoSolutions_UnifiedGraphHasCrossSolutionEdges()
    {
        var writer = new SqliteGraphWriter();
        var unifiedDb = Path.Combine(_workDir, "graph.db");

        // Solution A: defines Backend.Services.OrderService
        var nodesA = new Dictionary<string, GraphNode>
        {
            ["Backend.Services.OrderService"] = new()
            {
                Id = "Backend.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Backend.Services.OrderService.PlaceOrder"] = new()
            {
                Id = "Backend.Services.OrderService.PlaceOrder",
                Name = "PlaceOrder",
                Kind = NodeKind.Method,
                ContainingTypeId = "Backend.Services.OrderService",
                AssemblyName = "Backend"
            }
        };
        var edgesA = new List<GraphEdge>
        {
            new()
            {
                FromId = "Backend.Services.OrderService",
                ToId = "Backend.Services.OrderService.PlaceOrder",
                Type = EdgeType.Contains
            }
        };

        // Solution B: defines Frontend.OrderController, references OrderService externally
        var nodesB = new Dictionary<string, GraphNode>
        {
            ["Frontend.Controllers.OrderController"] = new()
            {
                Id = "Frontend.Controllers.OrderController",
                Name = "OrderController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };
        var edgesB = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Controllers.OrderController",
                ToId = "Backend.Services.OrderService",
                Type = EdgeType.Calls,
                IsExternal = true,
                Confidence = EdgeConfidence.Unresolved
            }
        };

        // Simulate multi-solution merge
        await writer.AppendAsync(unifiedDb, nodesA.Values, edgesA, "Backend");
        await writer.AppendAsync(unifiedDb, nodesB.Values, edgesB, "Frontend");

        // Read unified graph
        var (_, allNodes, allEdges) = await SqliteGraphReader.ReadAsync(unifiedDb);

        // Run cross-solution linking
        var (crossEdges, resolvedIds) = CrossSolutionLinker.Link(allNodes, allEdges);

        Assert.Single(crossEdges);
        Assert.Equal("Frontend.Controllers.OrderController", crossEdges[0].FromId);
        Assert.Equal("Backend.Services.OrderService", crossEdges[0].ToId);
        Assert.Equal(EdgeConfidence.Inferred, crossEdges[0].Confidence);
        Assert.False(crossEdges[0].IsExternal);
        Assert.Equal("cross-solution", crossEdges[0].Resolution);

        Assert.Single(resolvedIds);
        Assert.Contains("Backend.Services.OrderService", resolvedIds);

        // Append cross-solution edges to unified DB
        await writer.AppendAsync(unifiedDb, Array.Empty<GraphNode>(), crossEdges, "__cross_solution__");

        // Re-read and verify final state
        var (_, finalNodes, finalEdges) = await SqliteGraphReader.ReadAsync(unifiedDb);

        Assert.Equal(3, finalNodes.Count); // 2 from A + 1 from B
        Assert.True(finalNodes.ContainsKey("Backend.Services.OrderService"));
        Assert.True(finalNodes.ContainsKey("Backend.Services.OrderService.PlaceOrder"));
        Assert.True(finalNodes.ContainsKey("Frontend.Controllers.OrderController"));

        var inferredEdges = finalEdges.Where(e => e.Confidence == EdgeConfidence.Inferred).ToList();
        Assert.Single(inferredEdges);
        Assert.Equal("true", inferredEdges[0].Metadata["cross_solution"]);
    }

    [Fact]
    public async Task FullPipeline_SharedNuGetPackage_NoCrossLink()
    {
        var writer = new SqliteGraphWriter();
        var unifiedDb = Path.Combine(_workDir, "graph-nuget.db");

        // Both solutions reference Newtonsoft.Json externally
        var nodesA = new Dictionary<string, GraphNode>
        {
            ["A.ClassA"] = new() { Id = "A.ClassA", Name = "ClassA", Kind = NodeKind.Type, AssemblyName = "A" }
        };
        var edgesA = new List<GraphEdge>
        {
            new()
            {
                FromId = "A.ClassA",
                ToId = "Newtonsoft.Json.JsonConvert",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet"
            }
        };

        var nodesB = new Dictionary<string, GraphNode>
        {
            ["B.ClassB"] = new() { Id = "B.ClassB", Name = "ClassB", Kind = NodeKind.Type, AssemblyName = "B" }
        };
        var edgesB = new List<GraphEdge>
        {
            new()
            {
                FromId = "B.ClassB",
                ToId = "Newtonsoft.Json.JsonConvert",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet"
            }
        };

        await writer.AppendAsync(unifiedDb, nodesA.Values, edgesA, "SolA");
        await writer.AppendAsync(unifiedDb, nodesB.Values, edgesB, "SolB");

        var (_, allNodes, allEdges) = await SqliteGraphReader.ReadAsync(unifiedDb);
        var (crossEdges, _) = CrossSolutionLinker.Link(allNodes, allEdges);

        // NuGet target not present in unified nodes → no cross-link
        Assert.Empty(crossEdges);
    }

    [Fact]
    public async Task FullPipeline_NoMatch_ExternalNodeStaysExternal()
    {
        var writer = new SqliteGraphWriter();
        var unifiedDb = Path.Combine(_workDir, "graph-nomatch.db");

        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.ClassA"] = new() { Id = "A.ClassA", Name = "ClassA", Kind = NodeKind.Type, AssemblyName = "A" }
        };
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "A.ClassA",
                ToId = "Unknown.External.Service",
                Type = EdgeType.Calls,
                IsExternal = true
            }
        };

        await writer.AppendAsync(unifiedDb, nodes.Values, edges, "Sol");

        var (_, allNodes, allEdges) = await SqliteGraphReader.ReadAsync(unifiedDb);
        var (crossEdges, resolvedIds) = CrossSolutionLinker.Link(allNodes, allEdges);

        Assert.Empty(crossEdges);
        Assert.Empty(resolvedIds);

        // Original edge unchanged
        Assert.Single(allEdges);
        Assert.True(allEdges[0].IsExternal);
    }

    [Fact]
    public async Task AppendAndFindNodes_PartialRead_Works()
    {
        var writer = new SqliteGraphWriter();
        var dbPath = Path.Combine(_workDir, "graph-find.db");

        var nodesA = new List<GraphNode>
        {
            new() { Id = "Sol.Alpha", Name = "Alpha", Kind = NodeKind.Type, AssemblyName = "Sol" },
            new() { Id = "Sol.Beta", Name = "Beta", Kind = NodeKind.Type, AssemblyName = "Sol" },
            new() { Id = "Sol.Gamma", Name = "Gamma", Kind = NodeKind.Method, AssemblyName = "Sol" }
        };

        await writer.AppendAsync(dbPath, nodesA, Array.Empty<GraphEdge>(), "MySol");

        // FindNodesByIds — partial read
        var found = await SqliteGraphReader.FindNodesByIds(dbPath, new[] { "Sol.Alpha", "Sol.Gamma" });

        Assert.Equal(2, found.Count);
        Assert.True(found.ContainsKey("Sol.Alpha"));
        Assert.True(found.ContainsKey("Sol.Gamma"));
        Assert.False(found.ContainsKey("Sol.Beta"));

        // FindNodesByName — fuzzy pattern
        var byName = await SqliteGraphReader.FindNodesByName(dbPath, "%a%");

        // "Alpha", "Beta", "Gamma" all contain 'a'
        Assert.Equal(3, byName.Count);
    }
}
