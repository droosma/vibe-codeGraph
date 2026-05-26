using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

internal sealed class ExternalNodeCollector
{
    private readonly HashSet<string> _knownNodeIds;
    private readonly HashSet<string> _seenNodeIds = new(StringComparer.Ordinal);
    private readonly List<GraphNode> _nodes = [];

    public ExternalNodeCollector(HashSet<string> knownNodeIds)
    {
        _knownNodeIds = knownNodeIds;
    }

    public IReadOnlyList<GraphNode> Nodes => _nodes;

    public bool Contains(string id) => _seenNodeIds.Contains(id);

    public void Add(string id, Func<GraphNode> createNode)
    {
        if (_knownNodeIds.Contains(id) || !_seenNodeIds.Add(id))
        {
            return;
        }

        _nodes.Add(createNode());
    }

    public void AddSymbol(ISymbol symbol, string? id = null, Dictionary<string, string>? metadata = null)
    {
        var symbolId = id ?? SyntaxPass.GetSymbolId(symbol);
        Add(symbolId, () => PassUtilities.CreateExternalSymbolNode(symbol, symbolId, metadata));
    }

    public List<GraphNode> ToList() => [.. _nodes];
}

internal static class PassUtilities
{
    public static string GetRelativePath(string absolutePath, string solutionRoot)
    {
        if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(solutionRoot))
        {
            return absolutePath ?? string.Empty;
        }

        try
        {
            return Path.GetRelativePath(solutionRoot, absolutePath);
        }
        catch
        {
            return absolutePath;
        }
    }

    public static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name switch
            {
                GenericNameSyntax generic => generic.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => null
            },
            _ => null
        };
    }

    public static INamedTypeSymbol? ResolveNamedType(SemanticModel model, TypeSyntax typeSyntax)
    {
        try
        {
            return model.GetSymbolInfo(typeSyntax).Symbol as INamedTypeSymbol;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetContainingMethodId(SemanticModel model, SyntaxNode node)
    {
        var methodDeclaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDeclaration is null)
        {
            return null;
        }

        try
        {
            var symbol = model.GetDeclaredSymbol(methodDeclaration);
            return symbol is null ? null : SyntaxPass.GetSymbolId(symbol);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetContainingMemberId(SemanticModel model, SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax or EventDeclarationSyntax)
            {
                var symbol = model.GetDeclaredSymbol(current);
                return symbol is null ? null : SyntaxPass.GetSymbolId(symbol);
            }

            current = current.Parent;
        }

        return null;
    }

    public static GraphNode CreateExternalSymbolNode(
        ISymbol symbol,
        string id,
        Dictionary<string, string>? metadata = null)
    {
        var nodeMetadata = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);

        return new GraphNode
        {
            Id = id,
            Name = symbol.Name,
            Kind = GetNodeKind(symbol),
            FilePath = string.Empty,
            StartLine = 0,
            EndLine = 0,
            Signature = symbol.ToDisplayString(),
            Accessibility = SyntaxPass.MapAccessibility(symbol.DeclaredAccessibility),
            ContainingTypeId = symbol.ContainingType is not null
                ? SyntaxPass.GetSymbolId(symbol.ContainingType)
                : null,
            ContainingNamespaceId = symbol.ContainingNamespace is { IsGlobalNamespace: false }
                ? SyntaxPass.GetSymbolId(symbol.ContainingNamespace)
                : null,
            Metadata = nodeMetadata
        };
    }

    public static NodeKind GetNodeKind(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => NodeKind.Constructor,
            IMethodSymbol => NodeKind.Method,
            IPropertySymbol => NodeKind.Property,
            IFieldSymbol => NodeKind.Field,
            IEventSymbol => NodeKind.Event,
            _ => NodeKind.Type
        };
    }
}
