using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO.Sqlite;

public class SqliteGraphWriterTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;

    public SqliteGraphWriterTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), $"codegraph-sqlite-{Guid.NewGuid():N}");
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
        ProjectsIndexed = new[] { "ProjA", "ProjB" },
        Stats = new Dictionary<string, int>
        {
            ["node_count"] = 3,
            ["edge_count"] = 1
        }
    };

    [Fact]
    public async Task WriteAndRead_Roundtrip_PreservesAllData()
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "MyApp.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                FilePath = "src/Services/OrderService.cs",
                StartLine = 10,
                EndLine = 50,
                Signature = "public class OrderService",
                DocComment = "Handles orders",
                ContainingTypeId = null,
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp",
                Metadata = new Dictionary<string, string> { ["category"] = "service" }
            },
            new()
            {
                Id = "MyApp.Services.OrderService.PlaceOrder",
                Name = "PlaceOrder",
                Kind = NodeKind.Method,
                FilePath = "src/Services/OrderService.cs",
                StartLine = 15,
                EndLine = 25,
                Signature = "public void PlaceOrder(int orderId)",
                DocComment = null,
                ContainingTypeId = "MyApp.Services.OrderService",
                ContainingNamespaceId = "MyApp.Services",
                Accessibility = Accessibility.Public,
                AssemblyName = "MyApp"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "MyApp.Services.OrderService",
                ToId = "MyApp.Services.OrderService.PlaceOrder",
                Type = EdgeType.Contains,
                IsExternal = false,
                Metadata = new Dictionary<string, string> { ["weight"] = "1" }
            },
            new()
            {
                FromId = "MyApp.Services.OrderService.PlaceOrder",
                ToId = "External.Lib.Save",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet",
                SourceLink = "https://example.com",
                Resolution = "resolved"
            }
        };

        var metadata = MakeMetadata();

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, metadata);

        Assert.True(File.Exists(_dbPath));

        var (readMetadata, readNodes, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        // Verify metadata
        Assert.Equal(metadata.SchemaVersion, readMetadata.SchemaVersion);
        Assert.Equal(metadata.CommitHash, readMetadata.CommitHash);
        Assert.Equal(metadata.Branch, readMetadata.Branch);
        Assert.Equal(metadata.GeneratedAt, readMetadata.GeneratedAt);
        Assert.Equal(metadata.IndexerVersion, readMetadata.IndexerVersion);
        Assert.Equal(metadata.Solution, readMetadata.Solution);
        Assert.Equal(metadata.SolutionName, readMetadata.SolutionName);
        Assert.Equal(metadata.ProjectsIndexed, readMetadata.ProjectsIndexed);
        Assert.Equal(metadata.Stats["node_count"], readMetadata.Stats["node_count"]);
        Assert.Equal(metadata.Stats["edge_count"], readMetadata.Stats["edge_count"]);

        // Verify nodes
        Assert.Equal(2, readNodes.Count);
        var node1 = readNodes["MyApp.Services.OrderService"];
        Assert.Equal("OrderService", node1.Name);
        Assert.Equal(NodeKind.Type, node1.Kind);
        Assert.Equal("src/Services/OrderService.cs", node1.FilePath);
        Assert.Equal(10, node1.StartLine);
        Assert.Equal(50, node1.EndLine);
        Assert.Equal("public class OrderService", node1.Signature);
        Assert.Equal("Handles orders", node1.DocComment);
        Assert.Null(node1.ContainingTypeId);
        Assert.Equal("MyApp.Services", node1.ContainingNamespaceId);
        Assert.Equal(Accessibility.Public, node1.Accessibility);
        Assert.Equal("MyApp", node1.AssemblyName);
        Assert.Equal("service", node1.Metadata["category"]);

        var node2 = readNodes["MyApp.Services.OrderService.PlaceOrder"];
        Assert.Equal("PlaceOrder", node2.Name);
        Assert.Equal(NodeKind.Method, node2.Kind);
        Assert.Null(node2.DocComment);
        Assert.Equal("MyApp.Services.OrderService", node2.ContainingTypeId);

        // Verify edges
        Assert.Equal(2, readEdges.Count);
        var containsEdge = readEdges.First(e => e.Type == EdgeType.Contains);
        Assert.Equal("MyApp.Services.OrderService", containsEdge.FromId);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", containsEdge.ToId);
        Assert.False(containsEdge.IsExternal);
        Assert.Equal("1", containsEdge.Metadata["weight"]);

        var callsEdge = readEdges.First(e => e.Type == EdgeType.Calls);
        Assert.Equal("MyApp.Services.OrderService.PlaceOrder", callsEdge.FromId);
        Assert.Equal("External.Lib.Save", callsEdge.ToId);
        Assert.True(callsEdge.IsExternal);
        Assert.Equal("NuGet", callsEdge.PackageSource);
        Assert.Equal("https://example.com", callsEdge.SourceLink);
        Assert.Equal("resolved", callsEdge.Resolution);
    }

    [Fact]
    public async Task WriteAndRead_EmptyGraph_Succeeds()
    {
        var metadata = MakeMetadata();
        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), metadata);

        var (readMetadata, readNodes, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(metadata.CommitHash, readMetadata.CommitHash);
        Assert.Empty(readNodes);
        Assert.Empty(readEdges);
    }

    [Fact]
    public async Task WriteAsync_OverwriteExistingDatabase()
    {
        var metadata1 = MakeMetadata();
        var writer = new SqliteGraphWriter();
        var nodes1 = new List<GraphNode>
        {
            new() { Id = "First.Node", Name = "Node", Kind = NodeKind.Type }
        };
        await writer.WriteAsync(_dbPath, nodes1, Array.Empty<GraphEdge>(), metadata1);

        var metadata2 = new GraphMetadata
        {
            CommitHash = "def456",
            Branch = "develop",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "2.0.0",
            Solution = "Other.sln",
            SolutionName = "Other",
            ProjectsIndexed = new[] { "ProjC" }
        };
        var nodes2 = new List<GraphNode>
        {
            new() { Id = "Second.Node", Name = "Node2", Kind = NodeKind.Method }
        };
        await writer.WriteAsync(_dbPath, nodes2, Array.Empty<GraphEdge>(), metadata2);

        var (readMetadata, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal("def456", readMetadata.CommitHash);
        Assert.Single(readNodes);
        Assert.True(readNodes.ContainsKey("Second.Node"));
        Assert.False(readNodes.ContainsKey("First.Node"));
    }

    [Fact]
    public async Task WriteAndRead_AllNodeKinds_Roundtrip()
    {
        var kinds = Enum.GetValues(typeof(NodeKind)).Cast<NodeKind>().ToList();
        var nodes = kinds.Select((kind, i) => new GraphNode
        {
            Id = $"Test.{kind}_{i}",
            Name = $"{kind}_{i}",
            Kind = kind
        }).ToList();

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var (_, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(kinds.Count, readNodes.Count);
        foreach (var kind in kinds)
        {
            Assert.Contains(readNodes.Values, n => n.Kind == kind);
        }
    }

    [Fact]
    public async Task WriteAndRead_AllEdgeTypes_Roundtrip()
    {
        var edgeTypes = Enum.GetValues(typeof(EdgeType)).Cast<EdgeType>().ToList();
        var nodes = new List<GraphNode>
        {
            new() { Id = "Source", Name = "Source", Kind = NodeKind.Type },
            new() { Id = "Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = edgeTypes.Select(t => new GraphEdge
        {
            FromId = "Source",
            ToId = "Target",
            Type = t
        }).ToList();

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(edgeTypes.Count, readEdges.Count);
        foreach (var edgeType in edgeTypes)
        {
            Assert.Contains(readEdges, e => e.Type == edgeType);
        }
    }

    [Fact]
    public async Task WriteAndRead_AllAccessibilityLevels_Roundtrip()
    {
        var accessLevels = Enum.GetValues(typeof(Accessibility)).Cast<Accessibility>().ToList();
        var nodes = accessLevels.Select((acc, i) => new GraphNode
        {
            Id = $"Test.Class{i}",
            Name = $"Class{i}",
            Kind = NodeKind.Type,
            Accessibility = acc
        }).ToList();

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var (_, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(accessLevels.Count, readNodes.Count);
        foreach (var acc in accessLevels)
        {
            Assert.Contains(readNodes.Values, n => n.Accessibility == acc);
        }
    }

    [Fact]
    public async Task WriteAndRead_EdgeMetadata_Roundtrip()
    {
        var nodes = new List<GraphNode>
        {
            new() { Id = "A", Name = "A", Kind = NodeKind.Type },
            new() { Id = "B", Name = "B", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "A",
                ToId = "B",
                Type = EdgeType.Calls,
                Metadata = new Dictionary<string, string>
                {
                    ["call_site"] = "line 42",
                    ["confidence"] = "high"
                }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, MakeMetadata());

        var (_, _, readEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readEdges);
        Assert.Equal("line 42", readEdges[0].Metadata["call_site"]);
        Assert.Equal("high", readEdges[0].Metadata["confidence"]);
    }

    [Fact]
    public async Task WriteAndRead_NodeMetadata_Roundtrip()
    {
        var nodes = new List<GraphNode>
        {
            new()
            {
                Id = "MyApp.MyClass",
                Name = "MyClass",
                Kind = NodeKind.Type,
                Metadata = new Dictionary<string, string>
                {
                    ["assembly"] = "MyApp.Core",
                    ["is_abstract"] = "true"
                }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), MakeMetadata());

        var (_, readNodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(readNodes);
        var node = readNodes["MyApp.MyClass"];
        Assert.Equal("MyApp.Core", node.Metadata["assembly"]);
        Assert.Equal("true", node.Metadata["is_abstract"]);
    }
}
