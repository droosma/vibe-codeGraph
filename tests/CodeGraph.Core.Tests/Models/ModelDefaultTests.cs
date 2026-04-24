using CodeGraph.Core.Models;

namespace CodeGraph.Core.Tests.Models;

public class GraphNodeDefaultTests
{
    [Fact]
    public void Default_Id_IsEmpty()
    {
        var node = new GraphNode();
        Assert.Equal(string.Empty, node.Id);
    }

    [Fact]
    public void Default_Name_IsEmpty()
    {
        var node = new GraphNode();
        Assert.Equal(string.Empty, node.Name);
    }

    [Fact]
    public void Default_FilePath_IsEmpty()
    {
        var node = new GraphNode();
        Assert.Equal(string.Empty, node.FilePath);
    }

    [Fact]
    public void Default_Signature_IsEmpty()
    {
        var node = new GraphNode();
        Assert.Equal(string.Empty, node.Signature);
    }

    [Fact]
    public void Default_AssemblyName_IsEmpty()
    {
        var node = new GraphNode();
        Assert.Equal(string.Empty, node.AssemblyName);
    }

    [Fact]
    public void Default_DocComment_IsNull()
    {
        var node = new GraphNode();
        Assert.Null(node.DocComment);
    }

    [Fact]
    public void Default_ContainingTypeId_IsNull()
    {
        var node = new GraphNode();
        Assert.Null(node.ContainingTypeId);
    }

    [Fact]
    public void Default_ContainingNamespaceId_IsNull()
    {
        var node = new GraphNode();
        Assert.Null(node.ContainingNamespaceId);
    }

    [Fact]
    public void Default_Kind_IsNamespace()
    {
        var node = new GraphNode();
        Assert.Equal(NodeKind.Namespace, node.Kind);
    }

    [Fact]
    public void Default_Accessibility_IsPublic()
    {
        var node = new GraphNode();
        Assert.Equal(Accessibility.Public, node.Accessibility);
    }

    [Fact]
    public void Default_StartLine_IsZero()
    {
        var node = new GraphNode();
        Assert.Equal(0, node.StartLine);
    }

    [Fact]
    public void Default_EndLine_IsZero()
    {
        var node = new GraphNode();
        Assert.Equal(0, node.EndLine);
    }

    [Fact]
    public void Default_Metadata_IsEmptyDictionary()
    {
        var node = new GraphNode();
        Assert.NotNull(node.Metadata);
        Assert.Empty(node.Metadata);
    }
}

public class GraphEdgeDefaultTests
{
    [Fact]
    public void Default_FromId_IsEmpty()
    {
        var edge = new GraphEdge();
        Assert.Equal(string.Empty, edge.FromId);
    }

    [Fact]
    public void Default_ToId_IsEmpty()
    {
        var edge = new GraphEdge();
        Assert.Equal(string.Empty, edge.ToId);
    }

    [Fact]
    public void Default_Type_IsContains()
    {
        var edge = new GraphEdge();
        Assert.Equal(EdgeType.Contains, edge.Type);
    }

    [Fact]
    public void Default_IsExternal_IsFalse()
    {
        var edge = new GraphEdge();
        Assert.False(edge.IsExternal);
    }

    [Fact]
    public void Default_PackageSource_IsNull()
    {
        var edge = new GraphEdge();
        Assert.Null(edge.PackageSource);
    }

    [Fact]
    public void Default_SourceLink_IsNull()
    {
        var edge = new GraphEdge();
        Assert.Null(edge.SourceLink);
    }

    [Fact]
    public void Default_Resolution_IsNull()
    {
        var edge = new GraphEdge();
        Assert.Null(edge.Resolution);
    }

    [Fact]
    public void Default_Metadata_IsEmptyDictionary()
    {
        var edge = new GraphEdge();
        Assert.NotNull(edge.Metadata);
        Assert.Empty(edge.Metadata);
    }
}

public class GraphMetadataDefaultTests
{
    [Fact]
    public void Default_SchemaVersion_IsCurrentVersion()
    {
        var meta = new GraphMetadata();
        Assert.Equal(GraphSchema.CurrentVersion, meta.SchemaVersion);
    }

    [Fact]
    public void Default_CommitHash_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.Equal(string.Empty, meta.CommitHash);
    }

    [Fact]
    public void Default_Branch_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.Equal(string.Empty, meta.Branch);
    }

    [Fact]
    public void Default_IndexerVersion_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.Equal(string.Empty, meta.IndexerVersion);
    }

    [Fact]
    public void Default_Solution_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.Equal(string.Empty, meta.Solution);
    }

    [Fact]
    public void Default_SolutionName_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.Equal(string.Empty, meta.SolutionName);
    }

    [Fact]
    public void Default_ProjectsIndexed_IsEmpty()
    {
        var meta = new GraphMetadata();
        Assert.NotNull(meta.ProjectsIndexed);
        Assert.Empty(meta.ProjectsIndexed);
    }

    [Fact]
    public void Default_Stats_IsEmptyDictionary()
    {
        var meta = new GraphMetadata();
        Assert.NotNull(meta.Stats);
        Assert.Empty(meta.Stats);
    }
}

public class ProjectGraphDefaultTests
{
    [Fact]
    public void Default_ProjectOrNamespace_IsEmpty()
    {
        var graph = new ProjectGraph();
        Assert.Equal(string.Empty, graph.ProjectOrNamespace);
    }

    [Fact]
    public void Default_Nodes_IsEmptyDictionary()
    {
        var graph = new ProjectGraph();
        Assert.NotNull(graph.Nodes);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void Default_Edges_IsEmptyList()
    {
        var graph = new ProjectGraph();
        Assert.NotNull(graph.Edges);
        Assert.Empty(graph.Edges);
    }
}
