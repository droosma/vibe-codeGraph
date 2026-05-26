using System.Text.Json;
using CodeGraph.Core.IO.Sqlite;
using CodeGraph.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeGraph.Core.Tests.IO.Sqlite;

public sealed class SqliteGraphPersistenceIntegrityTests : IDisposable
{
    private readonly string _dbDirectory;
    private readonly string _dbPath;

    public SqliteGraphPersistenceIntegrityTests()
    {
        _dbDirectory = Path.Combine(Path.GetTempPath(), $"codegraph-sqlite-integrity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dbDirectory);
        _dbPath = Path.Combine(_dbDirectory, "graph.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dbDirectory))
            Directory.Delete(_dbDirectory, true);
    }

    [Fact]
    public async Task WriteAsync_RoundTrip_PreservesEveryPersistedField()
    {
        var metadata = new GraphMetadata
        {
            SchemaVersion = 7,
            CommitHash = "commit-1234567890",
            Branch = "feature/sqlite-integrity",
            GeneratedAt = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.FromHours(-7)),
            IndexerVersion = "9.8.7-test",
            Solution = "CodeGraph.sln",
            SolutionName = "CodeGraph",
            ProjectsIndexed = new[] { "CodeGraph.Core", "CodeGraph.Query", "CodeGraph.Indexer" },
            Stats = new Dictionary<string, int>
            {
                ["nodes"] = 2,
                ["edges"] = 2,
                ["external_edges"] = 1
            }
        };

        var nodes = new[]
        {
            new GraphNode
            {
                Id = "CodeGraph.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                FilePath = "src/Services/OrderService.cs",
                StartLine = 14,
                EndLine = 89,
                Signature = "public sealed class OrderService",
                DocComment = "Coordinates order workflows.",
                ContainingTypeId = "CodeGraph.Services.RootContainer",
                ContainingNamespaceId = "CodeGraph.Services",
                Accessibility = Accessibility.ProtectedInternal,
                AssemblyName = "CodeGraph.Core",
                Metadata = new Dictionary<string, string>
                {
                    ["role"] = "application-service",
                    ["source"] = "semantic-pass"
                }
            },
            new GraphNode
            {
                Id = "CodeGraph.Services.OrderService.PlaceOrder",
                Name = "PlaceOrder",
                Kind = NodeKind.Method,
                FilePath = "src/Services/OrderService.cs",
                StartLine = 30,
                EndLine = 44,
                Signature = "internal static string PlaceOrder(int orderId)",
                DocComment = null,
                ContainingTypeId = "CodeGraph.Services.OrderService",
                ContainingNamespaceId = "CodeGraph.Services",
                Accessibility = Accessibility.Internal,
                AssemblyName = "CodeGraph.Core",
                Metadata = new Dictionary<string, string>
                {
                    ["test-coverage"] = "covered",
                    ["route"] = "POST /orders"
                }
            }
        };

        var edges = new[]
        {
            new GraphEdge
            {
                FromId = nodes[0].Id,
                ToId = nodes[1].Id,
                Type = EdgeType.Contains,
                IsExternal = false,
                PackageSource = null,
                SourceLink = null,
                Resolution = null,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["containment"] = "declared-member"
                }
            },
            new GraphEdge
            {
                FromId = nodes[1].Id,
                ToId = "External.Payments.Client.Submit",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet:External.Payments",
                SourceLink = "https://packages.example/payments",
                Resolution = "resolved-via-package-reference",
                Confidence = EdgeConfidence.Inferred,
                Metadata = new Dictionary<string, string>
                {
                    ["dependency-kind"] = "package-call",
                    ["http-method"] = "POST"
                }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, metadata);

        var (actualMetadata, actualNodes, actualEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        AssertGraphMetadataEqual(metadata, actualMetadata);
        Assert.Equal(nodes.Length, actualNodes.Count);
        foreach (var expectedNode in nodes)
            AssertGraphNodeEqual(expectedNode, actualNodes[expectedNode.Id]);

        Assert.Equal(edges.Length, actualEdges.Count);
        foreach (var expectedEdge in edges)
        {
            var actualEdge = Assert.Single(actualEdges.Where(edge =>
                edge.FromId == expectedEdge.FromId &&
                edge.ToId == expectedEdge.ToId &&
                edge.Type == expectedEdge.Type));
            AssertGraphEdgeEqual(expectedEdge, actualEdge);
        }
    }

    [Fact]
    public async Task FindNodesApis_PreserveExactStoredFieldsAndMetadata()
    {
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "CodeGraph.Features.ReportController",
                Name = "ReportController",
                Kind = NodeKind.Type,
                FilePath = "src/Features/ReportController.cs",
                StartLine = 11,
                EndLine = 120,
                Signature = "public class ReportController",
                DocComment = "Handles report requests.",
                ContainingTypeId = null,
                ContainingNamespaceId = "CodeGraph.Features",
                Accessibility = Accessibility.Public,
                AssemblyName = "CodeGraph.Web",
                Metadata = new Dictionary<string, string>
                {
                    ["layer"] = "api",
                    ["area"] = "reporting"
                }
            },
            new GraphNode
            {
                Id = "CodeGraph.Features.ReportController.GetDailyReport",
                Name = "GetDailyReport",
                Kind = NodeKind.Method,
                FilePath = "src/Features/ReportController.cs",
                StartLine = 33,
                EndLine = 48,
                Signature = "private Task<string> GetDailyReport(DateOnly day)",
                DocComment = "Loads the daily report.",
                ContainingTypeId = "CodeGraph.Features.ReportController",
                ContainingNamespaceId = "CodeGraph.Features",
                Accessibility = Accessibility.Private,
                AssemblyName = "CodeGraph.Web",
                Metadata = new Dictionary<string, string>
                {
                    ["returns"] = "json",
                    ["cache"] = "24h"
                }
            },
            new GraphNode
            {
                Id = "CodeGraph.Features.LegacyController",
                Name = "LegacyController",
                Kind = NodeKind.Type,
                FilePath = "src/Features/LegacyController.cs",
                StartLine = 5,
                EndLine = 80,
                Signature = "internal class LegacyController",
                DocComment = null,
                ContainingTypeId = null,
                ContainingNamespaceId = "CodeGraph.Features",
                Accessibility = Accessibility.Internal,
                AssemblyName = "CodeGraph.Web",
                Metadata = new Dictionary<string, string>
                {
                    ["layer"] = "api",
                    ["status"] = "legacy"
                }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), CreateMetadata("FindNodes"));

        var nodesByIds = await SqliteGraphReader.FindNodesByIds(
            _dbPath,
            new[] { nodes[1].Id, "CodeGraph.Features.DoesNotExist" });

        Assert.Single(nodesByIds);
        AssertGraphNodeEqual(nodes[1], nodesByIds[nodes[1].Id]);

        var nodesByName = await SqliteGraphReader.FindNodesByName(_dbPath, "%Report%");

        Assert.Equal(2, nodesByName.Count);
        var reportController = Assert.Single(nodesByName.Where(node => node.Id == nodes[0].Id));
        var getDailyReport = Assert.Single(nodesByName.Where(node => node.Id == nodes[1].Id));
        AssertGraphNodeEqual(nodes[0], reportController);
        AssertGraphNodeEqual(nodes[1], getDailyReport);
    }

