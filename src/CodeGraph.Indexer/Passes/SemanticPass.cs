using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class SemanticPass
{
    public (List<GraphNode> ExternalNodes, List<GraphEdge> Edges) Execute(
        CSharpCompilation compilation,
        string solutionRoot,
        HashSet<string> knownNodeIds)
    {
        var externalNodes = new Dictionary<string, GraphNode>();
        var edges = new List<GraphEdge>();
        var seenEdges = new HashSet<(string, string, EdgeType)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var walker = new SemanticWalker(model, compilation, knownNodeIds, externalNodes, edges, seenEdges);
            try
            {
                walker.Visit(root);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: SemanticPass error in {tree.FilePath}: {ex.Message}");
            }
        }

        return (externalNodes.Values.ToList(), edges);
    }

    private sealed class SemanticWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly CSharpCompilation _compilation;
        private readonly HashSet<string> _knownIds;
        private readonly Dictionary<string, GraphNode> _externalNodes;
        private readonly List<GraphEdge> _edges;
        private readonly HashSet<(string, string, EdgeType)> _seenEdges;

        public SemanticWalker(
            SemanticModel model,
            CSharpCompilation compilation,
            HashSet<string> knownIds,
            Dictionary<string, GraphNode> externalNodes,
            List<GraphEdge> edges,
            HashSet<(string, string, EdgeType)> seenEdges)
        {
            _model = model;
            _compilation = compilation;
            _knownIds = knownIds;
            _externalNodes = externalNodes;
            _edges = edges;
            _seenEdges = seenEdges;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            try
            {
                var symbolInfo = _model.GetSymbolInfo(node);
                if (symbolInfo.Symbol is IMethodSymbol method)
                {
                    var callerId = GetContainingMemberId(node);
                    if (callerId is not null)
                    {
                        var targetId = SyntaxPass.GetSymbolId(method);
                        EnsureNode(method, targetId);
                        AddEdge(callerId, targetId, EdgeType.Calls, method);
                    }
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            try
            {
                var symbolInfo = _model.GetSymbolInfo(node);
                if (symbolInfo.Symbol is IMethodSymbol ctor)
                {
                    var callerId = GetContainingMemberId(node);
                    if (callerId is not null)
                    {
                        var targetId = SyntaxPass.GetSymbolId(ctor);
                        EnsureNode(ctor, targetId);
                        AddEdge(callerId, targetId, EdgeType.Calls, ctor);
                    }
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) { VisitTypeDeclaration(node); base.VisitClassDeclaration(node); }
        public override void VisitStructDeclaration(StructDeclarationSyntax node) { VisitTypeDeclaration(node); base.VisitStructDeclaration(node); }
        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) { VisitTypeDeclaration(node); base.VisitRecordDeclaration(node); }

        private void VisitTypeDeclaration(TypeDeclarationSyntax node)
        {
            try
            {
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol is null) return;

                var typeId = SyntaxPass.GetSymbolId(symbol);

                // Base type (inheritance)
                if (symbol.BaseType is not null &&
                    symbol.BaseType.SpecialType != SpecialType.System_Object &&
                    symbol.BaseType.SpecialType != SpecialType.System_ValueType)
                {
                    var baseId = SyntaxPass.GetSymbolId(symbol.BaseType);
                    EnsureNode(symbol.BaseType, baseId);
                    AddEdge(typeId, baseId, EdgeType.Inherits, symbol.BaseType);
                }

                // Interfaces
                foreach (var iface in symbol.Interfaces)
                {
                    var ifaceId = SyntaxPass.GetSymbolId(iface);
                    EnsureNode(iface, ifaceId);
                    AddEdge(typeId, ifaceId, EdgeType.Implements, iface);
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            VisitMemberForDependencies(node);
            VisitMethodForOverrides(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            VisitMemberForDependencies(node);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            try
            {
                var symbol = _model.GetDeclaredSymbol(node) as IPropertySymbol;
                if (symbol is null) return;

                var memberId = SyntaxPass.GetSymbolId(symbol);
                EmitTypeDependency(memberId, symbol.Type);
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            try
            {
                foreach (var variable in node.Declaration.Variables)
                {
                    var symbol = _model.GetDeclaredSymbol(variable) as IFieldSymbol;
                    if (symbol is null) continue;

                    var memberId = SyntaxPass.GetSymbolId(symbol);
                    EmitTypeDependency(memberId, symbol.Type);
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            try
            {
                // Skip if parent is an invocation (those are handled as Calls edges)
                if (node.Parent is InvocationExpressionSyntax)
                {
                    base.VisitMemberAccessExpression(node);
                    return;
                }

                var symbolInfo = _model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol;
                if (symbol is null || symbol is IMethodSymbol)
                {
                    base.VisitMemberAccessExpression(node);
                    return;
                }

                var referrerId = GetContainingMemberId(node);
                if (referrerId is not null)
                {
                    var targetId = SyntaxPass.GetSymbolId(symbol);
                    if (targetId != referrerId)
                    {
                        EnsureNode(symbol, targetId);
                        AddEdge(referrerId, targetId, EdgeType.References, symbol);
                    }
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
            base.VisitMemberAccessExpression(node);
        }

        private void VisitMemberForDependencies(SyntaxNode node)
        {
            try
            {
                var symbol = _model.GetDeclaredSymbol(node);
                if (symbol is not IMethodSymbol method) return;

                var memberId = SyntaxPass.GetSymbolId(method);

                if (method.MethodKind != MethodKind.Constructor)
                {
                    EmitTypeDependency(memberId, method.ReturnType);
                }

                foreach (var param in method.Parameters)
                {
                    EmitTypeDependency(memberId, param.Type);
                }
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
        }

        private void VisitMethodForOverrides(MethodDeclarationSyntax node)
        {
            try
            {
                var symbol = _model.GetDeclaredSymbol(node) as IMethodSymbol;
                if (symbol is null || !symbol.IsOverride || symbol.OverriddenMethod is null) return;

                var memberId = SyntaxPass.GetSymbolId(symbol);
                var baseId = SyntaxPass.GetSymbolId(symbol.OverriddenMethod);
                EnsureNode(symbol.OverriddenMethod, baseId);
                AddEdge(memberId, baseId, EdgeType.Overrides, symbol.OverriddenMethod);
            }
            catch { /* Missing references can cause Roslyn internal errors */ }
        }

        private void EmitTypeDependency(string fromId, ITypeSymbol type)
        {
            // Unwrap nullable, array, generic
            type = UnwrapType(type);

            if (type.SpecialType != SpecialType.None) return;
            if (type.TypeKind == TypeKind.TypeParameter) return;
            if (type.TypeKind == TypeKind.Error) return;

            var targetId = SyntaxPass.GetSymbolId(type);
            if (targetId == fromId) return;

            EnsureNode(type, targetId);
            AddEdge(fromId, targetId, EdgeType.DependsOn, type);
        }

        private static ITypeSymbol UnwrapType(ITypeSymbol type)
        {
            // Unwrap nullable value types
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                return nullable.TypeArguments[0];

            // Unwrap arrays
            if (type is IArrayTypeSymbol array)
                return UnwrapType(array.ElementType);

            return type;
        }

        private string? GetContainingMemberId(SyntaxNode node)
        {
            var current = node.Parent;
            while (current is not null)
            {
                if (current is MethodDeclarationSyntax or ConstructorDeclarationSyntax
                    or PropertyDeclarationSyntax or EventDeclarationSyntax)
                {
                    var symbol = _model.GetDeclaredSymbol(current);
                    return symbol is not null ? SyntaxPass.GetSymbolId(symbol) : null;
                }
                current = current.Parent;
            }
            return null;
        }

        private bool IsExternal(ISymbol symbol)
        {
            if (symbol.ContainingAssembly is null) return true;
            return !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);
        }

        private void EnsureNode(ISymbol symbol, string id)
        {
            if (_knownIds.Contains(id) || _externalNodes.ContainsKey(id)) return;
            if (!IsExternal(symbol)) return;

            var kind = symbol switch
            {
                IMethodSymbol => NodeKind.Method,
                IPropertySymbol => NodeKind.Property,
                IFieldSymbol => NodeKind.Field,
                IEventSymbol => NodeKind.Event,
                INamedTypeSymbol => NodeKind.Type,
                _ => NodeKind.Type
            };

            var assemblyName = symbol.ContainingAssembly?.Name ?? "Unknown";

            _externalNodes[id] = new GraphNode
            {
                Id = id,
                Name = symbol.Name,
                Kind = kind,
                FilePath = string.Empty,
                StartLine = 0,
                EndLine = 0,
                Signature = symbol.ToDisplayString(),
                Accessibility = SyntaxPass.MapAccessibility(symbol.DeclaredAccessibility),
                Metadata = new Dictionary<string, string> { ["assembly"] = assemblyName }
            };
        }

        private void AddEdge(string fromId, string toId, EdgeType type, ISymbol targetSymbol)
        {
            if (!_seenEdges.Add((fromId, toId, type))) return;

            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = type,
                IsExternal = IsExternal(targetSymbol)
            });
        }
    }
}
