using System.Collections.Immutable;
using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class RoutesPass
{
    private static readonly HashSet<string> s_httpMethodAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HttpGet", "HttpGetAttribute",
        "HttpPost", "HttpPostAttribute",
        "HttpPut", "HttpPutAttribute",
        "HttpDelete", "HttpDeleteAttribute",
        "HttpPatch", "HttpPatchAttribute"
    };

    private static readonly HashSet<string> s_routeAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Route", "RouteAttribute"
    };

    private static readonly HashSet<string> s_controllerBaseNames = new(StringComparer.Ordinal)
    {
        "ControllerBase", "Controller"
    };

    private static readonly Dictionary<string, string> s_minimalApiMapMethods = new(StringComparer.Ordinal)
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    private readonly record struct RouteDescriptor(string HttpMethod, string Route);

    public (List<GraphEdge> Edges, List<GraphNode> ExternalNodes) Execute(
        CSharpCompilation compilation,
        string solutionRoot,
        HashSet<string> knownNodeIds)
    {
        var edges = new List<GraphEdge>();
        var externalNodes = new ExternalNodeCollector(knownNodeIds);
        var seenEdges = new HashSet<(string FromId, string ToId, string HttpMethod, string Route)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var relativePath = PassUtilities.GetRelativePath(tree.FilePath, solutionRoot);
            AnalyzeTree(tree.GetRoot(), semanticModel, relativePath, seenEdges, edges, externalNodes);
        }

        return (edges, externalNodes.ToList());
    }

    private static void AnalyzeTree(
        SyntaxNode root,
        SemanticModel model,
        string relativePath,
        HashSet<(string FromId, string ToId, string HttpMethod, string Route)> seenEdges,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            TryEmitControllerRoutes(method, model, relativePath, seenEdges, edges, externalNodes);
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            TryEmitMinimalApiRoute(invocation, model, relativePath, seenEdges, edges, externalNodes);
        }
    }

    private static void TryEmitControllerRoutes(
        MethodDeclarationSyntax method,
        SemanticModel model,
        string relativePath,
        HashSet<(string FromId, string ToId, string HttpMethod, string Route)> seenEdges,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var methodSymbol = model.GetDeclaredSymbol(method);
        var containingType = methodSymbol?.ContainingType;
        if (methodSymbol is null || containingType is null || !IsControllerClass(containingType))
        {
            return;
        }

        var classTemplates = GetRouteTemplates(containingType.GetAttributes(), containingType, methodSymbol);
        var actionRoutes = BuildControllerActionRoutes(methodSymbol, containingType, classTemplates);
        foreach (var route in actionRoutes)
        {
            EmitRouteEdge(route, methodSymbol, relativePath, seenEdges, edges, externalNodes);
        }
    }

    private static IReadOnlyList<RouteDescriptor> BuildControllerActionRoutes(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol containingType,
        IReadOnlyList<string> classTemplates)
    {
        var methodTemplates = new List<string>();
        var httpMethods = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in methodSymbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.Name;
            if (attributeName is null)
            {
                continue;
            }

            if (s_httpMethodAttributes.Contains(attributeName))
            {
                httpMethods.Add(ExtractHttpMethod(attributeName));
                var template = ExtractTemplate(attribute);
                if (!string.IsNullOrWhiteSpace(template))
                {
                    methodTemplates.Add(ReplacePlaceholders(template, containingType, methodSymbol));
                }

                continue;
            }

            if (s_routeAttributes.Contains(attributeName))
            {
                methodTemplates.Add(ReplacePlaceholders(ExtractTemplate(attribute), containingType, methodSymbol));
            }
        }

        if (httpMethods.Count == 0 && methodTemplates.Count == 0)
        {
            return [];
        }

        if (httpMethods.Count == 0)
        {
            httpMethods.Add("ANY");
        }

        if (methodTemplates.Count == 0)
        {
            methodTemplates.Add(string.Empty);
        }

        var effectiveClassTemplates = classTemplates.Count == 0 ? new[] { string.Empty } : classTemplates;
        var routes = new HashSet<RouteDescriptor>();
        foreach (var httpMethod in httpMethods)
        {
            foreach (var classTemplate in effectiveClassTemplates)
            {
                foreach (var methodTemplate in methodTemplates)
                {
                    routes.Add(new RouteDescriptor(httpMethod, CombineRoute(classTemplate, methodTemplate)));
                }
            }
        }

        return [.. routes];
    }

    private static void TryEmitMinimalApiRoute(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string relativePath,
        HashSet<(string FromId, string ToId, string HttpMethod, string Route)> seenEdges,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var methodName = PassUtilities.GetInvocationMethodName(invocation);
        if (methodName is null || !s_minimalApiMapMethods.TryGetValue(methodName, out var httpMethod))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count < 2 || !IsEndpointRouteBuilderInvocation(invocation, model))
        {
            return;
        }

        var route = TryExtractLiteralRoute(invocation.ArgumentList.Arguments[0].Expression);
        var handler = ResolveHandlerMethod(invocation.ArgumentList.Arguments[1].Expression, model);
        if (route is null || handler is null)
        {
            return;
        }

        EmitRouteEdge(new RouteDescriptor(httpMethod, route), handler, relativePath, seenEdges, edges, externalNodes);
    }

    private static bool IsEndpointRouteBuilderInvocation(InvocationExpressionSyntax invocation, SemanticModel model)
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

            return IsEndpointRouteBuilderType(type) || type.AllInterfaces.Any(IsEndpointRouteBuilderType);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEndpointRouteBuilderType(ITypeSymbol type)
    {
        return type.Name is "IEndpointRouteBuilder" or "WebApplication"
            && type.ContainingNamespace?.ToDisplayString() is "Microsoft.AspNetCore.Builder";
    }

    private static IMethodSymbol? ResolveHandlerMethod(ExpressionSyntax expression, SemanticModel model)
    {
        try
        {
            var symbolInfo = model.GetSymbolInfo(expression);
            return symbolInfo.Symbol as IMethodSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void EmitRouteEdge(
        RouteDescriptor route,
        IMethodSymbol handler,
        string relativePath,
        HashSet<(string FromId, string ToId, string HttpMethod, string Route)> seenEdges,
        List<GraphEdge> edges,
        ExternalNodeCollector externalNodes)
    {
        var routeId = $"{route.HttpMethod} {route.Route}";
        var handlerId = SyntaxPass.GetSymbolId(handler);
        if (!seenEdges.Add((routeId, handlerId, route.HttpMethod, route.Route)))
        {
            return;
        }

        edges.Add(new GraphEdge
        {
            FromId = routeId,
            ToId = handlerId,
            Type = EdgeType.HandlesRoute,
            Confidence = EdgeConfidence.Verified,
            Metadata = new Dictionary<string, string>
            {
                ["httpMethod"] = route.HttpMethod,
                ["route"] = route.Route,
                ["registrationFile"] = relativePath
            }
        });

        externalNodes.Add(routeId, () => CreateRouteNode(routeId, route.HttpMethod, route.Route));
        externalNodes.AddSymbol(handler, handlerId);
    }

    private static GraphNode CreateRouteNode(string id, string httpMethod, string route)
    {
        return new GraphNode
        {
            Id = id,
            Name = id,
            Kind = NodeKind.Property,
            FilePath = string.Empty,
            Signature = $"Route: {id}",
            Accessibility = Core.Models.Accessibility.Public,
            Metadata = new Dictionary<string, string>
            {
                ["nodeType"] = "Route",
                ["httpMethod"] = httpMethod,
                ["route"] = route
            }
        };
    }

    private static bool IsControllerClass(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "ApiController" or "ApiControllerAttribute")
            {
                return true;
            }
        }

        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (s_controllerBaseNames.Contains(baseType.Name))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static List<string> GetRouteTemplates(
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol containingType,
        IMethodSymbol? methodSymbol)
    {
        var templates = new List<string>();
        var seenTemplates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeClass?.Name;
            if (attributeName is null || !s_routeAttributes.Contains(attributeName))
            {
                continue;
            }

            var template = ReplacePlaceholders(ExtractTemplate(attribute), containingType, methodSymbol);
            if (seenTemplates.Add(template))
            {
                templates.Add(template);
            }
        }

        return templates;
    }

    private static string ExtractTemplate(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is string template)
        {
            return template;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "Template" && namedArgument.Value.Value is string namedTemplate)
            {
                return namedTemplate;
            }
        }

        return string.Empty;
    }

    private static string ExtractHttpMethod(string attributeName)
    {
        var name = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName[..^"Attribute".Length]
            : attributeName;

        return name.StartsWith("Http", StringComparison.Ordinal)
            ? name[4..].ToUpperInvariant()
            : name.ToUpperInvariant();
    }

    private static string ReplacePlaceholders(string template, INamedTypeSymbol containingType, IMethodSymbol? methodSymbol)
    {
        var resolved = template;
        if (resolved.Contains("[controller]", StringComparison.OrdinalIgnoreCase))
        {
            var controllerName = containingType.Name;
            if (controllerName.EndsWith("Controller", StringComparison.Ordinal))
            {
                controllerName = controllerName[..^"Controller".Length];
            }

            resolved = resolved.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
        }

        if (methodSymbol is not null && resolved.Contains("[action]", StringComparison.OrdinalIgnoreCase))
        {
            resolved = resolved.Replace("[action]", methodSymbol.Name, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }

    private static string? TryExtractLiteralRoute(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? NormalizeRoute(literal.Token.ValueText)
            : null;
    }

    private static string CombineRoute(string classRoute, string methodRoute)
    {
        var combined = string.Empty;

        if (!string.IsNullOrWhiteSpace(classRoute))
        {
            combined = classRoute.Trim('/');
        }

        if (!string.IsNullOrWhiteSpace(methodRoute))
        {
            var trimmedMethodRoute = methodRoute.Trim('/');
            combined = string.IsNullOrEmpty(combined)
                ? trimmedMethodRoute
                : $"{combined}/{trimmedMethodRoute}";
        }

        return NormalizeRoute(combined);
    }

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized.TrimStart('/');
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
