using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class RoutesPass
{
    private static readonly HashSet<string> s_httpMethodAttributes = new(StringComparer.Ordinal)
    {
        "HttpGet", "HttpGetAttribute",
        "HttpPost", "HttpPostAttribute",
        "HttpPut", "HttpPutAttribute",
        "HttpDelete", "HttpDeleteAttribute",
        "HttpPatch", "HttpPatchAttribute"
    };

    private static readonly HashSet<string> s_routeAttributes = new(StringComparer.Ordinal)
    {
        "Route", "RouteAttribute"
    };

    private static readonly HashSet<string> s_controllerBaseNames = new(StringComparer.Ordinal)
    {
        "ControllerBase", "Controller"
    };

    public (List<GraphEdge> Edges, List<GraphNode> ExternalNodes) Execute(
        CSharpCompilation compilation,
        string solutionRoot,
        HashSet<string> knownNodeIds)
    {
        var edges = new List<GraphEdge>();
        var externalNodes = new List<GraphNode>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var walker = new RoutesWalker(semanticModel, edges);
            walker.Visit(tree.GetRoot());
        }

        return (edges, externalNodes);
    }

    private sealed class RoutesWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly List<GraphEdge> _edges;

        public RoutesWalker(SemanticModel model, List<GraphEdge> edges)
        {
            _model = model;
            _edges = edges;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            TryEmitRoute(node);
            base.VisitMethodDeclaration(node);
        }

        private void TryEmitRoute(MethodDeclarationSyntax method)
        {
            var methodSymbol = _model.GetDeclaredSymbol(method);
            if (methodSymbol is null)
                return;

            var containingType = methodSymbol.ContainingType;
            if (containingType is null || !IsControllerClass(containingType))
                return;

            foreach (var attr in methodSymbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName is null)
                    continue;

                if (!s_httpMethodAttributes.Contains(attrName))
                    continue;

                var httpMethod = ExtractHttpMethod(attrName);
                var methodTemplate = ExtractTemplate(attr);
                var classRoute = GetClassRoute(containingType);
                var fullRoute = CombineRoute(classRoute, methodTemplate);

                var fromId = SyntaxPass.GetSymbolId(methodSymbol);
                var toId = $"{httpMethod} {fullRoute}";

                _edges.Add(new GraphEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    Type = EdgeType.HandlesRoute,
                    Confidence = EdgeConfidence.Verified,
                    Metadata = new Dictionary<string, string>
                    {
                        ["httpMethod"] = httpMethod,
                        ["route"] = fullRoute
                    }
                });
            }
        }

        private static bool IsControllerClass(INamedTypeSymbol type)
        {
            // Check for [ApiController] attribute
            foreach (var attr in type.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name is "ApiController" or "ApiControllerAttribute")
                    return true;
            }

            // Check inheritance chain for ControllerBase / Controller
            var baseType = type.BaseType;
            while (baseType is not null)
            {
                if (s_controllerBaseNames.Contains(baseType.Name))
                    return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        private static string GetClassRoute(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name is null)
                    continue;

                if (s_routeAttributes.Contains(name) || s_httpMethodAttributes.Contains(name))
                {
                    var template = ExtractTemplate(attr);
                    if (!string.IsNullOrEmpty(template))
                        return ReplacePlaceholders(template, type);
                }
            }

            return string.Empty;
        }

        private static string ExtractTemplate(AttributeData attr)
        {
            // First positional constructor argument is the route template
            if (attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is string template)
            {
                return template;
            }

            return string.Empty;
        }

        private static string ExtractHttpMethod(string attrName)
        {
            // Strip "Attribute" suffix then "Http" prefix
            var name = attrName.EndsWith("Attribute", StringComparison.Ordinal)
                ? attrName[..^"Attribute".Length]
                : attrName;

            // name is e.g. "HttpGet" -> "GET"
            if (name.StartsWith("Http", StringComparison.Ordinal))
                return name[4..].ToUpperInvariant();

            return name.ToUpperInvariant();
        }

        private static string ReplacePlaceholders(string template, INamedTypeSymbol type)
        {
            if (template.Contains("[controller]", StringComparison.OrdinalIgnoreCase))
            {
                var controllerName = type.Name;
                if (controllerName.EndsWith("Controller", StringComparison.Ordinal))
                    controllerName = controllerName[..^"Controller".Length];

                template = template.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
            }

            return template;
        }

        private static string CombineRoute(string classRoute, string methodRoute)
        {
            if (string.IsNullOrEmpty(classRoute) && string.IsNullOrEmpty(methodRoute))
                return "/";

            if (string.IsNullOrEmpty(classRoute))
                return "/" + methodRoute.TrimStart('/');

            if (string.IsNullOrEmpty(methodRoute))
                return "/" + classRoute.TrimStart('/');

            return "/" + classRoute.TrimStart('/').TrimEnd('/') + "/" + methodRoute.TrimStart('/');
        }
    }
}
