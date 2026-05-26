using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.IO;

public class CrossSolutionLinkerTests
{
    [Fact]
    public void Link_ExternalEdgeMatchesInternalNode_ReturnsInferredEdge()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Services.OrderService"] = new()
            {
                Id = "Backend.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Frontend.Controllers.OrderController"] = new()
            {
                Id = "Frontend.Controllers.OrderController",
                Name = "OrderController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Controllers.OrderController",
                ToId = "Backend.Services.OrderService",
                Type = EdgeType.Calls,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        var edge = newEdges[0];
        Assert.Equal("Frontend.Controllers.OrderController", edge.FromId);
        Assert.Equal("Backend.Services.OrderService", edge.ToId);
        Assert.Equal(EdgeType.Calls, edge.Type);
        Assert.False(edge.IsExternal);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal("cross-solution", edge.Resolution);
        Assert.Equal("true", edge.Metadata["cross_solution"]);

        Assert.Single(resolvedIds);
        Assert.Contains("Backend.Services.OrderService", resolvedIds);
    }

    [Fact]
    public void Link_ExternalEdgeToNuGetPackage_NotResolved()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Services.OrderService"] = new()
            {
                Id = "Backend.Services.OrderService",
                Name = "OrderService",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Backend.Services.OrderService",
                ToId = "Newtonsoft.Json.JsonConvert",
                Type = EdgeType.Calls,
                IsExternal = true,
                PackageSource = "NuGet"
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_NoExternalEdges_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.ClassA"] = new() { Id = "A.ClassA", Name = "ClassA", Kind = NodeKind.Type },
            ["A.ClassB"] = new() { Id = "A.ClassB", Name = "ClassB", Kind = NodeKind.Type }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "A.ClassA",
                ToId = "A.ClassB",
                Type = EdgeType.Calls,
                IsExternal = false
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_FuzzyMatchSingleCandidate_ReturnsInferredEdge()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Data.Repository"] = new()
            {
                Id = "Backend.Data.Repository",
                Name = "Repository",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Frontend.Controllers.HomeController"] = new()
            {
                Id = "Frontend.Controllers.HomeController",
                Name = "HomeController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        // The external edge uses a different fully-qualified ID that doesn't exist,
        // but the last segment "Repository" matches exactly one node
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Controllers.HomeController",
                ToId = "External.Data.Repository",
                Type = EdgeType.References,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        var edge = newEdges[0];
        Assert.Equal("Frontend.Controllers.HomeController", edge.FromId);
        Assert.Equal("Backend.Data.Repository", edge.ToId);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal("cross-solution-fuzzy", edge.Resolution);
        Assert.Equal("External.Data.Repository", edge.Metadata["original_target"]);
    }

    [Fact]
    public void Link_FuzzyMatchMultipleCandidates_DoesNotResolve()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Data.Repository"] = new()
            {
                Id = "Backend.Data.Repository",
                Name = "Repository",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Shared.Data.Repository"] = new()
            {
                Id = "Shared.Data.Repository",
                Name = "Repository",
                Kind = NodeKind.Type,
                AssemblyName = "Shared"
            },
            ["Frontend.HomeController"] = new()
            {
                Id = "Frontend.HomeController",
                Name = "HomeController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.HomeController",
                ToId = "External.Data.Repository",
                Type = EdgeType.References,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        // Ambiguous match — should not resolve
        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_FuzzyMatchSingleSegmentExternalId_UsesWholeTargetName()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Data.Repository"] = new()
            {
                Id = "Backend.Data.Repository",
                Name = "Repository",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Frontend.HomeController"] = new()
            {
                Id = "Frontend.HomeController",
                Name = "HomeController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.HomeController",
                ToId = "Repository",
                Type = EdgeType.References,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        var edge = Assert.Single(newEdges);
        Assert.Equal("Backend.Data.Repository", edge.ToId);
        Assert.Equal("Repository", edge.Metadata["original_target"]);

        var resolvedId = Assert.Single(resolvedIds);
        Assert.Equal("Repository", resolvedId);
    }

    [Fact]
    public void Link_MultipleExternalEdgesResolved_DeduplicatesResolvedIds()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Shared.Models.User"] = new()
            {
                Id = "Shared.Models.User",
                Name = "User",
                Kind = NodeKind.Type,
                AssemblyName = "Shared"
            },
            ["Frontend.UserController"] = new()
            {
                Id = "Frontend.UserController",
                Name = "UserController",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            },
            ["Frontend.UserService"] = new()
            {
                Id = "Frontend.UserService",
                Name = "UserService",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.UserController",
                ToId = "Shared.Models.User",
                Type = EdgeType.References,
                IsExternal = true
            },
            new()
            {
                FromId = "Frontend.UserService",
                ToId = "Shared.Models.User",
                Type = EdgeType.References,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Equal(2, newEdges.Count);
        // Both edges point to the same target, but resolved IDs are deduplicated
        Assert.Single(resolvedIds);
        Assert.Equal("Shared.Models.User", resolvedIds[0]);
    }

    [Fact]
    public void Link_PreservesExistingMetadata()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Service"] = new()
            {
                Id = "Backend.Service",
                Name = "Service",
                Kind = NodeKind.Type,
                AssemblyName = "Backend"
            },
            ["Frontend.Client"] = new()
            {
                Id = "Frontend.Client",
                Name = "Client",
                Kind = NodeKind.Type,
                AssemblyName = "Frontend"
            }
        };

        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Client",
                ToId = "Backend.Service",
                Type = EdgeType.Calls,
                IsExternal = true,
                Metadata = new Dictionary<string, string> { ["call_site"] = "line 42" }
            }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("line 42", newEdges[0].Metadata["call_site"]);
        Assert.Equal("true", newEdges[0].Metadata["cross_solution"]);
    }

    [Fact]
    public void Link_ExactMatch_SetsIsExternalFalse()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Target"] = new() { Id = "A.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B.Caller", ToId = "A.Target", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.False(newEdges[0].IsExternal);
    }

    [Fact]
    public void Link_ExactMatch_SetsConfidenceInferred()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Target"] = new() { Id = "A.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B.Caller", ToId = "A.Target", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal(EdgeConfidence.Inferred, newEdges[0].Confidence);
    }

