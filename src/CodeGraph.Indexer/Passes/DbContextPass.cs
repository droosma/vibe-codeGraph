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
        _ = solutionRoot;

        var edges = new List<GraphEdge>();
        var externalNodes = new ExternalNodeCollector(knownNodeIds);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            AnalyzeTree(tree.GetRoot(), semanticModel, edges, externalNodes);
        }

        return (edges, externalNodes.ToList());
    }

    private static void AnalyzeTree(
        SyntaxNode root,
        SemanticModel model,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            AnalyzeClass(classDeclaration, model, edges, externalNodes);
        }
    }

    private static void AnalyzeClass(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel model,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var symbol = model.GetDeclaredSymbol(classDeclaration);
        if (symbol is null)
        {
            return;
        }

        if (IsDbContextSubclass(symbol))
        {
            ProcessDbContextClass(classDeclaration, model, edges, externalNodes);
        }

        if (ImplementsEntityTypeConfiguration(symbol, out var configuredEntityType))
        {
            EmitConfiguredByEdge(configuredEntityType, symbol, edges, externalNodes);
        }
    }

    private static void EmitConfiguredByEdge(
        INamedTypeSymbol entityType,
        INamedTypeSymbol configurationType,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var entityId = SyntaxPass.GetSymbolId(entityType);
        var configId = SyntaxPass.GetSymbolId(configurationType);

        edges.Add(new GraphEdge
        {
            FromId = entityId,
            ToId = configId,
            Type = EdgeType.ConfiguredBy,
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["configurationClass"] = configurationType.ToDisplayString()
            }
        });

        externalNodes.AddSymbol(entityType, entityId);
        externalNodes.AddSymbol(configurationType, configId);
    }

    private static void ProcessDbContextClass(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel model,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var entityTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            TryExtractDbSetEntity(property, model, entityTypes);
        }

        var entitiesWithExplicitTable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            if (method.Identifier.Text == "OnModelCreating")
            {
                ProcessOnModelCreating(method, model, entityTypes, entitiesWithExplicitTable, edges, externalNodes);
            }
        }

        foreach (var (entityId, entityType) in entityTypes)
        {
            if (!entitiesWithExplicitTable.Contains(entityId))
            {
                EmitConventionTableMapping(entityType, edges, externalNodes);
            }
        }
    }

    private static void TryExtractDbSetEntity(
        PropertyDeclarationSyntax property,
        SemanticModel model,
        Dictionary<string, INamedTypeSymbol> entityTypes)
    {
        if (model.GetDeclaredSymbol(property) is not IPropertySymbol { Type: INamedTypeSymbol propertyType })
        {
            return;
        }

        if (!IsDbSetType(propertyType) ||
            propertyType.TypeArguments.Length != 1 ||
            propertyType.TypeArguments[0] is not INamedTypeSymbol entityType)
        {
            return;
        }

        entityTypes.TryAdd(SyntaxPass.GetSymbolId(entityType), entityType);
    }

    private static void ProcessOnModelCreating(
        MethodDeclarationSyntax method,
        SemanticModel model,
        Dictionary<string, INamedTypeSymbol> entityTypes,
        HashSet<string> entitiesWithExplicitTable,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            TryProcessToTable(invocation, model, entityTypes, entitiesWithExplicitTable, edges, externalNodes);
            TryProcessNavigation(invocation, model, edges, externalNodes);
        }
    }

    private static void TryProcessToTable(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        Dictionary<string, INamedTypeSymbol> entityTypes,
        HashSet<string> entitiesWithExplicitTable,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text != "ToTable" ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal)
        {
            return;
        }

        var entityType = ResolveEntityTypeFromChain(memberAccess.Expression, model);
        if (entityType is null)
        {
            return;
        }

        var entityId = SyntaxPass.GetSymbolId(entityType);
        entitiesWithExplicitTable.Add(entityId);

        edges.Add(new GraphEdge
        {
            FromId = entityId,
            ToId = $"[Table:{literal.Token.ValueText}]",
            Type = EdgeType.MapsToTable,
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["tableName"] = literal.Token.ValueText
            }
        });

        externalNodes.AddSymbol(entityType, entityId);
        entityTypes.TryAdd(entityId, entityType);
    }

    private static void TryProcessNavigation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName is not "HasOne" and not "HasMany")
        {
            return;
        }

        var methodSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var sourceEntity = ResolveEntityTypeFromChain(memberAccess.Expression, model);
        var targetEntity = methodSymbol?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (sourceEntity is null || targetEntity is null)
        {
            return;
        }

        var metadata = new Dictionary<string, string>
        {
            ["relationship"] = methodName == "HasMany" ? "one-to-many" : "many-to-one"
        };

        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var propertyName = ExtractPropertyNameFromLambda(invocation.ArgumentList.Arguments[0].Expression);
            if (propertyName is not null)
            {
                metadata["property"] = propertyName;
            }
        }

        var sourceId = SyntaxPass.GetSymbolId(sourceEntity);
        var targetId = SyntaxPass.GetSymbolId(targetEntity);
        edges.Add(new GraphEdge
        {
            FromId = sourceId,
            ToId = targetId,
            Type = EdgeType.NavigatesTo,
            Confidence = EdgeConfidence.Verified,
            Metadata = metadata
        });

        externalNodes.AddSymbol(sourceEntity, sourceId);
        externalNodes.AddSymbol(targetEntity, targetId);
    }

    private static void EmitConventionTableMapping(
        INamedTypeSymbol entityType,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var entityId = SyntaxPass.GetSymbolId(entityType);
        edges.Add(new GraphEdge
        {
            FromId = entityId,
            ToId = $"[Table:{entityType.Name}]",
            Type = EdgeType.MapsToTable,
            Confidence = EdgeConfidence.Inferred,
            Metadata = new Dictionary<string, string>
            {
                ["tableName"] = entityType.Name
            }
        });

        externalNodes.AddSymbol(entityType, entityId);
    }

    private static INamedTypeSymbol? ResolveEntityTypeFromChain(ExpressionSyntax expression, SemanticModel model)
    {
        SyntaxNode? current = expression;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } memberAccess)
                {
                    if (genericName.Identifier.Text == "Entity" && genericName.TypeArgumentList.Arguments.Count == 1)
                    {
                        return PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[0]);
                    }

                    current = memberAccess.Expression;
                    continue;
                }

                var invocationSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (invocationSymbol?.ContainingType is { IsGenericType: true, Name: "EntityTypeBuilder" } containingType &&
                    containingType.TypeArguments.Length == 1 &&
                    containingType.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    return entityType;
                }
            }

            current = current is MemberAccessExpressionSyntax memberExpression
                ? memberExpression.Expression
                : null;
        }

        return null;
    }

    private static string? ExtractPropertyNameFromLambda(ExpressionSyntax expression)
    {
        return expression switch
        {
            SimpleLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax memberAccess } => memberAccess.Name.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax { Body: MemberAccessExpressionSyntax memberAccess } => memberAccess.Name.Identifier.Text,
            _ => null
        };
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
        return type is { IsGenericType: true, Name: "DbSet" }
            && type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool ImplementsEntityTypeConfiguration(INamedTypeSymbol symbol, out INamedTypeSymbol entityType)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface is { IsGenericType: true, Name: "IEntityTypeConfiguration" } &&
                iface.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore" &&
                iface.TypeArguments.Length == 1 &&
                iface.TypeArguments[0] is INamedTypeSymbol configuredEntityType)
            {
                entityType = configuredEntityType;
                return true;
            }
        }

        entityType = null!;
        return false;
    }
}
