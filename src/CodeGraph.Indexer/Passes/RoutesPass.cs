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
        var externalNodes = new List<GraphNode>();
        var seenExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var seenEdges = new HashSet<(string FromId, string ToId, string HttpMethod, string Route)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var relativePath = GetRelativePath(tree.FilePath, solutionRoot);
            var walker = new RoutesWalker(semanticModel, relativePath, knownNodeIds, seenExternalIds, seenEdges, edges, externalNodes);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class RoutesWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _relativePath;
        private readonly HashSet<string> _knownNodeIds;
        private readonly HashSet<string> _seenExternalIds;
        private readonly HashSet<(string FromId, string ToId, string HttpMethod, string Route)> _seenEdges;
        private readonly List<GraphEdge> _edges;
        private readonly List<GraphNode> _externalNodes;

        public RoutesWalker(
            SemanticModel model,
            string relativePath,
            HashSet<string> knownNodeIds,
            HashSet<string> seenExternalIds,
            HashSet<(string FromId, string ToId, string HttpMethod, string Route)> seenEdges,
            List<GraphEdge> edges,
            List<GraphNode> externalNodes)
        {
            _model = model;
            _relativePath = relativePath;
            _knownNodeIds = knownNodeIds;
            _seenExternalIds = seenExternalIds;
            _seenEdges = seenEdges;
            _edges = edges;
            _externalNodes = externalNodes;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            TryEmitControllerRoutes(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            TryEmitMinimalApiRoute(node);
            base.VisitInvocationExpression(node);
        }

        private void TryEmitControllerRoutes(MethodDeclarationSyntax method)
        {
            var methodSymbol = _model.GetDeclaredSymbol(method);
            if (methodSymbol is null)
                return;

            var containingType = methodSymbol.ContainingType;
            if (containingType is null || !IsControllerClass(containingType))
                return;

            var classTemplates = GetRouteTemplates(containingType.GetAttributes(), containingType, methodSymbol);
            var actionRoutes = BuildControllerActionRoutes(methodSymbol, containingType, classTemplates);
            foreach (var route in actionRoutes)
            {
                EmitRouteEdge(route, methodSymbol);
            }
        }

        private IReadOnlyList<RouteDescriptor> BuildControllerActionRoutes(
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
                    continue;

                if (s_httpMethodAttributes.Contains(attributeName))
                {
                    httpMethods.Add(ExtractHttpMethod(attributeName));
                    var template = ExtractTemplate(attribute);
                    if (!string.IsNullOrWhiteSpace(template))
                    {
                        methodTemplates.Add(ReplacePlaceholders(template, containingType, methodSymbol));
                    }
                }
                else if (s_routeAttributes.Contains(attributeName))
                {
                    var template = ExtractTemplate(attribute);
                    methodTemplates.Add(ReplacePlaceholders(template, containingType, methodSymbol));
                }
            }

            if (httpMethods.Count == 0 && methodTemplates.Count == 0)
                return [];

            if (httpMethods.Count == 0)
                httpMethods.Add("ANY");

            if (methodTemplates.Count == 0)
                methodTemplates.Add(string.Empty);

            var effectiveClassTemplates = classTemplates.Count == 0 ? new[] { string.Empty } : classTemplates;
            var routes = new List<RouteDescriptor>();

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

            return routes
                .Distinct()
                .ToList();
        }

        private void TryEmitMinimalApiRoute(InvocationExpressionSyntax invocation)
        {
            var methodName = GetMethodName(invocation);
            if (methodName is null || !s_minimalApiMapMethods.TryGetValue(methodName, out var httpMethod))
                return;

            if (invocation.ArgumentList.Arguments.Count < 2)
                return;

            if (!IsEndpointRouteBuilderInvocation(invocation))
                return;

            var route = TryExtractLiteralRoute(invocation.ArgumentList.Arguments[0].Expression);
            if (route is null)
                return;

            var handler = ResolveHandlerMethod(invocation.ArgumentList.Arguments[1].Expression);
            if (handler is null)
                return;

            EmitRouteEdge(new RouteDescriptor(httpMethod, route), handler);
        }

        private bool IsEndpointRouteBuilderInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;

            try
            {
                var type = _model.GetTypeInfo(memberAccess.Expression).Type;
                if (type is null)
                    return false;

                if (IsEndpointRouteBuilderType(type))
                    return true;

                return type.AllInterfaces.Any(IsEndpointRouteBuilderType);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEndpointRouteBuilderType(ITypeSymbol type)
        {
            return (type.Name is "IEndpointRouteBuilder" or "WebApplication")
                && type.ContainingNamespace?.ToDisplayString() is "Microsoft.AspNetCore.Builder";
        }

        private IMethodSymbol? ResolveHandlerMethod(ExpressionSyntax expression)
        {
            try
            {
                var symbolInfo = _model.GetSymbolInfo(expression);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                    return methodSymbol;

                return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void EmitRouteEdge(RouteDescriptor route, IMethodSymbol handler)
        {
            var routeId = $"{route.HttpMethod} {route.Route}";
            var handlerId = SyntaxPass.GetSymbolId(handler);
            if (!_seenEdges.Add((routeId, handlerId, route.HttpMethod, route.Route)))
                return;

            _edges.Add(new GraphEdge
            {
                FromId = routeId,
                ToId = handlerId,
                Type = EdgeType.HandlesRoute,
                Confidence = EdgeConfidence.Verified,
                Metadata = new Dictionary<string, string>
                {
                    ["httpMethod"] = route.HttpMethod,
                    ["route"] = route.Route,
                    ["registrationFile"] = _relativePath
                }
            });

            EnsureExternalRouteNode(routeId, route.HttpMethod, route.Route);
            EnsureExternalHandlerNode(handler, handlerId);
        }

        private void EnsureExternalRouteNode(string id, string httpMethod, string route)
        {
            if (_knownNodeIds.Contains(id))
                return;
            if (!_seenExternalIds.Add(id))
                return;

            _externalNodes.Add(new GraphNode
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
            });
        }

        private void EnsureExternalHandlerNode(IMethodSymbol symbol, string id)
        {
            if (_knownNodeIds.Contains(id))
                return;
            if (!_seenExternalIds.Add(id))
                return;

            _externalNodes.Add(new GraphNode
            {
                Id = id,
                Name = symbol.Name,
                Kind = symbol.MethodKind == MethodKind.Constructor ? NodeKind.Constructor : NodeKind.Method,
                FilePath = string.Empty,
                Signature = symbol.ToDisplayString(),
                Accessibility = SyntaxPass.MapAccessibility(symbol.DeclaredAccessibility),
                ContainingTypeId = symbol.ContainingType is not null ? SyntaxPass.GetSymbolId(symbol.ContainingType) : null,
                ContainingNamespaceId = symbol.ContainingNamespace is { IsGlobalNamespace: false }
                    ? SyntaxPass.GetSymbolId(symbol.ContainingNamespace)
                    : null
            });
        }

        private static bool IsControllerClass(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name is "ApiController" or "ApiControllerAttribute")
                    return true;
            }

            var baseType = type.BaseType;
            while (baseType is not null)
            {
                if (s_controllerBaseNames.Contains(baseType.Name))
                    return true;
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
            foreach (var attribute in attributes)
            {
                var attributeName = attribute.AttributeClass?.Name;
                if (attributeName is null || !s_routeAttributes.Contains(attributeName))
                    continue;

                var template = ExtractTemplate(attribute);
                templates.Add(ReplacePlaceholders(template, containingType, methodSymbol));
            }

            return templates
                .Distinct(StringComparer.Ordinal)
                .ToList();
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
                    return namedTemplate;
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
                    controllerName = controllerName[..^"Controller".Length];

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
            return expression switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => NormalizeRoute(literal.Token.ValueText),
                _ => null
            };
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

        private static string CombineRoute(string classRoute, string methodRoute)
        {
            var segments = new[] { classRoute, methodRoute }
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => segment.Trim('/'));

            var combined = string.Join("/", segments);
            return NormalizeRoute(combined);
        }

        private static string NormalizeRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route))
                return "/";

            var normalized = route.Replace('\\', '/').Trim();
            if (!normalized.StartsWith('/'))
                normalized = "/" + normalized.TrimStart('/');

            while (normalized.Contains("//", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            }

            if (normalized.Length > 1)
                normalized = normalized.TrimEnd('/');

            return normalized;
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
