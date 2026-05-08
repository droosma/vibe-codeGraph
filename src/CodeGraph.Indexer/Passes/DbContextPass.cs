using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class DbContextPass
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
            var walker = new DbContextWalker(semanticModel, relativePath, knownNodeIds, seenExternalIds, edges, externalNodes);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class DbContextWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _relativePath;
        private readonly HashSet<string> _knownNodeIds;
        private readonly HashSet<string> _seenExternalIds;
        private readonly List<GraphEdge> _edges;
        private readonly List<GraphNode> _externalNodes;

        public DbContextWalker(
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

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol is not null && IsDbContextSubclass(symbol))
            {
                ProcessDbContextClass(node, symbol);
            }

            if (symbol is not null && ImplementsEntityTypeConfiguration(symbol, out var configuredEntityType))
            {
                var entityId = SyntaxPass.GetSymbolId(configuredEntityType!);
                var configId = SyntaxPass.GetSymbolId(symbol);
                _edges.Add(new GraphEdge
                {
                    FromId = entityId,
                    ToId = configId,
                    Type = EdgeType.ConfiguredBy,
                    Confidence = EdgeConfidence.Verified,
                    Metadata = new Dictionary<string, string>
                    {
                        ["configurationClass"] = symbol.ToDisplayString()
                    }
                });
                EnsureExternalNode(configuredEntityType!, entityId);
                EnsureExternalNode(symbol, configId);
            }

            base.VisitClassDeclaration(node);
        }

        private void ProcessDbContextClass(ClassDeclarationSyntax classNode, INamedTypeSymbol contextSymbol)
        {
            // Discover DbSet<T> properties
            var entityTypes = new Dictionary<string, INamedTypeSymbol>();

            foreach (var member in classNode.Members)
            {
                if (member is PropertyDeclarationSyntax prop)
                {
                    TryExtractDbSetEntity(prop, entityTypes);
                }
            }

            // For entities without explicit ToTable, emit convention-based mapping (class name as table)
            var entitiesWithExplicitTable = new HashSet<string>();

            // Process OnModelCreating for fluent configurations
            foreach (var member in classNode.Members)
            {
                if (member is MethodDeclarationSyntax method &&
                    method.Identifier.Text == "OnModelCreating")
                {
                    ProcessOnModelCreating(method, entityTypes, entitiesWithExplicitTable);
                }
            }

            // Emit convention table mappings for entities without explicit ToTable
            foreach (var kvp in entityTypes)
            {
                if (!entitiesWithExplicitTable.Contains(kvp.Key))
                {
                    var entityId = SyntaxPass.GetSymbolId(kvp.Value);
                    var tableName = kvp.Value.Name;
                    var tableNodeId = $"[Table:{tableName}]";

                    _edges.Add(new GraphEdge
                    {
                        FromId = entityId,
                        ToId = tableNodeId,
                        Type = EdgeType.MapsToTable,
                        Confidence = EdgeConfidence.Inferred,
                        Metadata = new Dictionary<string, string>
                        {
                            ["tableName"] = tableName
                        }
                    });
                    EnsureExternalNode(kvp.Value, entityId);
                }
            }
        }

        private void TryExtractDbSetEntity(PropertyDeclarationSyntax prop, Dictionary<string, INamedTypeSymbol> entityTypes)
        {
            var propSymbol = _model.GetDeclaredSymbol(prop);
            if (propSymbol?.Type is not INamedTypeSymbol propType)
                return;

            if (!IsDbSetType(propType))
                return;

            if (propType.TypeArguments.Length == 1 &&
                propType.TypeArguments[0] is INamedTypeSymbol entityType)
            {
                var entityId = SyntaxPass.GetSymbolId(entityType);
                entityTypes.TryAdd(entityId, entityType);
            }
        }

        private void ProcessOnModelCreating(
            MethodDeclarationSyntax method,
            Dictionary<string, INamedTypeSymbol> entityTypes,
            HashSet<string> entitiesWithExplicitTable)
        {
            var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                TryProcessToTable(invocation, entityTypes, entitiesWithExplicitTable);
                TryProcessNavigation(invocation, entityTypes);
            }
        }

        private void TryProcessToTable(
            InvocationExpressionSyntax invocation,
            Dictionary<string, INamedTypeSymbol> entityTypes,
            HashSet<string> entitiesWithExplicitTable)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;

            if (memberAccess.Name.Identifier.Text != "ToTable")
                return;

            if (invocation.ArgumentList.Arguments.Count < 1)
                return;

            var tableNameArg = invocation.ArgumentList.Arguments[0].Expression;
            if (tableNameArg is not LiteralExpressionSyntax literal)
                return;

            var tableName = literal.Token.ValueText;

            // Resolve the entity type from the chain: modelBuilder.Entity<T>().ToTable("X")
            var entityType = ResolveEntityTypeFromChain(memberAccess.Expression);
            if (entityType is null)
                return;

            var entityId = SyntaxPass.GetSymbolId(entityType);
            var tableNodeId = $"[Table:{tableName}]";

            entitiesWithExplicitTable.Add(entityId);

            _edges.Add(new GraphEdge
            {
                FromId = entityId,
                ToId = tableNodeId,
                Type = EdgeType.MapsToTable,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["tableName"] = tableName
                }
            });

            EnsureExternalNode(entityType, entityId);
        }

        private void TryProcessNavigation(
            InvocationExpressionSyntax invocation,
            Dictionary<string, INamedTypeSymbol> entityTypes)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return;

            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName != "HasOne" && methodName != "HasMany")
                return;

            // Resolve the method symbol to get type arguments
            var methodSymbol = _model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol is null)
                return;

            // Get the source entity type from the chain
            var sourceEntity = ResolveEntityTypeFromChain(memberAccess.Expression);
            if (sourceEntity is null)
                return;

            // Get the target entity type from the method's type argument
            INamedTypeSymbol? targetEntity = null;
            if (methodSymbol.TypeArguments.Length >= 1 &&
                methodSymbol.TypeArguments[0] is INamedTypeSymbol targetType)
            {
                targetEntity = targetType;
            }

            if (targetEntity is null)
                return;

            // Determine relationship type and navigation property
            var relationship = methodName == "HasMany" ? "one-to-many" : "many-to-one";

            // Try to extract navigation property name from the lambda argument
            string? propertyName = null;
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                propertyName = ExtractPropertyNameFromLambda(invocation.ArgumentList.Arguments[0].Expression);
            }

            var fromId = SyntaxPass.GetSymbolId(sourceEntity);
            var toId = SyntaxPass.GetSymbolId(targetEntity);

            var metadata = new Dictionary<string, string>
            {
                ["relationship"] = relationship
            };

            if (propertyName is not null)
            {
                metadata["property"] = propertyName;
            }

            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = EdgeType.NavigatesTo,
                Confidence = EdgeConfidence.Verified,
                Metadata = metadata
            });

            EnsureExternalNode(sourceEntity, fromId);
            EnsureExternalNode(targetEntity, toId);
        }

        private INamedTypeSymbol? ResolveEntityTypeFromChain(ExpressionSyntax expression)
        {
            // Walk back to find modelBuilder.Entity<T>() in the call chain
            if (expression is InvocationExpressionSyntax chainInvocation)
            {
                if (chainInvocation.Expression is MemberAccessExpressionSyntax chainMember)
                {
                    if (chainMember.Name is GenericNameSyntax genericName &&
                        genericName.Identifier.Text == "Entity" &&
                        genericName.TypeArgumentList.Arguments.Count == 1)
                    {
                        var typeArg = genericName.TypeArgumentList.Arguments[0];
                        var typeInfo = _model.GetSymbolInfo(typeArg);
                        return typeInfo.Symbol as INamedTypeSymbol;
                    }

                    // Keep walking up the chain
                    return ResolveEntityTypeFromChain(chainInvocation);
                }

                // Check if the invocation itself has the method info
                var invocationSymbol = _model.GetSymbolInfo(chainInvocation).Symbol as IMethodSymbol;
                if (invocationSymbol is not null)
                {
                    // Check containing type for EntityTypeBuilder<T>
                    if (invocationSymbol.ContainingType is { IsGenericType: true } containingType &&
                        containingType.Name == "EntityTypeBuilder" &&
                        containingType.TypeArguments.Length == 1 &&
                        containingType.TypeArguments[0] is INamedTypeSymbol entityType)
                    {
                        return entityType;
                    }
                }
            }

            return null;
        }

        private static string? ExtractPropertyNameFromLambda(ExpressionSyntax expression)
        {
            if (expression is SimpleLambdaExpressionSyntax lambda)
            {
                if (lambda.Body is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Name.Identifier.Text;
                }
            }
            else if (expression is ParenthesizedLambdaExpressionSyntax parenLambda)
            {
                if (parenLambda.Body is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Name.Identifier.Text;
                }
            }

            return null;
        }

        private static bool IsDbContextSubclass(INamedTypeSymbol symbol)
        {
            var baseType = symbol.BaseType;
            while (baseType is not null)
            {
                if (baseType.Name == "DbContext" &&
                    baseType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        private static bool IsDbSetType(INamedTypeSymbol type)
        {
            return type is { IsGenericType: true, Name: "DbSet" } &&
                   type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
        }

        private static bool ImplementsEntityTypeConfiguration(INamedTypeSymbol symbol, out INamedTypeSymbol? entityType)
        {
            entityType = null;
            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface is { IsGenericType: true, Name: "IEntityTypeConfiguration" } &&
                    iface.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore" &&
                    iface.TypeArguments.Length == 1 &&
                    iface.TypeArguments[0] is INamedTypeSymbol et)
                {
                    entityType = et;
                    return true;
                }
            }
            return false;
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
