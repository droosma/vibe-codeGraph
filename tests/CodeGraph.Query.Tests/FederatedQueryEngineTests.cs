using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Query;
using CodeGraph.Query.Filters;

namespace CodeGraph.Query.Tests;

public class FederatedQueryEngineTests : IDisposable
{
    private readonly string _testDir;

    public FederatedQueryEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"codegraph-federated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private async Task WriteSubGraph(string solutionName, List<GraphNode> nodes, List<GraphEdge> edges)
    {
        var subDir = Path.Combine(_testDir, solutionName);
        var writer = new GraphWriter();
        var metadata = new GraphMetadata
        {
            CommitHash = "abc123",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = $"{solutionName}.sln",
            SolutionName = solutionName,
            ProjectsIndexed = nodes.Select(n => n.AssemblyName).Distinct().Where(a => !string.IsNullOrEmpty(a)).ToArray()
        };
        await writer.WriteAsync(subDir, nodes, edges, metadata);
    }

    [Fact]
    public async Task LoadAsync_FederatedGraph_MergesAllSubGraphs()
    {
        // Create two sub-graphs
        var backendNodes = new List<GraphNode>
        {
            new() { Id = "Backend.OrderService", Name = "OrderService", Kind = NodeKind.Type, AssemblyName = "Backend" },
            new() { Id = "Backend.OrderService.PlaceOrder", Name = "PlaceOrder", Kind = NodeKind.Method, AssemblyName = "Backend",
                ContainingTypeId = "Backend.OrderService" }
        };
        var backendEdges = new List<GraphEdge>
        {
            new() { FromId = "Backend.OrderService", ToId = "Backend.OrderService.PlaceOrder", Type = EdgeType.Contains }
        };

        var frontendNodes = new List<GraphNode>
        {
            new() { Id = "Frontend.OrderView", Name = "OrderView", Kind = NodeKind.Type, AssemblyName = "Frontend" },
            new() { Id = "Frontend.OrderView.Render", Name = "Render", Kind = NodeKind.Method, AssemblyName = "Frontend",
                ContainingTypeId = "Frontend.OrderView" }
        };
        var frontendEdges = new List<GraphEdge>
        {
            new() { FromId = "Frontend.OrderView", ToId = "Frontend.OrderView.Render", Type = EdgeType.Contains }
        };

        await WriteSubGraph("backend", backendNodes, backendEdges);
        await WriteSubGraph("frontend", frontendNodes, frontendEdges);

        var engine = await QueryEngine.LoadAsync(_testDir);

        // Should find nodes from both sub-graphs
        var backendResult = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 0 });
        Assert.Single(backendResult.MatchedNodes);
        Assert.Equal("Backend.OrderService", backendResult.MatchedNodes[0].Id);

        var frontendResult = engine.Query(new QueryOptions { Pattern = "OrderView", Depth = 0 });
        Assert.Single(frontendResult.MatchedNodes);
        Assert.Equal("Frontend.OrderView", frontendResult.MatchedNodes[0].Id);
    }

    [Fact]
    public async Task LoadAsync_FederatedGraph_DeduplicatesNodes()
    {
        // Create two sub-graphs with a shared node (same ID)
        var sharedNode = new GraphNode
        {
            Id = "Shared.Core.Logger", Name = "Logger", Kind = NodeKind.Type, AssemblyName = "Shared.Core"
        };

        var backendNodes = new List<GraphNode>
        {
            new() { Id = "Backend.OrderService", Name = "OrderService", Kind = NodeKind.Type, AssemblyName = "Backend" },
            sharedNode
        };
        var backendEdges = new List<GraphEdge>
        {
            new() { FromId = "Backend.OrderService", ToId = "Shared.Core.Logger", Type = EdgeType.Calls }
        };

        var frontendNodes = new List<GraphNode>
        {
            new() { Id = "Frontend.OrderView", Name = "OrderView", Kind = NodeKind.Type, AssemblyName = "Frontend" },
            sharedNode
        };
        var frontendEdges = new List<GraphEdge>
        {
            new() { FromId = "Frontend.OrderView", ToId = "Shared.Core.Logger", Type = EdgeType.Calls }
        };

        await WriteSubGraph("backend", backendNodes, backendEdges);
        await WriteSubGraph("frontend", frontendNodes, frontendEdges);

        var engine = await QueryEngine.LoadAsync(_testDir);

        // Logger should only appear once
        var loggerResult = engine.Query(new QueryOptions { Pattern = "Logger", Depth = 0 });
        Assert.Single(loggerResult.MatchedNodes);
    }

    [Fact]
    public async Task LoadAsync_FederatedGraph_DeduplicatesEdges()
    {
        var sharedNode = new GraphNode
        {
            Id = "Shared.Core.Logger", Name = "Logger", Kind = NodeKind.Type, AssemblyName = "Shared.Core"
        };

        var backendNodes = new List<GraphNode>
        {
            new() { Id = "Backend.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "Backend" },
            sharedNode
        };
        // Same edge from both sub-graphs
        var sameEdge = new GraphEdge { FromId = "Backend.Service", ToId = "Shared.Core.Logger", Type = EdgeType.Calls };

        await WriteSubGraph("backend", backendNodes, new List<GraphEdge> { sameEdge });
        await WriteSubGraph("frontend",
            new List<GraphNode>
            {
                new() { Id = "Backend.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "Backend" },
                sharedNode
            },
            new List<GraphEdge> { sameEdge });

        var engine = await QueryEngine.LoadAsync(_testDir);

        var result = engine.Query(new QueryOptions { Pattern = "Service", Depth = 1 });
        // Ensure the duplicated edge only appears once
        var callEdges = result.Edges.Where(e => e.Type == EdgeType.Calls).ToList();
        Assert.Single(callEdges);
    }

    [Fact]
    public async Task LoadAsync_WithSolutionFilter_LoadsOnlyFilteredSubGraph()
    {
        var backendNodes = new List<GraphNode>
        {
            new() { Id = "Backend.OrderService", Name = "OrderService", Kind = NodeKind.Type, AssemblyName = "Backend" }
        };

        var frontendNodes = new List<GraphNode>
        {
            new() { Id = "Frontend.OrderView", Name = "OrderView", Kind = NodeKind.Type, AssemblyName = "Frontend" }
        };

        await WriteSubGraph("backend", backendNodes, new List<GraphEdge>());
        await WriteSubGraph("frontend", frontendNodes, new List<GraphEdge>());

        // Load only backend
        var engine = await QueryEngine.LoadAsync(_testDir, "backend");

        var backendResult = engine.Query(new QueryOptions { Pattern = "OrderService", Depth = 0 });
        Assert.Single(backendResult.MatchedNodes);

        var frontendResult = engine.Query(new QueryOptions { Pattern = "OrderView", Depth = 0 });
        Assert.Empty(frontendResult.MatchedNodes);
    }

    [Fact]
    public async Task LoadAsync_SingleGraph_BackwardCompatible()
    {
        // Write a single-solution graph (no sub-directories)
        var nodes = new List<GraphNode>
        {
            new() { Id = "MyApp.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "MyApp" }
        };
        var edges = new List<GraphEdge>();
        var metadata = new GraphMetadata
        {
            CommitHash = "abc",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "1.0.0",
            Solution = "MyApp.sln",
            SolutionName = "MyApp"
        };

        var writer = new GraphWriter();
        await writer.WriteAsync(_testDir, nodes, edges, metadata);

        var engine = await QueryEngine.LoadAsync(_testDir);

        var result = engine.Query(new QueryOptions { Pattern = "Service", Depth = 0 });
        Assert.Single(result.MatchedNodes);
    }

    [Fact]
    public async Task LoadAsync_FederatedGraph_MergesProjectsIndexed()
    {
        var backendNodes = new List<GraphNode>
        {
            new() { Id = "Backend.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "Backend" }
        };

        var frontendNodes = new List<GraphNode>
        {
            new() { Id = "Frontend.View", Name = "View", Kind = NodeKind.Type, AssemblyName = "Frontend" }
        };

        await WriteSubGraph("backend", backendNodes, new List<GraphEdge>());
        await WriteSubGraph("frontend", frontendNodes, new List<GraphEdge>());

        var engine = await QueryEngine.LoadAsync(_testDir);

        // Query all - should find nodes from both solutions
        var allResult = engine.Query(new QueryOptions { Pattern = "*", Depth = 0 });
        Assert.True(allResult.MatchedNodes.Count >= 2);
    }

    [Fact]
    public async Task LoadAsync_EmptyDirectory_ThrowsFileNotFound()
    {
        var emptyDir = Path.Combine(_testDir, "empty");
        Directory.CreateDirectory(emptyDir);

        await Assert.ThrowsAsync<FileNotFoundException>(() => QueryEngine.LoadAsync(emptyDir));
    }
}
