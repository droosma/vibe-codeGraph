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
        var seenNamespaces = new HashSet<string>();
        var assemblyName = compilation.AssemblyName ?? "Unknown";

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var walker = new SyntaxNodeWalker(semanticModel, solutionRoot, assemblyName, nodes, edges, seenNamespaces);
            walker.Visit(root);
        }

        return (nodes, edges);
    }

    private sealed class SyntaxNodeWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly string _solutionRoot;
        private readonly string _assemblyName;
        private readonly List<GraphNode> _nodes;
        private readonly List<GraphEdge> _edges;
        private readonly HashSet<string> _seenNamespaces;

        public SyntaxNodeWalker(
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
            EmitNamespaceNode(node);
            base.VisitNamespaceDeclaration(node);
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            EmitNamespaceNode(node);
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) { EmitTypeNode(node); base.VisitClassDeclaration(node); }
        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) { EmitTypeNode(node); base.VisitInterfaceDeclaration(node); }
        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) { EmitTypeNode(node); base.VisitRecordDeclaration(node); }
        public override void VisitStructDeclaration(StructDeclarationSyntax node) { EmitTypeNode(node); base.VisitStructDeclaration(node); }
        public override void VisitEnumDeclaration(EnumDeclarationSyntax node) { EmitTypeNode(node); base.VisitEnumDeclaration(node); }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            EmitMemberNode(node, NodeKind.Method);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            EmitMemberNode(node, NodeKind.Constructor);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            EmitMemberNode(node, NodeKind.Property);
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // FieldDeclaration can declare multiple variables
            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = _model.GetDeclaredSymbol(variable);
                if (symbol is null) continue;

                var graphNode = CreateNode(symbol, variable, NodeKind.Field);
                _nodes.Add(graphNode);

                EmitContainsEdgeForMember(symbol, graphNode.Id);
            }
            base.VisitFieldDeclaration(node);
        }

        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            EmitMemberNode(node, NodeKind.Event);
            base.VisitEventDeclaration(node);
        }

        private void EmitNamespaceNode(BaseNamespaceDeclarationSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol is null) return;

            var id = GetSymbolId(symbol);
            if (!_seenNamespaces.Add(id)) return;

            var lineSpan = node.GetLocation().GetLineSpan();
            var filePath = GetRelativePath(lineSpan.Path);

            _nodes.Add(new GraphNode
            {
                Id = id,
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

        private void EmitTypeNode(BaseTypeDeclarationSyntax node)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol is null) return;

            var graphNode = CreateNode(symbol, node, NodeKind.Type);
            _nodes.Add(graphNode);

            // Contains edge: namespace → type or type → nested type
            if (symbol.ContainingType is not null)
            {
                _edges.Add(new GraphEdge
                {
                    FromId = GetSymbolId(symbol.ContainingType),
                    ToId = graphNode.Id,
                    Type = EdgeType.Contains
                });
            }
            else if (symbol.ContainingNamespace is { IsGlobalNamespace: false })
            {
                var nsId = GetSymbolId(symbol.ContainingNamespace);
                // Ensure namespace node exists
                if (_seenNamespaces.Add(nsId))
                {
                    _nodes.Add(new GraphNode
                    {
                        Id = nsId,
                        Name = symbol.ContainingNamespace.Name,
                        Kind = NodeKind.Namespace,
                        FilePath = GetRelativePath(node.GetLocation().GetLineSpan().Path),
                        StartLine = 0,
                        EndLine = 0,
                        Signature = symbol.ContainingNamespace.ToDisplayString(),
                        Accessibility = Core.Models.Accessibility.Public,
                        AssemblyName = _assemblyName
                    });
                }
                _edges.Add(new GraphEdge
                {
                    FromId = nsId,
                    ToId = graphNode.Id,
                    Type = EdgeType.Contains
                });
            }
        }

        private void EmitMemberNode(SyntaxNode node, NodeKind kind)
        {
            var symbol = _model.GetDeclaredSymbol(node);
            if (symbol is null) return;

            var graphNode = CreateNode(symbol, node, kind);
            _nodes.Add(graphNode);

            EmitContainsEdgeForMember(symbol, graphNode.Id);
        }

        private void EmitContainsEdgeForMember(ISymbol symbol, string memberId)
        {
            if (symbol.ContainingType is not null)
            {
                _edges.Add(new GraphEdge
                {
                    FromId = GetSymbolId(symbol.ContainingType),
                    ToId = memberId,
                    Type = EdgeType.Contains
                });
            }
        }

        private GraphNode CreateNode(ISymbol symbol, SyntaxNode node, NodeKind kind)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var filePath = GetRelativePath(lineSpan.Path);
            var metadata = BuildMetadata(symbol, kind);

            return new GraphNode
            {
                Id = GetSymbolId(symbol),
                Name = symbol.Name,
                Kind = kind,
                FilePath = filePath,
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
                Metadata = metadata
            };
        }

        private static Dictionary<string, string> BuildMetadata(ISymbol symbol, NodeKind kind)
        {
            var meta = new Dictionary<string, string>();

            if (symbol.IsAbstract) meta["isAbstract"] = "true";
            if (symbol.IsStatic) meta["isStatic"] = "true";
            if (symbol.IsSealed) meta["isSealed"] = "true";
            if (symbol.IsVirtual) meta["isVirtual"] = "true";
            if (symbol.IsOverride) meta["isOverride"] = "true";

            switch (symbol)
            {
                case INamedTypeSymbol typeSymbol:
                    meta["typeKind"] = typeSymbol.TypeKind.ToString();
                    if (typeSymbol.IsGenericType)
                        meta["genericArity"] = typeSymbol.TypeParameters.Length.ToString();
                    if (typeSymbol.IsRecord)
                        meta["isRecord"] = "true";
                    break;

                case IMethodSymbol methodSymbol:
                    if (methodSymbol.IsAsync) meta["isAsync"] = "true";
                    if (methodSymbol.IsExtensionMethod) meta["isExtension"] = "true";
                    meta["returnType"] = methodSymbol.ReturnType.ToDisplayString();
                    meta["parameterCount"] = methodSymbol.Parameters.Length.ToString();
                    if (methodSymbol.IsGenericMethod)
                        meta["genericArity"] = methodSymbol.TypeParameters.Length.ToString();
                    break;

                case IPropertySymbol propSymbol:
                    meta["propertyType"] = propSymbol.Type.ToDisplayString();
                    if (propSymbol.IsIndexer) meta["isIndexer"] = "true";
                    break;

                case IFieldSymbol fieldSymbol:
                    meta["fieldType"] = fieldSymbol.Type.ToDisplayString();
                    if (fieldSymbol.IsConst) meta["isConst"] = "true";
                    if (fieldSymbol.IsReadOnly) meta["isReadOnly"] = "true";
                    break;
            }

            return meta;
        }

        private string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(_solutionRoot))
                return absolutePath ?? string.Empty;
            try
            {
                return Path.GetRelativePath(_solutionRoot, absolutePath);
            }
            catch
            {
                return absolutePath;
            }
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
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary is null) return null;

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
