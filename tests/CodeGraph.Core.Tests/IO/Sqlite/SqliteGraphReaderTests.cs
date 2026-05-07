using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO.Sqlite;

public class SqliteGraphReaderTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;

    public SqliteGraphReaderTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), $"codegraph-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "graph.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dbDir))
            Directory.Delete(_dbDir, true);
    }

    private static GraphMetadata MakeMetadata() => new()
    {
        SchemaVersion = 1,
        CommitHash = "abc123",
        Branch = "main",
        GeneratedAt = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero),
        IndexerVersion = "1.0.0",
        Solution = "Test.sln",
        SolutionName = "Test",
        ProjectsIndexed = new[] { "ProjA" }
    };

    private async Task SeedDatabaseAsync()
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "MyApp.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                FilePath = "src/OrderService.cs",
                StartLine = 10, EndLine = 50,
                Signature = "public class OrderService",
                DocComment = "Handles orders",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                Metadata = new Dictionary<string, string> { ["category"] = "service" }
            },
            new()
            {
                Id = "MyApp.Services.UserService",
                Name = "UserService",
                Kind = NodeKind.Type,
                FilePath = "src/UserService.cs",
                StartLine = 5, EndLine = 30,
                Signature = "public class UserService",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp"
            },
            new()
            {
                Id = "MyApp.Data.Repository",
                Name = "Repository",
                Kind = NodeKind.Type,
                FilePath = "src/Repository.cs",
                Accessibility = Accessibility.Internal,
                AssemblyName = "MyApp"
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());
    }

    [Fact]
    public async Task ReadAsync_NonExistentDb_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => SqliteGraphReader.ReadAsync(fakePath));
    }

    [Fact]
    public async Task FindNodesByIds_ReturnsMatchingNodes()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            new[] { "MyApp.Services.OrderService", "MyApp.Data.Repository" });

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("MyApp.Services.OrderService"));
        Assert.True(result.ContainsKey("MyApp.Data.Repository"));
        Assert.Equal("OrderService", result["MyApp.Services.OrderService"].Name);
        Assert.Equal("Repository", result["MyApp.Data.Repository"].Name);
    }

    [Fact]
    public async Task FindNodesByIds_IncludesMetadata()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            new[] { "MyApp.Services.OrderService" });

        Assert.Single(result);
        Assert.Equal("service", result["MyApp.Services.OrderService"].Metadata["category"]);
    }

    [Fact]
    public async Task FindNodesByIds_NonExistentIds_ReturnsEmpty()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            new[] { "DoesNot.Exist" });

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindNodesByIds_EmptyInput_ReturnsEmpty()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindNodesByIds_NonExistentDb_Throws()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => SqliteGraphReader.FindNodesByIds(fakePath, new[] { "any" }));
    }

    [Fact]
    public async Task FindNodesByName_ExactMatch()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "OrderService");

        Assert.Single(result);
        Assert.Equal("MyApp.Services.OrderService", result[0].Id);
    }

    [Fact]
    public async Task FindNodesByName_PatternMatch()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "%Service%");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.Name == "OrderService");
        Assert.Contains(result, n => n.Name == "UserService");
    }

    [Fact]
    public async Task FindNodesByName_NoMatch_ReturnsEmpty()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "NonExistent");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindNodesByName_IncludesMetadata()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "OrderService");

        Assert.Single(result);
        Assert.Equal("service", result[0].Metadata["category"]);
    }

    [Fact]
    public async Task FindNodesByName_NonExistentDb_Throws()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => SqliteGraphReader.FindNodesByName(fakePath, "%any%"));
    }
}
