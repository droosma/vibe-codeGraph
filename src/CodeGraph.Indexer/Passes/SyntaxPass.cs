using System.Xml.Linq;
using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace CodeGraph.Indexer.Passes;

public class SyntaxPass
{
    public (List<GraphNode> Nodes, List<GraphEdge> Edges) Execute(
        CSharpCompilation compilation,
        string solutionRoot)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var assemblyName = compilation.AssemblyName ?? "Unknown";

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var walker = new SyntaxWalker(semanticModel, solutionRoot, assemblyName, nodes, edges, seenNamespaces);
            walker.Visit(tree.GetRoot());
        }

        return (nodes, edges);
    }

    private sealed class SyntaxWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _solutionRoot;
        private readonly string _assemblyName;
        private readonly List<GraphNode> _nodes;
        private readonly List<GraphEdge> _edges;
        private readonly HashSet<string> _seenNamespaces;

        public SyntaxWalker(
            SemanticModel model,
            string solutionRoot,
            string assemblyName,
            List<GraphNode> nodes,
            List<GraphEdge> edges,
            HashSet<string> seenNamespaces)
        {
            _model = model;
            _solutionRoot = solutionRoot;
            _assemblyName = assemblyName;
            _nodes = nodes;
            _edges = edges;
            _seenNamespaces = seenNamespaces;
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            AddNamespaceNode(node);
            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            AddNamespaceNode(node);
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            AddTypeNode(node);
            base.VisitClassDeclaration(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            AddTypeNode(node);
            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            AddTypeNode(node);
            base.VisitRecordDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            AddTypeNode(node);
            base.VisitStructDeclaration(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            AddTypeNode(node);
            base.VisitEnumDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            AddMemberNode(node, NodeKind.Method);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            AddMemberNode(node, NodeKind.Constructor);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            AddMemberNode(node, NodeKind.Property);
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            AddFieldNodes(node);
            base.VisitFieldDeclaration(node);
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            AddMemberNode(node, NodeKind.Event);
            base.VisitEventDeclaration(node);
        }

        private void AddNamespaceNode(BaseNamespaceDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not INamespaceSymbol symbol)
            {
                return;
            }

            var namespaceId = GetSymbolId(symbol);
            if (!_seenNamespaces.Add(namespaceId))
            {
                return;
            }

            var lineSpan = node.GetLocation().GetLineSpan();
            var filePath = PassUtilities.GetRelativePath(lineSpan.Path, _solutionRoot);
            _nodes.Add(new GraphNode
            {
                Id = namespaceId,
                Name = symbol.Name,
                Kind = NodeKind.Namespace,
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                Signature = symbol.ToDisplayString(),
                DocComment = null,
                ContainingNamespaceId = symbol.ContainingNamespace?.IsGlobalNamespace == false
                    ? GetSymbolId(symbol.ContainingNamespace)
                    : null,
                Accessibility = Core.Models.Accessibility.Public,
                AssemblyName = _assemblyName
            });
        }

        private void AddTypeNode(BaseTypeDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol)
            {
                return;
            }

            var graphNode = CreateGraphNode(symbol, node, NodeKind.Type);
            _nodes.Add(graphNode);

            if (symbol.ContainingType is not null)
            {
                AddContainsEdge(GetSymbolId(symbol.ContainingType), graphNode.Id);
                return;
            }

            if (symbol.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace)
            {
                EnsureNamespaceNode(containingNamespace, node.GetLocation().GetLineSpan().Path);
                AddContainsEdge(GetSymbolId(containingNamespace), graphNode.Id);
            }
        }

        private void AddMemberNode(SyntaxNode node, NodeKind kind)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol is null)
            {
                return;
            }

            var graphNode = CreateGraphNode(symbol, node, kind);
            _nodes.Add(graphNode);
            AddContainingTypeEdge(symbol, graphNode.Id);
        }

        private void AddFieldNodes(FieldDeclarationSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = _model.GetDeclaredSymbol(variable);
                if (symbol is null)
                {
                    continue;
                }

                var graphNode = CreateGraphNode(symbol, variable, NodeKind.Field);
                _nodes.Add(graphNode);
                AddContainingTypeEdge(symbol, graphNode.Id);
            }
        }

        private void EnsureNamespaceNode(INamespaceSymbol containingNamespace, string sourcePath)
        {
            var namespaceId = GetSymbolId(containingNamespace);
            if (!_seenNamespaces.Add(namespaceId))
            {
                return;
            }

            _nodes.Add(new GraphNode
            {
                Id = namespaceId,
                Name = containingNamespace.Name,
                Kind = NodeKind.Namespace,
                FilePath = PassUtilities.GetRelativePath(sourcePath, _solutionRoot),
                StartLine = 0,
                EndLine = 0,
                Signature = containingNamespace.ToDisplayString(),
                Accessibility = Core.Models.Accessibility.Public,
                AssemblyName = _assemblyName
            });
        }

        private void AddContainingTypeEdge(ISymbol symbol, string memberId)
        {
            if (symbol.ContainingType is not null)
            {
                AddContainsEdge(GetSymbolId(symbol.ContainingType), memberId);
            }
        }

        private void AddContainsEdge(string fromId, string toId)
        {
            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = EdgeType.Contains,
                Confidence = EdgeConfidence.Verified
            });
        }

        private GraphNode CreateGraphNode(ISymbol symbol, SyntaxNode node, NodeKind kind)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            return new GraphNode
            {
                Id = GetSymbolId(symbol),
                Name = symbol.Name,
                Kind = kind,
                FilePath = PassUtilities.GetRelativePath(lineSpan.Path, _solutionRoot),
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                Signature = symbol.ToDisplayString(),
                DocComment = ExtractDocComment(symbol),
                ContainingTypeId = symbol.ContainingType is not null
                    ? GetSymbolId(symbol.ContainingType)
                    : null,
                ContainingNamespaceId = symbol.ContainingNamespace is { IsGlobalNamespace: false }
                    ? GetSymbolId(symbol.ContainingNamespace)
                    : null,
                Accessibility = MapAccessibility(symbol.DeclaredAccessibility),
                AssemblyName = _assemblyName,
                Metadata = BuildMetadata(symbol)
            };
        }

        private static Dictionary<string, string> BuildMetadata(ISymbol symbol)
        {
            var metadata = new Dictionary<string, string>();

            if (symbol.IsAbstract)
            {
                metadata["isAbstract"] = "true";
            }

            if (symbol.IsStatic)
            {
                metadata["isStatic"] = "true";
            }

            if (symbol.IsSealed)
            {
                metadata["isSealed"] = "true";
            }

            if (symbol.IsVirtual)
            {
                metadata["isVirtual"] = "true";
            }

            if (symbol.IsOverride)
            {
                metadata["isOverride"] = "true";
            }

            switch (symbol)
            {
                case INamedTypeSymbol typeSymbol:
                    metadata["typeKind"] = typeSymbol.TypeKind.ToString();
                    if (typeSymbol.IsGenericType)
                    {
                        metadata["genericArity"] = typeSymbol.TypeParameters.Length.ToString();
                    }

                    if (typeSymbol.IsRecord)
                    {
                        metadata["isRecord"] = "true";
                    }

                    break;

                case IMethodSymbol methodSymbol:
                    if (methodSymbol.IsAsync)
                    {
                        metadata["isAsync"] = "true";
                    }

                    if (methodSymbol.IsExtensionMethod)
                    {
                        metadata["isExtension"] = "true";
                    }

                    metadata["returnType"] = methodSymbol.ReturnType.ToDisplayString();
                    metadata["parameterCount"] = methodSymbol.Parameters.Length.ToString();
                    if (methodSymbol.IsGenericMethod)
                    {
                        metadata["genericArity"] = methodSymbol.TypeParameters.Length.ToString();
                    }

                    break;

                case IPropertySymbol propertySymbol:
                    metadata["propertyType"] = propertySymbol.Type.ToDisplayString();
                    if (propertySymbol.IsIndexer)
                    {
                        metadata["isIndexer"] = "true";
                    }

                    break;

                case IFieldSymbol fieldSymbol:
                    metadata["fieldType"] = fieldSymbol.Type.ToDisplayString();
                    if (fieldSymbol.IsConst)
                    {
                        metadata["isConst"] = "true";
                    }

                    if (fieldSymbol.IsReadOnly)
                    {
                        metadata["isReadOnly"] = "true";
                    }

                    break;
            }

            return metadata;
        }
    }

    private static readonly SymbolDisplayFormat s_idFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    internal static string GetSymbolId(ISymbol symbol)
    {
        return symbol.ToDisplayString(s_idFormat);
    }

    internal static string? ExtractDocComment(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary is null)
            {
                return null;
            }

            var text = summary.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    internal static Core.Models.Accessibility MapAccessibility(RoslynAccessibility accessibility)
    {
        return accessibility switch
        {
            RoslynAccessibility.Public => Core.Models.Accessibility.Public,
            RoslynAccessibility.Internal => Core.Models.Accessibility.Internal,
            RoslynAccessibility.Protected => Core.Models.Accessibility.Protected,
            RoslynAccessibility.Private => Core.Models.Accessibility.Private,
            RoslynAccessibility.ProtectedOrInternal => Core.Models.Accessibility.ProtectedInternal,
            RoslynAccessibility.ProtectedAndInternal => Core.Models.Accessibility.PrivateProtected,
            _ => Core.Models.Accessibility.Private
        };
    }
}
