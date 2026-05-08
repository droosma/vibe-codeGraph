using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class MiddlewarePass
{
    public (List<GraphEdge> Edges, List<GraphNode> ExternalNodes) Execute(
        CSharpCompilation compilation,
        string solutionRoot,
        HashSet<string> knownNodeIds)
    {
        var edges = new List<GraphEdge>();
        var externalNodes = new List<GraphNode>();
        var seenExternalIds = new HashSet<string>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var relativePath = GetRelativePath(tree.FilePath, solutionRoot);
            var walker = new MiddlewareWalker(semanticModel, relativePath, knownNodeIds, seenExternalIds, edges, externalNodes);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class MiddlewareWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _relativePath;
        private readonly HashSet<string> _knownNodeIds;
        private readonly HashSet<string> _seenExternalIds;
        private readonly List<GraphEdge> _edges;
        private readonly List<GraphNode> _externalNodes;

        // Track pipeline order per containing method
        private readonly Dictionary<string, int> _pipelineCounters = new();

        public MiddlewareWalker(
            SemanticModel model,
            string relativePath,
            HashSet<string> knownNodeIds,
            HashSet<string> seenExternalIds,
            List<GraphEdge> edges,
            List<GraphNode> externalNodes)
        {
            _model = model;
            _relativePath = relativePath;
            _knownNodeIds = knownNodeIds;
            _seenExternalIds = seenExternalIds;
            _edges = edges;
            _externalNodes = externalNodes;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            TryEmitMiddleware(node);
            base.VisitInvocationExpression(node);
        }

        private void TryEmitMiddleware(InvocationExpressionSyntax invocation)
        {
            var methodName = GetMethodName(invocation);
            if (methodName is null)
                return;

            if (!methodName.StartsWith("Use", StringComparison.Ordinal) &&
                !methodName.StartsWith("Map", StringComparison.Ordinal))
                return;

            if (!IsOnApplicationBuilder(invocation))
                return;

            var containingMethodId = GetContainingMethodId(invocation);
            if (containingMethodId is null)
                return;

            // Determine the target ID
            string toId;
            string middlewareName = methodName;

            if (methodName == "UseMiddleware" && TryResolveUseMiddlewareType(invocation, out var middlewareType))
            {
                toId = SyntaxPass.GetSymbolId(middlewareType!);
                middlewareName = middlewareType!.Name;
                EnsureExternalNode(middlewareType!, toId);
            }
            else
            {
                toId = $"[Middleware:{methodName}]";
            }

            // Track pipeline order per containing method
            if (!_pipelineCounters.TryGetValue(containingMethodId, out var order))
                order = 0;
            order++;
            _pipelineCounters[containingMethodId] = order;

            _edges.Add(new GraphEdge
            {
                FromId = containingMethodId,
                ToId = toId,
                Type = EdgeType.UsesMiddleware,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["pipelineOrder"] = order.ToString(),
                    ["middlewareName"] = middlewareName
                }
            });
        }

        private static string? GetMethodName(InvocationExpressionSyntax invocation)
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

        private bool IsOnApplicationBuilder(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;

            try
            {
                var typeInfo = _model.GetTypeInfo(memberAccess.Expression);
                var type = typeInfo.Type;
                if (type is null)
                    return false;

                // Check if the type itself or any of its interfaces is IApplicationBuilder
                if (IsApplicationBuilderType(type))
                    return true;

                foreach (var iface in type.AllInterfaces)
                {
                    if (IsApplicationBuilderType(iface))
                        return true;
                }
            }
            catch
            {
                // Roslyn can throw on incomplete PE references
            }

            return false;
        }

        private static bool IsApplicationBuilderType(ITypeSymbol type)
        {
            return type.Name is "IApplicationBuilder" or "WebApplication" or "IEndpointRouteBuilder"
                && type.ContainingNamespace?.ToDisplayString() is "Microsoft.AspNetCore.Builder";
        }

        private string? GetContainingMethodId(InvocationExpressionSyntax invocation)
        {
            var methodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl is null)
                return null;

            try
            {
                var symbol = _model.GetDeclaredSymbol(methodDecl);
                if (symbol is null)
                    return null;
                return SyntaxPass.GetSymbolId(symbol);
            }
            catch
            {
                return null;
            }
        }

        private bool TryResolveUseMiddlewareType(
            InvocationExpressionSyntax invocation,
            out INamedTypeSymbol? middlewareType)
        {
            middlewareType = null;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;
            if (memberAccess.Name is not GenericNameSyntax genericName)
                return false;
            if (genericName.TypeArgumentList.Arguments.Count != 1)
                return false;

            var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];

            try
            {
                var symbolInfo = _model.GetSymbolInfo(typeArgSyntax);
                middlewareType = symbolInfo.Symbol as INamedTypeSymbol;
            }
            catch
            {
                return false;
            }

            return middlewareType is not null;
        }

        private void EnsureExternalNode(INamedTypeSymbol symbol, string id)
        {
            if (_knownNodeIds.Contains(id))
                return;
            if (!_seenExternalIds.Add(id))
                return;

            _externalNodes.Add(new GraphNode
            {
                Id = id,
                Name = symbol.Name,
                Kind = NodeKind.Type,
                FilePath = string.Empty,
                Signature = symbol.ToDisplayString(),
                Accessibility = SyntaxPass.MapAccessibility(symbol.DeclaredAccessibility),
                ContainingNamespaceId = symbol.ContainingNamespace is { IsGlobalNamespace: false }
                    ? SyntaxPass.GetSymbolId(symbol.ContainingNamespace)
                    : null
            });
        }
    }

    private static string GetRelativePath(string absolutePath, string solutionRoot)
    {
        if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(solutionRoot))
            return absolutePath ?? string.Empty;
        try
        {
            return Path.GetRelativePath(solutionRoot, absolutePath);
        }
        catch
        {
            return absolutePath;
        }
    }
}
