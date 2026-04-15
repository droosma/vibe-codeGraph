namespace CodeGraph.Core.Models;

public record GraphNode
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public NodeKind Kind { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Signature { get; init; } = string.Empty;
    public string? DocComment { get; init; }
    public string? ContainingTypeId { get; init; }
    public string? ContainingNamespaceId { get; init; }
    public Accessibility Accessibility { get; init; }
    /// <summary>Assembly or project name this node belongs to. Used for graph splitting.</summary>
    public string AssemblyName { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum NodeKind { Namespace, Type, Method, Property, Field, Event, Constructor }
public enum Accessibility { Public, Internal, Protected, Private, ProtectedInternal, PrivateProtected }
