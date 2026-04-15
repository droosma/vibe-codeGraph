using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class DiPass
{
    private static readonly HashSet<string> s_diMethods = new(StringComparer.Ordinal)
    {
        "AddScoped", "AddTransient", "AddSingleton",
        "TryAddScoped", "TryAddTransient", "TryAddSingleton"
    };

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
            var walker = new DiWalker(semanticModel, relativePath, knownNodeIds, seenExternalIds, edges, externalNodes);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class DiWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _relativePath;
        private readonly HashSet<string> _knownNodeIds;
        private readonly HashSet<string> _seenExternalIds;
        private readonly List<GraphEdge> _edges;
        private readonly List<GraphNode> _externalNodes;

        public DiWalker(
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
            TryEmitRegistration(node);
            base.VisitInvocationExpression(node);
        }

        private void TryEmitRegistration(InvocationExpressionSyntax invocation)
        {
            var methodName = GetMethodName(invocation);
            if (methodName is null || !s_diMethods.Contains(methodName))
                return;

            var lifetime = ExtractLifetime(methodName);

            // Try generic overload: services.AddScoped<IFoo, Foo>()
            if (TryResolveGenericTypeArgs(invocation, out var abstractionSymbol, out var implSymbol))
            {
                EmitEdge(abstractionSymbol!, implSymbol!, lifetime);
                return;
            }

            // Try typeof overload: services.AddScoped(typeof(IFoo), typeof(Foo))
            if (TryResolveTypeofArgs(invocation, out abstractionSymbol, out implSymbol))
            {
                EmitEdge(abstractionSymbol!, implSymbol!, lifetime);
            }
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

        private bool TryResolveGenericTypeArgs(
            InvocationExpressionSyntax invocation,
            out INamedTypeSymbol? abstraction,
            out INamedTypeSymbol? implementation)
        {
            abstraction = null;
            implementation = null;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;
            if (memberAccess.Name is not GenericNameSyntax genericName)
                return false;
            if (genericName.TypeArgumentList.Arguments.Count != 2)
                return false;

            var serviceTypeSyntax = genericName.TypeArgumentList.Arguments[0];
            var implTypeSyntax = genericName.TypeArgumentList.Arguments[1];

            try
            {
                var serviceTypeInfo = _model.GetSymbolInfo(serviceTypeSyntax);
                var implTypeInfo = _model.GetSymbolInfo(implTypeSyntax);

                abstraction = serviceTypeInfo.Symbol as INamedTypeSymbol;
                implementation = implTypeInfo.Symbol as INamedTypeSymbol;
            }
            catch
            {
                // Roslyn can throw on incomplete PE references
                return false;
            }

            return abstraction is not null && implementation is not null;
        }

        private bool TryResolveTypeofArgs(
            InvocationExpressionSyntax invocation,
            out INamedTypeSymbol? abstraction,
            out INamedTypeSymbol? implementation)
        {
            abstraction = null;
            implementation = null;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 2)
                return false;

            var firstTypeof = args[0].Expression as TypeOfExpressionSyntax;
            var secondTypeof = args[1].Expression as TypeOfExpressionSyntax;
            if (firstTypeof is null || secondTypeof is null)
                return false;

            try
            {
                var firstInfo = _model.GetSymbolInfo(firstTypeof.Type);
                var secondInfo = _model.GetSymbolInfo(secondTypeof.Type);

                abstraction = firstInfo.Symbol as INamedTypeSymbol;
                implementation = secondInfo.Symbol as INamedTypeSymbol;
            }
            catch
            {
                // Roslyn can throw on incomplete PE references
                return false;
            }

            return abstraction is not null && implementation is not null;
        }

        private void EmitEdge(INamedTypeSymbol abstraction, INamedTypeSymbol implementation, string lifetime)
        {
            var fromId = SyntaxPass.GetSymbolId(abstraction);
            var toId = SyntaxPass.GetSymbolId(implementation);

            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = EdgeType.ResolvesTo,
                Metadata = new Dictionary<string, string>
                {
                    ["lifetime"] = lifetime,
                    ["registrationFile"] = _relativePath
                }
            });

            EnsureExternalNode(abstraction, fromId);
            EnsureExternalNode(implementation, toId);
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

        private static string ExtractLifetime(string methodName)
        {
            if (methodName.Contains("Scoped")) return "Scoped";
            if (methodName.Contains("Transient")) return "Transient";
            if (methodName.Contains("Singleton")) return "Singleton";
            return "Unknown";
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