    [Fact]
    public async Task AppendAsync_ReindexingSolution_ReplacesOnlyThatSolutionAndCleansScopedMetadata()
    {
        var writer = new SqliteGraphWriter();

        var alphaV1Node = new GraphNode
        {
            Id = "Alpha.OldService",
            Name = "OldService",
            Kind = NodeKind.Type,
            FilePath = "src/Alpha/OldService.cs",
            StartLine = 10,
            EndLine = 40,
            Signature = "public class OldService",
            DocComment = "Original alpha service.",
            ContainingTypeId = null,
            ContainingNamespaceId = "Alpha",
            Accessibility = Accessibility.Public,
            AssemblyName = "Alpha.Assembly",
            Metadata = new Dictionary<string, string>
            {
                ["alpha-version"] = "v1"
            }
        };
        var alphaV1Edge = new GraphEdge
        {
            FromId = alphaV1Node.Id,
            ToId = "External.Old.Dependency",
            Type = EdgeType.Calls,
            IsExternal = true,
            PackageSource = "NuGet:Alpha.Old",
            SourceLink = "https://alpha.example/old",
            Resolution = "old-resolution",
            Confidence = EdgeConfidence.Unresolved,
            Metadata = new Dictionary<string, string>
            {
                ["alpha-edge"] = "stale"
            }
        };

        var betaNode = new GraphNode
        {
            Id = "Beta.ReportService",
            Name = "ReportService",
            Kind = NodeKind.Type,
            FilePath = "src/Beta/ReportService.cs",
            StartLine = 8,
            EndLine = 55,
            Signature = "internal class ReportService",
            DocComment = "Beta reporting service.",
            ContainingTypeId = null,
            ContainingNamespaceId = "Beta",
            Accessibility = Accessibility.Internal,
            AssemblyName = "Beta.Assembly",
            Metadata = new Dictionary<string, string>
            {
                ["beta-owner"] = "team-reporting"
            }
        };
        var betaEdge = new GraphEdge
        {
            FromId = betaNode.Id,
            ToId = "External.Beta.Dependency",
            Type = EdgeType.DependsOn,
            IsExternal = true,
            PackageSource = "NuGet:Beta.Dependency",
            SourceLink = "https://beta.example/dependency",
            Resolution = "beta-resolution",
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["beta-edge"] = "keep"
            }
        };

        var alphaV2Node = new GraphNode
        {
            Id = "Alpha.NewService",
            Name = "NewService",
            Kind = NodeKind.Type,
            FilePath = "src/Alpha/NewService.cs",
            StartLine = 12,
            EndLine = 72,
            Signature = "public sealed class NewService",
            DocComment = "Replacement alpha service.",
            ContainingTypeId = null,
            ContainingNamespaceId = "Alpha",
            Accessibility = Accessibility.Protected,
            AssemblyName = "Alpha.Assembly",
            Metadata = new Dictionary<string, string>
            {
                ["alpha-version"] = "v2",
                ["alpha-state"] = "replacement"
            }
        };
        var alphaV2Edge = new GraphEdge
        {
            FromId = alphaV2Node.Id,
            ToId = "External.New.Dependency",
            Type = EdgeType.Calls,
            IsExternal = true,
            PackageSource = "NuGet:Alpha.New",
            SourceLink = "https://alpha.example/new",
            Resolution = "new-resolution",
            Confidence = EdgeConfidence.Inferred,
            Metadata = new Dictionary<string, string>
            {
                ["alpha-edge"] = "fresh"
            }
        };

