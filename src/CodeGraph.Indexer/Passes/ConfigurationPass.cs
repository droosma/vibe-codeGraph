using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class ConfigurationPass
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
            var walker = new ConfigurationWalker(semanticModel, relativePath, knownNodeIds, seenExternalIds, edges, externalNodes);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class ConfigurationWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _relativePath;
        private readonly HashSet<string> _knownNodeIds;
        private readonly HashSet<string> _seenExternalIds;
        private readonly List<GraphEdge> _edges;
        private readonly List<GraphNode> _externalNodes;

        public ConfigurationWalker(
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
            TryEmitConfigureBinding(node);
            TryEmitAddOptionsBinding(node);
            base.VisitInvocationExpression(node);
        }

        /// <summary>
        /// Detects: services.Configure&lt;T&gt;(config.GetSection("..."))
        /// </summary>
        private void TryEmitConfigureBinding(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;
            if (memberAccess.Name is not GenericNameSyntax genericName)
                return;
            if (genericName.Identifier.Text != "Configure")
                return;
            if (genericName.TypeArgumentList.Arguments.Count != 1)
                return;

            var optionsType = ResolveTypeArgument(genericName.TypeArgumentList.Arguments[0]);
            if (optionsType is null)
                return;

            var sectionPath = TryExtractSectionPath(invocation.ArgumentList);
            if (sectionPath is null)
                return;

            var hasValidation = false;
            EmitEdge(optionsType, sectionPath, "Configure", hasValidation);
        }

        /// <summary>
        /// Detects: services.AddOptions&lt;T&gt;().Bind(config.GetSection("..."))
        /// Optionally followed by .ValidateDataAnnotations()
        /// </summary>
        private void TryEmitAddOptionsBinding(InvocationExpressionSyntax invocation)
        {
            // Walk up the fluent chain to find .Bind(...) and optionally .ValidateDataAnnotations()
            // Pattern: services.AddOptions<T>().Bind(section).ValidateDataAnnotations()
            // We look for .Bind(...) calls where the receiver chain includes AddOptions<T>()

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;

            var methodName = memberAccess.Name switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax g => g.Identifier.Text,
                _ => null
            };

            // Check if this is a .Bind(...) call
            if (methodName == "Bind")
            {
                // If ValidateDataAnnotations() wraps this call, skip here; the outer visit handles it
                if (CheckParentForValidation(invocation))
                    return;

                var sectionPath = TryExtractSectionPath(invocation.ArgumentList);
                if (sectionPath is null)
                    return;

                var optionsType = FindAddOptionsTypeInChain(memberAccess.Expression);
                if (optionsType is null)
                    return;

                EmitEdge(optionsType, sectionPath, "AddOptions", hasValidation: false);
                return;
            }

            // Check if this is .ValidateDataAnnotations() with .Bind(...) deeper in the chain
            if (methodName == "ValidateDataAnnotations")
            {
                // The receiver should be a .Bind(...) call
                if (memberAccess.Expression is InvocationExpressionSyntax bindInvocation)
                {
                    if (bindInvocation.Expression is MemberAccessExpressionSyntax bindAccess &&
                        bindAccess.Name is IdentifierNameSyntax bindId &&
                        bindId.Identifier.Text == "Bind")
                    {
                        var sectionPath = TryExtractSectionPath(bindInvocation.ArgumentList);
                        if (sectionPath is null)
                            return;

                        var optionsType = FindAddOptionsTypeInChain(bindAccess.Expression);
                        if (optionsType is null)
                            return;

                        EmitEdge(optionsType, sectionPath, "AddOptions", hasValidation: true);
                    }
                }
            }
        }

        private bool CheckParentForValidation(InvocationExpressionSyntax bindInvocation)
        {
            // Check if the parent is .ValidateDataAnnotations()
            if (bindInvocation.Parent is MemberAccessExpressionSyntax parentAccess &&
                parentAccess.Name is IdentifierNameSyntax parentId &&
                parentId.Identifier.Text == "ValidateDataAnnotations" &&
                parentAccess.Parent is InvocationExpressionSyntax)
            {
                return true;
            }
            return false;
        }

        private INamedTypeSymbol? FindAddOptionsTypeInChain(ExpressionSyntax expression)
        {
            // Walk down the chain to find AddOptions<T>()
            if (expression is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax access &&
                    access.Name is GenericNameSyntax generic &&
                    generic.Identifier.Text == "AddOptions" &&
                    generic.TypeArgumentList.Arguments.Count == 1)
                {
                    return ResolveTypeArgument(generic.TypeArgumentList.Arguments[0]);
                }

                // Recurse into the receiver (for longer chains)
                if (invocation.Expression is MemberAccessExpressionSyntax chainAccess)
                {
                    return FindAddOptionsTypeInChain(chainAccess.Expression);
                }
            }

            return null;
        }

        private INamedTypeSymbol? ResolveTypeArgument(TypeSyntax typeSyntax)
        {
            try
            {
                var symbolInfo = _model.GetSymbolInfo(typeSyntax);
                return symbolInfo.Symbol as INamedTypeSymbol;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts the section path string from the first argument.
        /// Supports: config.GetSection("Payment:Gateway") as the argument.
        /// </summary>
        private string? TryExtractSectionPath(ArgumentListSyntax argumentList)
        {
            if (argumentList.Arguments.Count < 1)
                return null;

            var arg = argumentList.Arguments[0].Expression;

            // Direct GetSection("...") call
            if (arg is InvocationExpressionSyntax getSectionCall)
            {
                return ExtractGetSectionPath(getSectionCall);
            }

            return null;
        }

        private string? ExtractGetSectionPath(InvocationExpressionSyntax invocation)
        {
            // Must be a .GetSection(...) call
            var methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                },
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            };

            if (methodName != "GetSection")
                return null;

            if (invocation.ArgumentList.Arguments.Count != 1)
                return null;

            var sectionArg = invocation.ArgumentList.Arguments[0].Expression;

            // Only extract literal string arguments
            if (sectionArg is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            return null;
        }

        private void EmitEdge(INamedTypeSymbol optionsType, string sectionPath, string registrationMethod, bool hasValidation)
        {
            var fromId = SyntaxPass.GetSymbolId(optionsType);
            var toId = $"[Config:{sectionPath}]";

            var metadata = new Dictionary<string, string>
            {
                ["section"] = sectionPath,
                ["registrationMethod"] = registrationMethod,
                ["registrationFile"] = _relativePath
            };

            if (hasValidation)
            {
                metadata["validation"] = "DataAnnotations";
            }

            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = EdgeType.BindsConfiguration,
                Confidence = EdgeConfidence.Verified,
                Metadata = metadata
            });

            EnsureExternalNode(optionsType, fromId);
            EnsureExternalConfigNode(toId, sectionPath);
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

        private void EnsureExternalConfigNode(string id, string sectionPath)
        {
            if (_knownNodeIds.Contains(id))
                return;
            if (!_seenExternalIds.Add(id))
                return;

            _externalNodes.Add(new GraphNode
            {
                Id = id,
                Name = sectionPath,
                Kind = NodeKind.Property,
                FilePath = string.Empty,
                Signature = $"Configuration Section: {sectionPath}",
                Accessibility = Core.Models.Accessibility.Public,
                Metadata = new Dictionary<string, string>
                {
                    ["nodeType"] = "ConfigurationSection"
                }
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
