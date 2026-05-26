using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public async Task FindNodesByIds_MixOfFoundAndNotFound_ReturnsOnlyFound()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            new[] { "MyApp.Services.OrderService", "MyApp.Data.Repository", "Does.Not.Exist" });

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("MyApp.Services.OrderService"));
        Assert.True(result.ContainsKey("MyApp.Data.Repository"));
        Assert.False(result.ContainsKey("Does.Not.Exist"));
    }

    [Fact]
    public async Task FindNodesByName_CaseSensitive_SqliteDefaultBehavior()
    {
        await SeedDatabaseAsync();

        // SQLite LIKE is case-insensitive for ASCII by default, so lowercase matches
        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "orderservice");

        Assert.Single(result);
        Assert.Equal("OrderService", result[0].Name);
    }

    [Fact]
    public async Task ReadAsync_EmptyDatabase_ReturnsEmptyNodesAndEdges()
    {
        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath,
            Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), MakeMetadata());

        var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Empty(nodes);
        Assert.Empty(edges);
        Assert.Equal(1, metadata.SchemaVersion);
    }

    [Fact]
    public async Task ReadAsync_StatsMetadata_ParsedCorrectly()
    {
        var meta = MakeMetadata() with
        {
            Stats = new Dictionary<string, int>
            {
                ["nodes"] = 42,
                ["edges"] = 7
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath,
            Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), meta);

        var (readMeta, _, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(2, readMeta.Stats.Count);
        Assert.Equal(42, readMeta.Stats["nodes"]);
        Assert.Equal(7, readMeta.Stats["edges"]);
    }

    [Fact]
    public async Task ReadAsync_NoStatsMetadata_ReturnsEmptyStats()
    {
        var meta = MakeMetadata() with { Stats = new Dictionary<string, int>() };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath,
            Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), meta);

        var (readMeta, _, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Empty(readMeta.Stats);
    }

    [Fact]
    public async Task ReadAsync_ProjectsIndexedLiteralNullJson_ReturnsEmptyArray()
    {
        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath,
            Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), MakeMetadata());

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value = 'null' WHERE key = 'projects_indexed'";
            await command.ExecuteNonQueryAsync();
        }

        var (readMeta, _, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Empty(readMeta.ProjectsIndexed);
    }

    [Fact]
    public async Task ReadAsync_EdgesWithNullableFieldsMixed()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A", Name = "A", Kind = NodeKind.Type, FilePath = "a.cs", Signature = "class A", Accessibility = Accessibility.Public, AssemblyName = "Asm" },
            new() { Id = "B", Name = "B", Kind = NodeKind.Type, FilePath = "b.cs", Signature = "class B", Accessibility = Accessibility.Public, AssemblyName = "Asm" },
            new() { Id = "C", Name = "C", Kind = NodeKind.Type, FilePath = "c.cs", Signature = "class C", Accessibility = Accessibility.Public, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "A", ToId = "B", Type = EdgeType.Calls,
                PackageSource = "NuGet", SourceLink = "https://example.com", Resolution = "Exact",
                Confidence = EdgeConfidence.Verified
            },
            new()
            {
                FromId = "B", ToId = "C", Type = EdgeType.DependsOn,
                PackageSource = null, SourceLink = null, Resolution = null,
                Confidence = EdgeConfidence.Inferred
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(2, readEdges.Count);

        var edgeWithValues = readEdges.First(e => e.FromId == "A" && e.ToId == "B");
        Assert.Equal("NuGet", edgeWithValues.PackageSource);
        Assert.Equal("https://example.com", edgeWithValues.SourceLink);
        Assert.Equal("Exact", edgeWithValues.Resolution);

        var edgeWithNulls = readEdges.First(e => e.FromId == "B" && e.ToId == "C");
        Assert.Null(edgeWithNulls.PackageSource);
        Assert.Null(edgeWithNulls.SourceLink);
        Assert.Null(edgeWithNulls.Resolution);
    }

    [Fact]
    public async Task ReadAsync_IsExternalTrue_ReadBackCorrectly()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A", Name = "A", Kind = NodeKind.Type, FilePath = "a.cs", Signature = "class A", Accessibility = Accessibility.Public, AssemblyName = "Asm" },
            new() { Id = "B", Name = "B", Kind = NodeKind.Type, FilePath = "b.cs", Signature = "class B", Accessibility = Accessibility.Public, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.DependsOn, IsExternal = true, Confidence = EdgeConfidence.Verified }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readEdges);
        Assert.True(readEdges[0].IsExternal);
    }

    [Fact]
    public async Task ReadAsync_IsExternalFalse_ReadBackCorrectly()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A", Name = "A", Kind = NodeKind.Type, FilePath = "a.cs", Signature = "class A", Accessibility = Accessibility.Public, AssemblyName = "Asm" },
            new() { Id = "B", Name = "B", Kind = NodeKind.Type, FilePath = "b.cs", Signature = "class B", Accessibility = Accessibility.Public, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A", ToId = "B", Type = EdgeType.Calls, IsExternal = false, Confidence = EdgeConfidence.Verified }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readEdges);
        Assert.False(readEdges[0].IsExternal);
    }

    [Theory]
    [InlineData(EdgeConfidence.Verified)]
    [InlineData(EdgeConfidence.Inferred)]
    [InlineData(EdgeConfidence.Unresolved)]
    public async Task ReadAsync_AllEdgeConfidences_Roundtrip(EdgeConfidence confidence)
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "X", Name = "X", Kind = NodeKind.Method, FilePath = "x.cs", Signature = "void X()", Accessibility = Accessibility.Public, AssemblyName = "Asm" },
            new() { Id = "Y", Name = "Y", Kind = NodeKind.Method, FilePath = "y.cs", Signature = "void Y()", Accessibility = Accessibility.Public, AssemblyName = "Asm" }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "X", ToId = "Y", Type = EdgeType.Calls, Confidence = confidence }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readEdges);
        Assert.Equal(confidence, readEdges[0].Confidence);
    }

    [Fact]
    public async Task ReadAsync_NodeNullableFieldsCombinations()
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "AllNull", Name = "AllNull", Kind = NodeKind.Method,
                FilePath = "all.cs", StartLine = 1, EndLine = 5,
                Signature = "void AllNull()",
                DocComment = null, ContainingTypeId = null, ContainingNamespaceId = null,
                Accessibility = Accessibility.Private, AssemblyName = "Asm"
            },
            new()
            {
                Id = "AllSet", Name = "AllSet", Kind = NodeKind.Property,
                FilePath = "set.cs", StartLine = 10, EndLine = 12,
                Signature = "string AllSet { get; }",
                DocComment = "A doc comment", ContainingTypeId = "ParentType", ContainingNamespaceId = "MyNamespace",
                Accessibility = Accessibility.Protected, AssemblyName = "Lib"
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var (_, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(2, readNodes.Count);

        var allNull = readNodes["AllNull"];
        Assert.Null(allNull.DocComment);
        Assert.Null(allNull.ContainingTypeId);
        Assert.Null(allNull.ContainingNamespaceId);
        Assert.Equal(NodeKind.Method, allNull.Kind);
        Assert.Equal(Accessibility.Private, allNull.Accessibility);
        Assert.Equal(1, allNull.StartLine);
        Assert.Equal(5, allNull.EndLine);

        var allSet = readNodes["AllSet"];
        Assert.Equal("A doc comment", allSet.DocComment);
        Assert.Equal("ParentType", allSet.ContainingTypeId);
        Assert.Equal("MyNamespace", allSet.ContainingNamespaceId);
        Assert.Equal(NodeKind.Property, allSet.Kind);
        Assert.Equal(Accessibility.Protected, allSet.Accessibility);
        Assert.Equal("set.cs", allSet.FilePath);
        Assert.Equal("string AllSet { get; }", allSet.Signature);
        Assert.Equal("Lib", allSet.AssemblyName);
    }

    [Fact]
    public async Task FindNodesByName_PatternMatchIncludesNodeProperties()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByName(_dbPath, "%Service%");

        Assert.Equal(2, result.Count);

        var order = result.First(n => n.Name == "OrderService");
        Assert.Equal("MyApp.Services.OrderService", order.Id);
        Assert.Equal("src/OrderService.cs", order.FilePath);
        Assert.Equal(10, order.StartLine);
        Assert.Equal(50, order.EndLine);
        Assert.Equal("public class OrderService", order.Signature);
        Assert.Equal(Accessibility.Public, order.Accessibility);
        Assert.Equal("MyApp", order.AssemblyName);
        Assert.Equal("Handles orders", order.DocComment);
        Assert.Equal("MyApp.Services", order.ContainingNamespaceId);

        var user = result.First(n => n.Name == "UserService");
        Assert.Equal("src/UserService.cs", user.FilePath);
        Assert.Equal(5, user.StartLine);
        Assert.Equal(30, user.EndLine);
        Assert.Equal("public class UserService", user.Signature);
        Assert.Null(user.DocComment);
    }

    [Fact]
    public async Task FindNodesByIds_ReturnsCorrectNodeProperties()
    {
        await SeedDatabaseAsync();

        var result = await SqliteGraphReader.FindNodesByIds(_dbPath,
            new[] { "MyApp.Services.OrderService", "MyApp.Data.Repository" });

        var order = result["MyApp.Services.OrderService"];
        Assert.Equal("OrderService", order.Name);
        Assert.Equal(NodeKind.Type, order.Kind);
        Assert.Equal("src/OrderService.cs", order.FilePath);
        Assert.Equal(10, order.StartLine);
        Assert.Equal(50, order.EndLine);
        Assert.Equal("public class OrderService", order.Signature);
        Assert.Equal("Handles orders", order.DocComment);
        Assert.Equal("MyApp.Services", order.ContainingNamespaceId);
        Assert.Null(order.ContainingTypeId);
        Assert.Equal(Accessibility.Public, order.Accessibility);
        Assert.Equal("MyApp", order.AssemblyName);

        var repo = result["MyApp.Data.Repository"];
        Assert.Equal("Repository", repo.Name);
        Assert.Equal(Accessibility.Internal, repo.Accessibility);
        Assert.Null(repo.DocComment);
        Assert.Null(repo.ContainingTypeId);
    }

    [Fact]
    public async Task ReadAsync_LargeMetadataValues_Preserved()
    {
        var largeValue = new string('X', 1500);
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "Big", Name = "Big", Kind = NodeKind.Type,
                FilePath = "big.cs", Signature = "class Big",
                Accessibility = Accessibility.Public, AssemblyName = "Asm",
                Metadata = new Dictionary<string, string> { ["large_key"] = largeValue }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var (_, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readNodes);
        Assert.Equal(largeValue, readNodes["Big"].Metadata["large_key"]);
        Assert.Equal(1500, readNodes["Big"].Metadata["large_key"].Length);
    }
}
