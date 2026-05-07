using CodeGraph.Core.Models;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Query.Tests;

public class GraphDiffFormatterTests
{
    [Fact]
    public void ContextFormatter_IncludesSections()
    {
        var diff = CreateDiffResult();

        var output = GraphDiffContextFormatter.Format(diff);

        Assert.Contains("# Graph Diff: abc1234..def5678", output);
        Assert.Contains("## Added Nodes (1)", output);
        Assert.Contains("## Removed Nodes (1)", output);
        Assert.Contains("## Changed Signatures (1)", output);
        Assert.Contains("## New Edges (1)", output);
        Assert.Contains("## Removed Edges (1)", output);
        Assert.Contains("- Was: PlaceOrder(OrderRequest)", output);
        Assert.Contains("+ Now: PlaceOrder(OrderRequest, CancellationToken)", output);
    }

    [Fact]
    public void TextFormatter_IncludesCounts()
    {
        var diff = CreateDiffResult();

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Graph Diff abc1234..def5678", output);
        Assert.Contains("Added nodes: 1", output);
        Assert.Contains("Removed nodes: 1", output);
        Assert.Contains("Signature changes: 1", output);
        Assert.Contains("Added edges: 1", output);
        Assert.Contains("Removed edges: 1", output);
    }

    [Fact]
    public void JsonFormatter_UsesCamelCase()
    {
        var diff = CreateDiffResult();

        var output = GraphDiffJsonFormatter.Format(diff);

        Assert.Contains("\"addedNodes\"", output);
        Assert.Contains("\"removedNodes\"", output);
        Assert.Contains("\"signatureChangedNodes\"", output);
        Assert.Contains("\"addedEdges\"", output);
        Assert.Contains("\"removedEdges\"", output);
    }

    private static GraphDiffResult CreateDiffResult()
    {
        return new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abc1234", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "def5678", Branch = "feature", GeneratedAt = DateTimeOffset.UtcNow },
            AddedNodes =
            [
                new GraphNode
                {
                    Id = "MyApp.Services.PaymentService",
                    Name = "PaymentService",
                    Kind = NodeKind.Type,
                    FilePath = "src/Payments/PaymentService.cs",
                    StartLine = 1,
                    EndLine = 45
                }
            ],
            RemovedNodes = [new GraphNode { Id = "MyApp.Services.LegacyOrderProcessor", Name = "LegacyOrderProcessor", Kind = NodeKind.Type }],
            SignatureChangedNodes =
            [
                new GraphSignatureChange
                {
                    Previous = new GraphNode
                    {
                        Id = "MyApp.Services.OrderService.PlaceOrder",
                        Name = "PlaceOrder",
                        Kind = NodeKind.Method,
                        Signature = "PlaceOrder(OrderRequest)"
                    },
                    Current = new GraphNode
                    {
                        Id = "MyApp.Services.OrderService.PlaceOrder",
                        Name = "PlaceOrder",
                        Kind = NodeKind.Method,
                        Signature = "PlaceOrder(OrderRequest, CancellationToken)"
                    }
                }
            ],
            AddedEdges = [new GraphEdge { FromId = "PaymentService", ToId = "IPaymentGateway", Type = EdgeType.Calls }],
            RemovedEdges = [new GraphEdge { FromId = "OrderController", ToId = "LegacyOrderProcessor", Type = EdgeType.Calls }]
        };
    }

    [Fact]
    public void TextFormatter_ShortCommit_NotTruncated()
    {
        // Targets L29: commitHash.Length > 7 condition
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abc", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "xyz", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Graph Diff abc..xyz", output);
    }

    [Fact]
    public void TextFormatter_EmptyCommit_ShowsUnknown()
    {
        // Targets L27: empty commit NoCoverage
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Graph Diff unknown..unknown", output);
    }

    [Fact]
    public void TextFormatter_Exactly7CharCommit_NotTruncated()
    {
        // Targets L29: equality mutation (> vs >=)
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "1234567", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "abcdefg", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Graph Diff 1234567..abcdefg", output);
    }

    [Fact]
    public void TextFormatter_8CharCommit_Truncated()
    {
        // Length > 7 with 8 chars should be truncated
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "12345678", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "abcdefgh", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Graph Diff 1234567..abcdefg", output);
        Assert.DoesNotContain("12345678", output);
    }

    [Fact]
    public void TextFormatter_EmptyDiff_AllCountsZero()
    {
        var diff = new GraphDiffResult
        {
            BaseMetadata = new GraphMetadata { CommitHash = "abc1234", Branch = "main", GeneratedAt = DateTimeOffset.UtcNow },
            HeadMetadata = new GraphMetadata { CommitHash = "def5678", Branch = "feat", GeneratedAt = DateTimeOffset.UtcNow }
        };

        var output = GraphDiffTextFormatter.Format(diff);

        Assert.Contains("Added nodes: 0", output);
        Assert.Contains("Removed nodes: 0", output);
        Assert.Contains("Signature changes: 0", output);
        Assert.Contains("Added edges: 0", output);
        Assert.Contains("Removed edges: 0", output);
    }
}
