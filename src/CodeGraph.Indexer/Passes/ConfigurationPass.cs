using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class ConfigurationPass
{
    private readonly record struct BindingDescriptor(
        INamedTypeSymbol OptionsType,
        string SectionPath,
        string RegistrationMethod,
        bool HasValidation);

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
            if (TryCreateBinding(invocation, model, out var binding))
            {
                EmitBindingEdge(binding, relativePath, edges, externalNodes);
            }
        }
    }

    private static bool TryCreateBinding(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out BindingDescriptor binding)
    {
        if (TryCreateConfigureBinding(invocation, model, out binding))
        {
            return true;
        }

        return TryCreateAddOptionsBinding(invocation, model, out binding);
    }

    private static bool TryCreateConfigureBinding(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out BindingDescriptor binding)
    {
        binding = default;

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName })
        {
            return false;
        }

        if (genericName.Identifier.Text != "Configure" || genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var optionsType = PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[0]);
        var sectionPath = TryExtractSectionPath(invocation.ArgumentList);
        if (optionsType is null || sectionPath is null)
        {
            return false;
        }

        binding = new BindingDescriptor(optionsType, sectionPath, "Configure", HasValidation: false);
        return true;
    }

    private static bool TryCreateAddOptionsBinding(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out BindingDescriptor binding)
    {
        binding = default;

        var methodName = PassUtilities.GetInvocationMethodName(invocation);
        if (methodName == "Bind")
        {
            if (IsValidatedBind(invocation))
            {
                return false;
            }

            return TryCreateBoundOptionsBinding(invocation, model, hasValidation: false, out binding);
        }

        if (methodName != "ValidateDataAnnotations" ||
            invocation.Expression is not MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax bindInvocation })
        {
            return false;
        }

        return TryCreateBoundOptionsBinding(bindInvocation, model, hasValidation: true, out binding);
    }

    private static bool TryCreateBoundOptionsBinding(
        InvocationExpressionSyntax bindInvocation,
        SemanticModel model,
        bool hasValidation,
        out BindingDescriptor binding)
    {
        binding = default;

        if (PassUtilities.GetInvocationMethodName(bindInvocation) != "Bind" ||
            bindInvocation.Expression is not MemberAccessExpressionSyntax bindAccess)
        {
            return false;
        }

        var sectionPath = TryExtractSectionPath(bindInvocation.ArgumentList);
        var optionsType = FindAddOptionsType(bindAccess.Expression, model);
        if (sectionPath is null || optionsType is null)
        {
            return false;
        }

        binding = new BindingDescriptor(optionsType, sectionPath, "AddOptions", hasValidation);
        return true;
    }

    private static bool IsValidatedBind(InvocationExpressionSyntax bindInvocation)
    {
        return bindInvocation.Parent is MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax { Identifier.Text: "ValidateDataAnnotations" },
                Parent: InvocationExpressionSyntax
            };
    }

    private static INamedTypeSymbol? FindAddOptionsType(ExpressionSyntax expression, SemanticModel model)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access)
            {
                return null;
            }

            if (access.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "AddOptions" &&
                genericName.TypeArgumentList.Arguments.Count == 1)
            {
                return PassUtilities.ResolveNamedType(model, genericName.TypeArgumentList.Arguments[0]);
            }

            current = access.Expression;
        }

        return null;
    }

    // Only literal GetSection("...") calls are stable enough to model as graph nodes.
    private static string? TryExtractSectionPath(ArgumentListSyntax argumentList)
    {
        if (argumentList.Arguments.Count == 0 ||
            argumentList.Arguments[0].Expression is not InvocationExpressionSyntax getSectionCall)
        {
            return null;
        }

        return ExtractGetSectionPath(getSectionCall);
    }

    private static string? ExtractGetSectionPath(InvocationExpressionSyntax invocation)
    {
        if (PassUtilities.GetInvocationMethodName(invocation) != "GetSection" ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return null;
        }

        return literal.Token.ValueText;
    }

    private static void EmitBindingEdge(
        BindingDescriptor binding,
        string relativePath,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var fromId = SyntaxPass.GetSymbolId(binding.OptionsType);
        var toId = $"[Config:{binding.SectionPath}]";
        var metadata = new Dictionary<string, string>
        {
            ["section"] = binding.SectionPath,
            ["registrationMethod"] = binding.RegistrationMethod,
            ["registrationFile"] = relativePath
        };

        if (binding.HasValidation)
        {
            metadata["validation"] = "DataAnnotations";
        }

        edges.Add(new GraphEdge
        {
            FromId = fromId,
            ToId = toId,
            Type = EdgeType.BindsConfiguration,
            Confidence = EdgeConfidence.Verified,
            Metadata = metadata
        });

        externalNodes.AddSymbol(binding.OptionsType, fromId);
        externalNodes.Add(toId, () => CreateConfigurationNode(toId, binding.SectionPath));
    }

    private static GraphNode CreateConfigurationNode(string id, string sectionPath)
    {
        return new GraphNode
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
        };
    }
}
