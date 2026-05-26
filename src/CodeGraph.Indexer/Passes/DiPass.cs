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
        var externalNodes = new ExternalNodeCollector(knownNodeIds);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var relativePath = PassUtilities.GetRelativePath(tree.FilePath, solutionRoot);
            AnalyzeTree(tree.GetRoot(), semanticModel, relativePath, edges, externalNodes);
        }

        return (edges, externalNodes.ToList());
    }

    private static void AnalyzeTree(
        SyntaxNode root,
        SemanticModel model,
        string relativePath,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            TryEmitRegistration(invocation, model, relativePath, edges, externalNodes);
        }
    }

    private static void TryEmitRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string relativePath,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var methodName = PassUtilities.GetInvocationMethodName(invocation);
        if (methodName is null || !s_diMethods.Contains(methodName))
        {
            return;
        }

        var lifetime = ExtractLifetime(methodName);

        if (TryResolveGenericRegistration(invocation, model, out var abstraction, out var implementation))
        {
            EmitRegistrationEdge(abstraction, implementation, lifetime, relativePath, edges, externalNodes);
            return;
        }

        if (TryResolveTypeofRegistration(invocation, model, out abstraction, out implementation))
        {
            EmitRegistrationEdge(abstraction, implementation, lifetime, relativePath, edges, externalNodes);
        }
    }

    private static bool TryResolveGenericRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out INamedTypeSymbol abstraction,
        out INamedTypeSymbol implementation)
    {
        abstraction = null!;
        implementation = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName })
        {
            return false;
        }

        if (genericName.TypeArgumentList.Arguments.Count != 2)
        {
            return false;
        }

        abstraction = PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[0])!;
        implementation = PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[1])!;
        return abstraction is not null && implementation is not null;
    }

    private static bool TryResolveTypeofRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out INamedTypeSymbol abstraction,
        out INamedTypeSymbol implementation)
    {
        abstraction = null!;
        implementation = null!;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2 ||
            arguments[0].Expression is not TypeOfExpressionSyntax firstType ||
            arguments[1].Expression is not TypeOfExpressionSyntax secondType)
        {
            return false;
        }

        abstraction = PassUtilities.ResolveNamedType(model, firstType.Type)!;
        implementation = PassUtilities.ResolveNamedType(model, secondType.Type)!;
        return abstraction is not null && implementation is not null;
    }

    private static void EmitRegistrationEdge(
        INamedTypeSymbol abstraction,
        INamedTypeSymbol implementation,
        string lifetime,
        string relativePath,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var fromId = SyntaxPass.GetSymbolId(abstraction);
        var toId = SyntaxPass.GetSymbolId(implementation);

        edges.Add(new GraphEdge
        {
            FromId = fromId,
            ToId = toId,
            Type = EdgeType.ResolvesTo,
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["lifetime"] = lifetime,
                ["registrationFile"] = relativePath
            }
        });

        externalNodes.AddSymbol(abstraction, fromId);
        externalNodes.AddSymbol(implementation, toId);
    }

    private static string ExtractLifetime(string methodName)
    {
        if (methodName.Contains("Scoped", StringComparison.Ordinal))
        {
            return "Scoped";
        }

        if (methodName.Contains("Transient", StringComparison.Ordinal))
        {
            return "Transient";
        }

        return methodName.Contains("Singleton", StringComparison.Ordinal) ? "Singleton" : "Unknown";
    }
}