        await writer.AppendAsync(_dbPath, new[] { alphaV1Node }, new[] { alphaV1Edge }, "Alpha");
        await writer.AppendAsync(_dbPath, new[] { betaNode }, new[] { betaEdge }, "Beta");
        await writer.AppendAsync(_dbPath, new[] { alphaV2Node }, new[] { alphaV2Edge }, "Alpha");

        var (_, actualNodes, actualEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(2, actualNodes.Count);
        Assert.False(actualNodes.ContainsKey(alphaV1Node.Id));
        Assert.True(actualNodes.ContainsKey(alphaV2Node.Id));
        Assert.True(actualNodes.ContainsKey(betaNode.Id));
        AssertGraphNodeEqual(AddSourceSolution(alphaV2Node, "Alpha"), actualNodes[alphaV2Node.Id]);
        AssertGraphNodeEqual(AddSourceSolution(betaNode, "Beta"), actualNodes[betaNode.Id]);

        Assert.Equal(2, actualEdges.Count);
        Assert.DoesNotContain(actualEdges, edge => edge.ToId == alphaV1Edge.ToId);
        AssertGraphEdgeEqual(alphaV2Edge, Assert.Single(actualEdges.Where(edge => edge.FromId == alphaV2Edge.FromId)));
        AssertGraphEdgeEqual(betaEdge, Assert.Single(actualEdges.Where(edge => edge.FromId == betaEdge.FromId)));

        var solutions = await ReadSolutionsMetadataAsync();
        Assert.Equal(new[] { "Alpha", "Beta" }, solutions.OrderBy(solution => solution).ToArray());
        Assert.Equal(2L, await CountRowsAsync("edge_metadata"));
        Assert.Equal(5L, await CountRowsAsync("node_metadata"));
    }