    [Fact]
    public void Link_ExactMatch_SetsResolutionCrossSolution()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Target"] = new() { Id = "A.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B.Caller", ToId = "A.Target", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("cross-solution", newEdges[0].Resolution);
    }

    [Fact]
    public void Link_FuzzyMatch_SetsResolutionCrossSolutionFuzzy()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Repo"] = new() { Id = "Internal.Repo", Name = "Repo", Kind = NodeKind.Type },
            ["Other.Caller"] = new() { Id = "Other.Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Caller", ToId = "External.Lib.Repo", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("cross-solution-fuzzy", newEdges[0].Resolution);
    }

    [Fact]
    public void Link_FuzzyMatch_SetsOriginalTargetMetadata()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Repo"] = new() { Id = "Internal.Repo", Name = "Repo", Kind = NodeKind.Type },
            ["Other.Caller"] = new() { Id = "Other.Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Caller", ToId = "External.Lib.Repo", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("External.Lib.Repo", newEdges[0].Metadata["original_target"]);
    }

    [Fact]
    public void Link_FuzzyMatch_PreservesEdgeType()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Base"] = new() { Id = "Internal.Base", Name = "Base", Kind = NodeKind.Type },
            ["Other.Child"] = new() { Id = "Other.Child", Name = "Child", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Child", ToId = "External.Lib.Base", Type = EdgeType.Inherits, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal(EdgeType.Inherits, newEdges[0].Type);
    }

    [Fact]
    public void Link_FuzzyMatch_PreservesFromId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Repo"] = new() { Id = "Internal.Repo", Name = "Repo", Kind = NodeKind.Type },
            ["Other.Caller"] = new() { Id = "Other.Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Caller", ToId = "External.Lib.Repo", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("Other.Caller", newEdges[0].FromId);
    }

    [Fact]
    public void Link_FuzzyMatch_SetsIsExternalFalse()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Repo"] = new() { Id = "Internal.Repo", Name = "Repo", Kind = NodeKind.Type },
            ["Other.Caller"] = new() { Id = "Other.Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Caller", ToId = "External.Lib.Repo", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.False(newEdges[0].IsExternal);
    }

    [Fact]
    public void Link_FuzzyMatch_TargetIdWithoutDot_UsesWholeId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Internal.Data.Repository"] = new() { Id = "Internal.Data.Repository", Name = "Repository", Kind = NodeKind.Type },
            ["Other.Caller"] = new() { Id = "Other.Caller", Name = "Caller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "Other.Caller", ToId = "Repository", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("Internal.Data.Repository", newEdges[0].ToId);
        Assert.Equal("cross-solution-fuzzy", newEdges[0].Resolution);
    }

    [Fact]
    public void Link_FuzzyMatch_SelfEdgeFiltered_NoMatch()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Service"] = new() { Id = "A.Service", Name = "Service", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Service", ToId = "External.Lib.Service", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_FuzzyMatch_SelfEdgeFiltered_OtherCandidateMatches()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Service"] = new() { Id = "A.Service", Name = "Service", Kind = NodeKind.Type },
            ["B.Service"] = new() { Id = "B.Service", Name = "Service", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "A.Service", ToId = "External.Lib.Service", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal("B.Service", newEdges[0].ToId);
    }

    [Fact]
    public void Link_ExactMatch_PreservesEdgeType()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.IService"] = new() { Id = "A.IService", Name = "IService", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B.Impl", ToId = "A.IService", Type = EdgeType.Implements, IsExternal = true }
        };

        var (newEdges, _) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Single(newEdges);
        Assert.Equal(EdgeType.Implements, newEdges[0].Type);
    }

    [Fact]
    public void Link_EmptyEdgeList_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["A.Target"] = new() { Id = "A.Target", Name = "Target", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>();

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_EmptyNodeDict_ReturnsEmpty()
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>
        {
            new() { FromId = "B.Caller", ToId = "A.Target", Type = EdgeType.Calls, IsExternal = true }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        Assert.Empty(newEdges);
        Assert.Empty(resolvedIds);
    }

    [Fact]
    public void Link_ExactMatch_WithDuplicateNameCandidates_PrefersExactTarget()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Services.Repository"] = new() { Id = "Backend.Services.Repository", Name = "Repository", Kind = NodeKind.Type },
            ["Shared.Repository"] = new() { Id = "Shared.Repository", Name = "Repository", Kind = NodeKind.Type },
            ["Frontend.Controller"] = new() { Id = "Frontend.Controller", Name = "Controller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Controller",
                ToId = "Backend.Services.Repository",
                Type = EdgeType.Calls,
                IsExternal = true
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        var edge = Assert.Single(newEdges);
        Assert.Equal("Backend.Services.Repository", edge.ToId);
        Assert.Equal("cross-solution", edge.Resolution);
        Assert.False(edge.Metadata.ContainsKey("original_target"));
        Assert.Single(resolvedIds);
        Assert.Equal("Backend.Services.Repository", resolvedIds[0]);
    }

    [Fact]
    public void Link_FuzzyMatch_PreservesMetadataAndTracksOriginalExternalId()
    {
        var nodes = new Dictionary<string, GraphNode>
        {
            ["Backend.Data.Repository"] = new() { Id = "Backend.Data.Repository", Name = "Repository", Kind = NodeKind.Type },
            ["Frontend.Controller"] = new() { Id = "Frontend.Controller", Name = "Controller", Kind = NodeKind.Type }
        };
        var edges = new List<GraphEdge>
        {
            new()
            {
                FromId = "Frontend.Controller",
                ToId = "External.Data.Repository",
                Type = EdgeType.References,
                IsExternal = true,
                Metadata = new Dictionary<string, string> { ["source"] = "api" }
            }
        };

        var (newEdges, resolvedIds) = CrossSolutionLinker.Link(nodes, edges);

        var edge = Assert.Single(newEdges);
        Assert.Equal("Backend.Data.Repository", edge.ToId);
        Assert.Equal("api", edge.Metadata["source"]);
        Assert.Equal("true", edge.Metadata["cross_solution"]);
        Assert.Equal("External.Data.Repository", edge.Metadata["original_target"]);
        Assert.Single(resolvedIds);
        Assert.Equal("External.Data.Repository", resolvedIds[0]);
    }
}
