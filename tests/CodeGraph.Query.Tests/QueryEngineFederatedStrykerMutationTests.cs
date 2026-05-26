using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.Tests;

public sealed class QueryEngineFederatedStrykerMutationTests : IDisposable
{
    private readonly string _testDir = Path.Combine(AppContext.BaseDirectory, "queryengine-federated-stryker", Guid.NewGuid().ToString("N"));

    public QueryEngineFederatedStrykerMutationTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_FederatedGraph_PreservesFirstMetadataAndMergedSolutionInfo()
    {
        await WriteSubGraph(
            "backend",
            new GraphMetadata
            {
                CommitHash = "backend-commit",
                Branch = "backend-branch",
                GeneratedAt = DateTimeOffset.UnixEpoch,
                IndexerVersion = "1.0.0",
                Solution = "backend.sln",
                SolutionName = "backend",
                ProjectsIndexed = new[] { "Backend" }
            },
            [new GraphNode { Id = "Backend.Service", Name = "Service", Kind = NodeKind.Type, AssemblyName = "Backend" }],
            []);

        await WriteSubGraph(
            "frontend",
            new GraphMetadata
            {
                CommitHash = "frontend-commit",
                Branch = "frontend-branch",
                GeneratedAt = DateTimeOffset.UnixEpoch.AddDays(1),
                IndexerVersion = "2.0.0",
                Solution = "frontend.sln",
                SolutionName = "frontend",
                ProjectsIndexed = new[] { "Frontend" }
            },
            [new GraphNode { Id = "Frontend.View", Name = "View", Kind = NodeKind.Type, AssemblyName = "Frontend" }],
            []);

        var engine = await QueryEngine.LoadAsync(_testDir);

        Assert.Equal("backend-commit", engine.Metadata.CommitHash);
        Assert.Equal("backend-branch", engine.Metadata.Branch);
        Assert.Equal("backend, frontend", engine.Metadata.SolutionName);
        Assert.Equal("backend.sln, frontend.sln", engine.Metadata.Solution);
        Assert.Equal(new[] { "Backend", "Frontend" }, engine.Metadata.ProjectsIndexed.OrderBy(project => project, StringComparer.Ordinal));
    }

    private async Task WriteSubGraph(string solutionName, GraphMetadata metadata, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var writer = new GraphWriter();
        await writer.WriteAsync(Path.Combine(_testDir, solutionName), nodes.ToList(), edges.ToList(), metadata);
    }
}