    [Theory]
    [InlineData(true, "NuGet:Reference.Package", "https://source.example/reference", "resolved-reference", EdgeConfidence.Inferred)]
    [InlineData(false, null, null, null, EdgeConfidence.Verified)]
    public async Task ReadAsync_EdgeFieldsRemainDistinctAcrossBooleanAndNullableCombinations(
        bool isExternal,
        string? packageSource,
        string? sourceLink,
        string? resolution,
        EdgeConfidence confidence)
    {
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Edge.Source",
                Name = "EdgeSource",
                Kind = NodeKind.Type,
                FilePath = "src/EdgeSource.cs",
                StartLine = 1,
                EndLine = 10,
                Signature = "class EdgeSource",
                Accessibility = Accessibility.Public,
                AssemblyName = "Edge.Tests"
            },
            new GraphNode
            {
                Id = "Edge.Target",
                Name = "EdgeTarget",
                Kind = NodeKind.Type,
                FilePath = "src/EdgeTarget.cs",
                StartLine = 1,
                EndLine = 10,
                Signature = "class EdgeTarget",
                Accessibility = Accessibility.Public,
                AssemblyName = "Edge.Tests"
            }
        };

        var expectedEdge = new GraphEdge
        {
            FromId = nodes[0].Id,
            ToId = nodes[1].Id,
            Type = EdgeType.References,
            IsExternal = isExternal,
            PackageSource = packageSource,
            SourceLink = sourceLink,
            Resolution = resolution,
            Confidence = confidence,
            Metadata = new Dictionary<string, string>
            {
                ["edge-case"] = isExternal ? "external" : "internal"
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, new[] { expectedEdge }, CreateMetadata($"EdgeCase-{isExternal}"));

        var (_, _, actualEdges) = await SqliteGraphReader.ReadAsync(_dbPath);
        var actualEdge = Assert.Single(actualEdges);

        AssertGraphEdgeEqual(expectedEdge, actualEdge);
    }

    [Fact]
    public void WriteAsync_DoesNotCaptureSynchronizationContext()
    {
        var writer = new SqliteGraphWriter();
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Sync.Writer.Source",
                Name = "SyncWriterSource",
                Kind = NodeKind.Type,
                FilePath = "src/SyncWriterSource.cs",
                Signature = "class SyncWriterSource",
                Accessibility = Accessibility.Public,
                AssemblyName = "Sync.Writer"
            },
            new GraphNode
            {
                Id = "Sync.Writer.Target",
                Name = "SyncWriterTarget",
                Kind = NodeKind.Method,
                FilePath = "src/SyncWriterTarget.cs",
                Signature = "void SyncWriterTarget()",
                Accessibility = Accessibility.Public,
                AssemblyName = "Sync.Writer"
            }
        };
        var edges = new[]
        {
            new GraphEdge
            {
                FromId = nodes[0].Id,
                ToId = nodes[1].Id,
                Type = EdgeType.Contains,
                Metadata = new Dictionary<string, string>
                {
                    ["sync"] = "writer"
                }
            }
        };

        var postCount = RunWithTrackingSynchronizationContext(() =>
            writer.WriteAsync(_dbPath, nodes, edges, CreateMetadata("SyncWriter")));

        Assert.Equal(0, postCount);
    }

    [Fact]
    public void AppendAsync_DoesNotCaptureSynchronizationContext()
    {
        var writer = new SqliteGraphWriter();
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Sync.Append.Node",
                Name = "SyncAppendNode",
                Kind = NodeKind.Type,
                FilePath = "src/SyncAppendNode.cs",
                Signature = "class SyncAppendNode",
                Accessibility = Accessibility.Public,
                AssemblyName = "Sync.Append",
                Metadata = new Dictionary<string, string>
                {
                    ["sync"] = "append"
                }
            }
        };
        var edges = new[]
        {
            new GraphEdge
            {
                FromId = nodes[0].Id,
                ToId = "External.Sync.Append",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet:Sync.Append",
                SourceLink = "https://sync.append/source",
                Resolution = "sync-append-resolution",
                Confidence = EdgeConfidence.Inferred,
                Metadata = new Dictionary<string, string>
                {
                    ["sync"] = "append-edge"
                }
            }
        };

        var postCount = RunWithTrackingSynchronizationContext(() =>
            writer.AppendAsync(_dbPath, nodes, edges, "SyncAppend"));

        Assert.Equal(0, postCount);
    }

    [Fact]
    public async Task ReaderApis_DoNotCaptureSynchronizationContext()
    {
        var writer = new SqliteGraphWriter();
        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Sync.Reader.Node",
                Name = "SyncReaderNode",
                Kind = NodeKind.Type,
                FilePath = "src/SyncReaderNode.cs",
                Signature = "class SyncReaderNode",
                Accessibility = Accessibility.Public,
                AssemblyName = "Sync.Reader",
                Metadata = new Dictionary<string, string>
                {
                    ["sync"] = "reader"
                }
            }
        };

        await writer.WriteAsync(_dbPath, nodes, Array.Empty<GraphEdge>(), CreateMetadata("SyncReader"));

        var readPostCount = RunWithTrackingSynchronizationContext(() => SqliteGraphReader.ReadAsync(_dbPath));
        var byIdsPostCount = RunWithTrackingSynchronizationContext(() => SqliteGraphReader.FindNodesByIds(_dbPath, nodes.Select(node => node.Id)));
        var byNamePostCount = RunWithTrackingSynchronizationContext(() => SqliteGraphReader.FindNodesByName(_dbPath, "%SyncReader%"));

        Assert.Equal(0, readPostCount);
        Assert.Equal(0, byIdsPostCount);
        Assert.Equal(0, byNamePostCount);
    }

    [Fact]
    public async Task ReadAsync_MissingMetadataEntries_UsesExpectedDefaults()
    {
        await ResetDatabaseWithSchemaAsync();
        await using var connection = await OpenReadWriteConnectionAsync();
        await InsertKeyValueRowsAsync(connection, "metadata", null, new Dictionary<string, string>
        {
            ["schema_version"] = "1"
        });

        var (metadata, nodes, edges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(1, metadata.SchemaVersion);
        Assert.Equal(string.Empty, metadata.CommitHash);
        Assert.Equal(string.Empty, metadata.Branch);
        Assert.Equal(DateTimeOffset.MinValue, metadata.GeneratedAt);
        Assert.Equal(string.Empty, metadata.IndexerVersion);
        Assert.Equal(string.Empty, metadata.Solution);
        Assert.Equal(string.Empty, metadata.SolutionName);
        Assert.Empty(metadata.ProjectsIndexed);
        Assert.Empty(metadata.Stats);
        Assert.Empty(nodes);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task ReadAsync_NonExistentDatabase_IncludesStableErrorMessage()
    {
        var missingPath = Path.Combine(_dbDirectory, $"missing-{Guid.NewGuid():N}.db");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => SqliteGraphReader.ReadAsync(missingPath));

        Assert.Equal(missingPath, exception.FileName);
        Assert.Contains("Graph database not found.", exception.Message);
    }

    [Fact]
    public async Task AppendAsync_ReindexingSolutionWithMultipleNodes_RemovesAllPriorSolutionNodes()
    {
        var writer = new SqliteGraphWriter();
        var originalNodes = new[]
        {
            new GraphNode
            {
                Id = "Multi.Old.One",
                Name = "OldOne",
                Kind = NodeKind.Type,
                FilePath = "src/MultiOldOne.cs",
                Signature = "class OldOne",
                Accessibility = Accessibility.Public,
                AssemblyName = "Multi.Solution"
            },
            new GraphNode
            {
                Id = "Multi.Old.Two",
                Name = "OldTwo",
                Kind = NodeKind.Method,
                FilePath = "src/MultiOldTwo.cs",
                Signature = "void OldTwo()",
                Accessibility = Accessibility.Internal,
                AssemblyName = "Multi.Solution"
            }
        };
        var replacementNode = new GraphNode
        {
            Id = "Multi.New.Only",
            Name = "NewOnly",
            Kind = NodeKind.Type,
            FilePath = "src/MultiNewOnly.cs",
            Signature = "class NewOnly",
            Accessibility = Accessibility.Public,
            AssemblyName = "Multi.Solution"
        };

        await writer.AppendAsync(_dbPath, originalNodes, Array.Empty<GraphEdge>(), "Multi");
        await writer.AppendAsync(_dbPath, new[] { replacementNode }, Array.Empty<GraphEdge>(), "Multi");

        var (_, nodes, _) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Single(nodes);
        Assert.True(nodes.ContainsKey(replacementNode.Id));
        Assert.False(nodes.ContainsKey(originalNodes[0].Id));
        Assert.False(nodes.ContainsKey(originalNodes[1].Id));
    }

    [Fact]
    public async Task WriteAsync_PersistsExactValuesInRawSqliteTables()
    {
        var metadata = new GraphMetadata
        {
            SchemaVersion = 12,
            CommitHash = "raw-writer-commit",
            Branch = "raw-writer-branch",
            GeneratedAt = new DateTimeOffset(2025, 4, 5, 6, 7, 8, TimeSpan.FromHours(2)),
            IndexerVersion = "2.3.4-raw",
            Solution = "RawWriter.sln",
            SolutionName = "RawWriter",
            ProjectsIndexed = new[] { "Raw.Writer.Project" },
            Stats = new Dictionary<string, int>
            {
                ["nodes"] = 2,
                ["edges"] = 2
            }
        };

        var nodes = new[]
        {
            new GraphNode
            {
                Id = "Raw.Writer.Source",
                Name = "RawWriterSource",
                Kind = NodeKind.Type,
                FilePath = "src/RawWriterSource.cs",
                StartLine = 12,
                EndLine = 70,
                Signature = "public class RawWriterSource",
                DocComment = "Stored exactly as written.",
                ContainingTypeId = "Raw.Writer.Container",
                ContainingNamespaceId = "Raw.Writer",
                Accessibility = Accessibility.PrivateProtected,
                AssemblyName = "Raw.Writer.Assembly",
                Metadata = new Dictionary<string, string>
                {
                    ["writer-key"] = "writer-value",
                    ["writer-mode"] = "exact"
                }
            },
            new GraphNode
            {
                Id = "Raw.Writer.Target",
                Name = "RawWriterTarget",
                Kind = NodeKind.Method,
                FilePath = "src/RawWriterTarget.cs",
                StartLine = 71,
                EndLine = 88,
                Signature = "internal void RawWriterTarget()",
                DocComment = null,
                ContainingTypeId = "Raw.Writer.Source",
                ContainingNamespaceId = "Raw.Writer",
                Accessibility = Accessibility.Internal,
                AssemblyName = "Raw.Writer.Assembly",
                Metadata = new Dictionary<string, string>
                {
                    ["writer-target"] = "secondary"
                }
            }
        };

        var edges = new[]
        {
            new GraphEdge
            {
                FromId = nodes[0].Id,
                ToId = nodes[1].Id,
                Type = EdgeType.Contains,
                IsExternal = false,
                PackageSource = null,
                SourceLink = null,
                Resolution = null,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["edge-kind"] = "internal"
                }
            },
            new GraphEdge
            {
                FromId = nodes[1].Id,
                ToId = "External.Raw.Dependency",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet:Raw.Writer.Package",
                SourceLink = "https://raw.writer/source",
                Resolution = "raw-writer-resolution",
                Confidence = EdgeConfidence.Unresolved,
                Metadata = new Dictionary<string, string>
                {
                    ["edge-kind"] = "external",
                    ["edge-source"] = "package"
                }
            }
        };

        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, nodes, edges, metadata);

        await using var connection = await OpenReadOnlyConnectionAsync();

        var metadataRows = await ReadStringDictionaryAsync(connection, "SELECT key, value FROM metadata");
        Assert.Equal("12", metadataRows["schema_version"]);
        Assert.Equal("raw-writer-commit", metadataRows["commit_hash"]);
        Assert.Equal("raw-writer-branch", metadataRows["branch"]);
        Assert.Equal(metadata.GeneratedAt.ToString("O"), metadataRows["generated_at"]);
        Assert.Equal("2.3.4-raw", metadataRows["indexer_version"]);
        Assert.Equal("RawWriter.sln", metadataRows["solution"]);
        Assert.Equal("RawWriter", metadataRows["solution_name"]);
        Assert.Equal(JsonSerializer.Serialize(metadata.ProjectsIndexed), metadataRows["projects_indexed"]);
        Assert.Equal("2", metadataRows["stat_nodes"]);
        Assert.Equal("2", metadataRows["stat_edges"]);

        using (var nodeCommand = connection.CreateCommand())
        {
            nodeCommand.CommandText = """
                SELECT id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                       containing_type_id, containing_namespace_id, accessibility, assembly_name
                FROM nodes
                WHERE id = $id
                """;
            nodeCommand.Parameters.AddWithValue("$id", nodes[0].Id);

            using var nodeReader = await nodeCommand.ExecuteReaderAsync();
            Assert.True(await nodeReader.ReadAsync());
            Assert.Equal(nodes[0].Id, nodeReader.GetString(0));
            Assert.Equal(nodes[0].Name, nodeReader.GetString(1));
            Assert.Equal((int)nodes[0].Kind, nodeReader.GetInt32(2));
            Assert.Equal(nodes[0].FilePath, nodeReader.GetString(3));
            Assert.Equal(nodes[0].StartLine, nodeReader.GetInt32(4));
            Assert.Equal(nodes[0].EndLine, nodeReader.GetInt32(5));
            Assert.Equal(nodes[0].Signature, nodeReader.GetString(6));
            Assert.Equal(nodes[0].DocComment, nodeReader.GetString(7));
            Assert.Equal(nodes[0].ContainingTypeId, nodeReader.GetString(8));
            Assert.Equal(nodes[0].ContainingNamespaceId, nodeReader.GetString(9));
            Assert.Equal((int)nodes[0].Accessibility, nodeReader.GetInt32(10));
            Assert.Equal(nodes[0].AssemblyName, nodeReader.GetString(11));
        }

        var nodeMetadata = await ReadStringDictionaryAsync(
            connection,
            "SELECT key, value FROM node_metadata WHERE node_id = $nodeId ORDER BY key",
            ("$nodeId", nodes[0].Id));
        AssertDictionaryEqual(nodes[0].Metadata, nodeMetadata);

        using (var edgeCommand = connection.CreateCommand())
        {
            edgeCommand.CommandText = """
                SELECT from_id, to_id, type, is_external, package_source, source_link, resolution, confidence
                FROM edges
                ORDER BY rowid
                """;

            using var edgeReader = await edgeCommand.ExecuteReaderAsync();
            Assert.True(await edgeReader.ReadAsync());
            Assert.Equal(edges[0].FromId, edgeReader.GetString(0));
            Assert.Equal(edges[0].ToId, edgeReader.GetString(1));
            Assert.Equal((int)edges[0].Type, edgeReader.GetInt32(2));
            Assert.Equal(0, edgeReader.GetInt32(3));
            Assert.True(edgeReader.IsDBNull(4));
            Assert.True(edgeReader.IsDBNull(5));
            Assert.True(edgeReader.IsDBNull(6));
            Assert.Equal((int)edges[0].Confidence, edgeReader.GetInt32(7));

            Assert.True(await edgeReader.ReadAsync());
            Assert.Equal(edges[1].FromId, edgeReader.GetString(0));
            Assert.Equal(edges[1].ToId, edgeReader.GetString(1));
            Assert.Equal((int)edges[1].Type, edgeReader.GetInt32(2));
            Assert.Equal(1, edgeReader.GetInt32(3));
            Assert.Equal(edges[1].PackageSource, edgeReader.GetString(4));
            Assert.Equal(edges[1].SourceLink, edgeReader.GetString(5));
            Assert.Equal(edges[1].Resolution, edgeReader.GetString(6));
            Assert.Equal((int)edges[1].Confidence, edgeReader.GetInt32(7));
        }

        var internalEdgeRowId = await ExecuteScalarAsync<long>(
            connection,
            "SELECT rowid FROM edges WHERE from_id = $fromId AND to_id = $toId AND type = $type",
            ("$fromId", edges[0].FromId),
            ("$toId", edges[0].ToId),
            ("$type", (int)edges[0].Type));
        var internalEdgeMetadata = await ReadStringDictionaryAsync(
            connection,
            "SELECT key, value FROM edge_metadata WHERE edge_rowid = $rowId ORDER BY key",
            ("$rowId", internalEdgeRowId));
        AssertDictionaryEqual(edges[0].Metadata, internalEdgeMetadata);

        var externalEdgeRowId = await ExecuteScalarAsync<long>(
            connection,
            "SELECT rowid FROM edges WHERE from_id = $fromId AND to_id = $toId AND type = $type",
            ("$fromId", edges[1].FromId),
            ("$toId", edges[1].ToId),
            ("$type", (int)edges[1].Type));
        var edgeMetadata = await ReadStringDictionaryAsync(
            connection,
            "SELECT key, value FROM edge_metadata WHERE edge_rowid = $rowId ORDER BY key",
            ("$rowId", externalEdgeRowId));
        AssertDictionaryEqual(edges[1].Metadata, edgeMetadata);
    }

    [Fact]
    public async Task ReadAsync_MapsExactValuesFromRawSqliteTables()
    {
        await ResetDatabaseWithSchemaAsync();
        await using var connection = await OpenReadWriteConnectionAsync();

        var metadata = new Dictionary<string, string>
        {
            ["schema_version"] = "15",
            ["commit_hash"] = "raw-reader-commit",
            ["branch"] = "raw-reader-branch",
            ["generated_at"] = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero).ToString("O"),
            ["indexer_version"] = "5.6.7-reader",
            ["solution"] = "RawReader.sln",
            ["solution_name"] = "RawReader",
            ["projects_indexed"] = JsonSerializer.Serialize(new[] { "Reader.Project.A", "Reader.Project.B" }),
            ["stat_nodes"] = "2",
            ["stat_edges"] = "2",
            ["stat_external_edges"] = "1"
        };
        await InsertKeyValueRowsAsync(connection, "metadata", null, metadata);

        var expectedNodes = new[]
        {
            new GraphNode
            {
                Id = "Raw.Reader.Type",
                Name = "RawReaderType",
                Kind = NodeKind.Type,
                FilePath = "src/RawReaderType.cs",
                StartLine = 5,
                EndLine = 65,
                Signature = "public class RawReaderType",
                DocComment = "Reader type documentation.",
                ContainingTypeId = "Raw.Reader.Container",
                ContainingNamespaceId = "Raw.Reader",
                Accessibility = Accessibility.ProtectedInternal,
                AssemblyName = "Raw.Reader.Assembly",
                Metadata = new Dictionary<string, string>
                {
                    ["reader-key"] = "reader-value",
                    ["reader-state"] = "primary"
                }
            },
            new GraphNode
            {
                Id = "Raw.Reader.Method",
                Name = "RawReaderMethod",
                Kind = NodeKind.Method,
                FilePath = "src/RawReaderMethod.cs",
                StartLine = 66,
                EndLine = 82,
                Signature = "internal void RawReaderMethod()",
                DocComment = null,
                ContainingTypeId = null,
                ContainingNamespaceId = "Raw.Reader",
                Accessibility = Accessibility.Internal,
                AssemblyName = "Raw.Reader.Assembly",
                Metadata = new Dictionary<string, string>
                {
                    ["reader-target"] = "secondary"
                }
            }
        };

        foreach (var node in expectedNodes)
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                INSERT INTO nodes (id, name, kind, file_path, start_line, end_line, signature, doc_comment,
                                   containing_type_id, containing_namespace_id, accessibility, assembly_name)
                VALUES ($id, $name, $kind, $filePath, $startLine, $endLine, $signature, $docComment,
                        $containingTypeId, $containingNamespaceId, $accessibility, $assemblyName)
                """,
                ("$id", node.Id),
                ("$name", node.Name),
                ("$kind", (int)node.Kind),
                ("$filePath", node.FilePath),
                ("$startLine", node.StartLine),
                ("$endLine", node.EndLine),
                ("$signature", node.Signature),
                ("$docComment", node.DocComment),
                ("$containingTypeId", node.ContainingTypeId),
                ("$containingNamespaceId", node.ContainingNamespaceId),
                ("$accessibility", (int)node.Accessibility),
                ("$assemblyName", node.AssemblyName));
            await InsertKeyValueRowsAsync(connection, "node_metadata", ("node_id", node.Id), node.Metadata);
        }

        var expectedEdges = new[]
        {
            new GraphEdge
            {
                FromId = expectedNodes[0].Id,
                ToId = expectedNodes[1].Id,
                Type = EdgeType.Contains,
                IsExternal = false,
                PackageSource = null,
                SourceLink = null,
                Resolution = null,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["reader-edge"] = "internal"
                }
            },
            new GraphEdge
            {
                FromId = expectedNodes[1].Id,
                ToId = "External.Reader.Dependency",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet:Raw.Reader.Package",
                SourceLink = "https://raw.reader/source",
                Resolution = "raw-reader-resolution",
                Confidence = EdgeConfidence.Inferred,
                Metadata = new Dictionary<string, string>
                {
                    ["reader-edge"] = "external",
                    ["reader-origin"] = "package"
                }
            }
        };

        foreach (var edge in expectedEdges)
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                INSERT INTO edges (from_id, to_id, type, is_external, package_source, source_link, resolution, confidence)
                VALUES ($fromId, $toId, $type, $isExternal, $packageSource, $sourceLink, $resolution, $confidence)
                """,
                ("$fromId", edge.FromId),
                ("$toId", edge.ToId),
                ("$type", (int)edge.Type),
                ("$isExternal", edge.IsExternal ? 1 : 0),
                ("$packageSource", edge.PackageSource),
                ("$sourceLink", edge.SourceLink),
                ("$resolution", edge.Resolution),
                ("$confidence", (int)edge.Confidence));

            var rowId = await ExecuteScalarAsync<long>(connection, "SELECT last_insert_rowid()");
            await InsertKeyValueRowsAsync(connection, "edge_metadata", ("edge_rowid", rowId), edge.Metadata);
        }

        var (actualMetadata, actualNodes, actualEdges) = await SqliteGraphReader.ReadAsync(_dbPath);

        Assert.Equal(15, actualMetadata.SchemaVersion);
        Assert.Equal("raw-reader-commit", actualMetadata.CommitHash);
        Assert.Equal("raw-reader-branch", actualMetadata.Branch);
        Assert.Equal(new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero), actualMetadata.GeneratedAt);
        Assert.Equal("5.6.7-reader", actualMetadata.IndexerVersion);
        Assert.Equal("RawReader.sln", actualMetadata.Solution);
        Assert.Equal("RawReader", actualMetadata.SolutionName);
        Assert.Equal(new[] { "Reader.Project.A", "Reader.Project.B" }, actualMetadata.ProjectsIndexed);
        Assert.Equal(3, actualMetadata.Stats.Count);
        Assert.Equal(2, actualMetadata.Stats["nodes"]);
        Assert.Equal(2, actualMetadata.Stats["edges"]);
        Assert.Equal(1, actualMetadata.Stats["external_edges"]);

        foreach (var expectedNode in expectedNodes)
            AssertGraphNodeEqual(expectedNode, actualNodes[expectedNode.Id]);

        foreach (var expectedEdge in expectedEdges)
            AssertGraphEdgeEqual(expectedEdge, Assert.Single(actualEdges.Where(edge => edge.FromId == expectedEdge.FromId)));

        var nodesByIds = await SqliteGraphReader.FindNodesByIds(_dbPath, expectedNodes.Select(node => node.Id));
        foreach (var expectedNode in expectedNodes)
            AssertGraphNodeEqual(expectedNode, nodesByIds[expectedNode.Id]);

        var nodesByName = await SqliteGraphReader.FindNodesByName(_dbPath, "%RawReader%");
        Assert.Equal(2, nodesByName.Count);
        AssertGraphNodeEqual(expectedNodes[0], Assert.Single(nodesByName.Where(node => node.Id == expectedNodes[0].Id)));
        AssertGraphNodeEqual(expectedNodes[1], Assert.Single(nodesByName.Where(node => node.Id == expectedNodes[1].Id)));
    }

    private static GraphMetadata CreateMetadata(string solutionName)
    {
        return new GraphMetadata
        {
            SchemaVersion = 1,
            CommitHash = $"commit-{solutionName}",
            Branch = $"branch-{solutionName}",
            GeneratedAt = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero),
            IndexerVersion = "1.0.0-test",
            Solution = $"{solutionName}.sln",
            SolutionName = solutionName,
            ProjectsIndexed = new[] { $"{solutionName}.Project" },
            Stats = new Dictionary<string, int>
            {
                ["nodes"] = 1,
                ["edges"] = 0
            }
        };
    }

    private static GraphNode AddSourceSolution(GraphNode node, string solutionName)
    {
        var metadata = new Dictionary<string, string>(node.Metadata)
        {
            ["source_solution"] = solutionName
        };

        return node with { Metadata = metadata };
    }

    private static void AssertGraphMetadataEqual(GraphMetadata expected, GraphMetadata actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.CommitHash, actual.CommitHash);
        Assert.Equal(expected.Branch, actual.Branch);
        Assert.Equal(expected.GeneratedAt, actual.GeneratedAt);
        Assert.Equal(expected.IndexerVersion, actual.IndexerVersion);
        Assert.Equal(expected.Solution, actual.Solution);
        Assert.Equal(expected.SolutionName, actual.SolutionName);
        Assert.Equal(expected.ProjectsIndexed, actual.ProjectsIndexed);
        AssertDictionaryEqual(expected.Stats, actual.Stats);
    }

    private static void AssertGraphNodeEqual(GraphNode expected, GraphNode actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.StartLine, actual.StartLine);
        Assert.Equal(expected.EndLine, actual.EndLine);
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.DocComment, actual.DocComment);
        Assert.Equal(expected.ContainingTypeId, actual.ContainingTypeId);
        Assert.Equal(expected.ContainingNamespaceId, actual.ContainingNamespaceId);
        Assert.Equal(expected.Accessibility, actual.Accessibility);
        Assert.Equal(expected.AssemblyName, actual.AssemblyName);
        AssertDictionaryEqual(expected.Metadata, actual.Metadata);
    }

    private static void AssertGraphEdgeEqual(GraphEdge expected, GraphEdge actual)
    {
        Assert.Equal(expected.FromId, actual.FromId);
        Assert.Equal(expected.ToId, actual.ToId);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.IsExternal, actual.IsExternal);
        Assert.Equal(expected.PackageSource, actual.PackageSource);
        Assert.Equal(expected.SourceLink, actual.SourceLink);
        Assert.Equal(expected.Resolution, actual.Resolution);
        Assert.Equal(expected.Confidence, actual.Confidence);
        AssertDictionaryEqual(expected.Metadata, actual.Metadata);
    }

    private static void AssertDictionaryEqual<TValue>(IReadOnlyDictionary<string, TValue> expected, IReadOnlyDictionary<string, TValue> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
        {
            Assert.True(actual.ContainsKey(pair.Key), $"Expected key '{pair.Key}' was not found.");
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    private static int RunWithTrackingSynchronizationContext(Func<Task> action)
    {
        var context = new TrackingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            action().GetAwaiter().GetResult();
            return context.PostCount;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => _postCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    private async Task ResetDatabaseWithSchemaAsync()
    {
        var writer = new SqliteGraphWriter();
        await writer.WriteAsync(_dbPath, Array.Empty<GraphNode>(), Array.Empty<GraphEdge>(), CreateMetadata("SchemaReset"));

        await using var connection = await OpenReadWriteConnectionAsync();
        await ExecuteNonQueryAsync(connection, "DELETE FROM edge_metadata");
        await ExecuteNonQueryAsync(connection, "DELETE FROM edges");
        await ExecuteNonQueryAsync(connection, "DELETE FROM node_metadata");
        await ExecuteNonQueryAsync(connection, "DELETE FROM nodes");
        await ExecuteNonQueryAsync(connection, "DELETE FROM metadata");
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Dictionary<string, string>> ReadStringDictionaryAsync(
        SqliteConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var values = new Dictionary<string, string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values[reader.GetString(0)] = reader.GetString(1);

        return values;
    }

    private static async Task InsertKeyValueRowsAsync(
        SqliteConnection connection,
        string tableName,
        (string ColumnName, object Value)? owner,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            if (owner is { } rowOwner)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    $"INSERT INTO {tableName} ({rowOwner.ColumnName}, key, value) VALUES ($owner, $key, $value)",
                    ("$owner", rowOwner.Value),
                    ("$key", pair.Key),
                    ("$value", pair.Value));
            }
            else
            {
                await ExecuteNonQueryAsync(
                    connection,
                    $"INSERT INTO {tableName} (key, value) VALUES ($key, $value)",
                    ("$key", pair.Key),
                    ("$value", pair.Value));
            }
        }
    }

    private async Task<List<string>> ReadSolutionsMetadataAsync()
    {
        await using var connection = await OpenReadOnlyConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = 'solutions'";

        var json = (string)(await command.ExecuteScalarAsync())!;
        return JsonSerializer.Deserialize<List<string>>(json)!;
    }

    private async Task<long> CountRowsAsync(string tableName)
    {
        await using var connection = await OpenReadOnlyConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<SqliteConnection> OpenReadWriteConnectionAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<SqliteConnection> OpenReadOnlyConnectionAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}

