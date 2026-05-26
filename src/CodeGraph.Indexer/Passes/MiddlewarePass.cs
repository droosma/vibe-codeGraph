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
        _ = solutionRoot;

        var edges = new List<GraphEdge>();
        var externalNodes = new ExternalNodeCollector(knownNodeIds);
        var pipelineCounters = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            AnalyzeTree(tree.GetRoot(), semanticModel, pipelineCounters, edges, externalNodes);
        }

        return (edges, externalNodes.ToList());
    }

    private static void AnalyzeTree(
        SyntaxNode root,
        SemanticModel model,
        Dictionary<string, int> pipelineCounters,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            TryEmitMiddleware(invocation, model, pipelineCounters, edges, externalNodes);
        }
    }

    private static void TryEmitMiddleware(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        Dictionary<string, int> pipelineCounters,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var methodName = PassUtilities.GetInvocationMethodName(invocation);
        if (methodName is null ||
            (!methodName.StartsWith("Use", StringComparison.Ordinal) &&
             !methodName.StartsWith("Map", StringComparison.Ordinal)))
        {
            return;
        }

        if (!IsApplicationBuilderInvocation(invocation, model))
        {
            return;
        }

        var containingMethodId = PassUtilities.GetContainingMethodId(model, invocation);
        if (containingMethodId is null)
        {
            return;
        }

        var middlewareName = methodName;
        var targetId = $"[Middleware:{methodName}]";

        if (methodName == "UseMiddleware" && TryResolveMiddlewareType(invocation, model, out var middlewareType))
        {
            targetId = SyntaxPass.GetSymbolId(middlewareType);
            middlewareName = middlewareType.Name;
            externalNodes.AddSymbol(middlewareType, targetId);
        }

        var order = GetNextPipelineOrder(containingMethodId, pipelineCounters);
        edges.Add(new GraphEdge
        {
            FromId = containingMethodId,
            ToId = targetId,
            Type = EdgeType.UsesMiddleware,
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["pipelineOrder"] = order.ToString(),
                ["middlewareName"] = middlewareName
            }
        });
    }

    private static int GetNextPipelineOrder(string containingMethodId, Dictionary<string, int> pipelineCounters)
    {
        pipelineCounters.TryGetValue(containingMethodId, out var currentOrder);
        currentOrder++;
        pipelineCounters[containingMethodId] = currentOrder;
        return currentOrder;
    }

    private static bool IsApplicationBuilderInvocation(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        try
        {
            var type = model.GetTypeInfo(memberAccess.Expression).Type;
            if (type is null)
            {
                return false;
            }

            return IsApplicationBuilderType(type) || type.AllInterfaces.Any(IsApplicationBuilderType);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsApplicationBuilderType(ITypeSymbol type)
    {
        return type.Name is "IApplicationBuilder" or "WebApplication" or "IEndpointRouteBuilder"
            && type.ContainingNamespace?.ToDisplayString() is "Microsoft.AspNetCore.Builder";
    }

    private static bool TryResolveMiddlewareType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out INamedTypeSymbol middlewareType)
    {
        middlewareType = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } ||
            genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        middlewareType = PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[0])!;
        return middlewareType is not null;
    }
}
