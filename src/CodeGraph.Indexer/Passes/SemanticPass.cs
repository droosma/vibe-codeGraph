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
        _ = solutionRoot;

        var externalNodes = new ExternalNodeCollector(knownNodeIds);
        var edges = new List<GraphEdge>();
        var seenEdges = new HashSet<(string FromId, string ToId, EdgeType Type)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new SemanticWalker(model, compilation, externalNodes, edges, seenEdges);
            try
            {
                walker.Visit(tree.GetRoot());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: SemanticPass error in {tree.FilePath}: {ex.Message}");
            }
        }

        return (externalNodes.ToList(), edges);
    }

    private sealed class SemanticWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly CSharpCompilation _compilation;
        private readonly ExternalNodeCollector _externalNodes;
        private readonly List<GraphEdge> _edges;
        private readonly HashSet<(string FromId, string ToId, EdgeType Type)> _seenEdges;

        public SemanticWalker(
            SemanticModel model,
            CSharpCompilation compilation,
            ExternalNodeCollector externalNodes,
            List<GraphEdge> edges,
            HashSet<(string FromId, string ToId, EdgeType Type)> seenEdges)
        {
            _model = model;
            _compilation = compilation;
            _externalNodes = externalNodes;
            _edges = edges;
            _seenEdges = seenEdges;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            TryRun(() => ProcessInvocation(node));
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            TryRun(() => ProcessObjectCreation(node));
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            TryRun(() => ProcessTypeDeclaration(node));
            base.VisitClassDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            TryRun(() => ProcessTypeDeclaration(node));
            base.VisitStructDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            TryRun(() => ProcessTypeDeclaration(node));
            base.VisitRecordDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            TryRun(() => ProcessMemberDependencies(node));
            TryRun(() => ProcessOverride(node));
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            TryRun(() => ProcessMemberDependencies(node));
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            TryRun(() => ProcessProperty(node));
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            TryRun(() => ProcessFields(node));
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            TryRun(() => ProcessMemberAccess(node));
            base.VisitMemberAccessExpression(node);
        }

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // Missing references can cause Roslyn internal errors.
            }
        }

        private void ProcessInvocation(InvocationExpressionSyntax node)
        {
            if (_model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            {
                return;
            }

            var callerId = PassUtilities.GetContainingMemberId(_model, node);
            if (callerId is null)
            {
                return;
            }

            var targetId = SyntaxPass.GetSymbolId(method);
            EnsureExternalNode(method, targetId);
            AddEdge(callerId, targetId, EdgeType.Calls, method);
        }

        private void ProcessObjectCreation(ObjectCreationExpressionSyntax node)
        {
            if (_model.GetSymbolInfo(node).Symbol is not IMethodSymbol constructor)
            {
                return;
            }

            var callerId = PassUtilities.GetContainingMemberId(_model, node);
            if (callerId is null)
            {
                return;
            }

            var targetId = SyntaxPass.GetSymbolId(constructor);
            EnsureExternalNode(constructor, targetId);
            AddEdge(callerId, targetId, EdgeType.Calls, constructor);
        }

        private void ProcessTypeDeclaration(TypeDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol)
            {
                return;
            }

            var typeId = SyntaxPass.GetSymbolId(symbol);
            if (symbol.BaseType is not null &&
                symbol.BaseType.SpecialType is not SpecialType.System_Object and not SpecialType.System_ValueType)
            {
                var baseId = SyntaxPass.GetSymbolId(symbol.BaseType);
                EnsureExternalNode(symbol.BaseType, baseId);
                AddEdge(typeId, baseId, EdgeType.Inherits, symbol.BaseType);
            }

            foreach (var interfaceType in symbol.Interfaces)
            {
                var interfaceId = SyntaxPass.GetSymbolId(interfaceType);
                EnsureExternalNode(interfaceType, interfaceId);
                AddEdge(typeId, interfaceId, EdgeType.Implements, interfaceType);
            }
        }

        private void ProcessMemberDependencies(SyntaxNode node)
        {
            if (_model.GetDeclaredSymbol(node) is not IMethodSymbol method)
            {
                return;
            }

            var memberId = SyntaxPass.GetSymbolId(method);
            if (method.MethodKind != MethodKind.Constructor)
            {
                EmitTypeDependency(memberId, method.ReturnType);
            }

            foreach (var parameter in method.Parameters)
            {
                EmitTypeDependency(memberId, parameter.Type);
            }
        }

        private void ProcessOverride(MethodDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not IMethodSymbol { IsOverride: true, OverriddenMethod: not null } symbol)
            {
                return;
            }

            var memberId = SyntaxPass.GetSymbolId(symbol);
            var baseId = SyntaxPass.GetSymbolId(symbol.OverriddenMethod);
            EnsureExternalNode(symbol.OverriddenMethod, baseId);
            AddEdge(memberId, baseId, EdgeType.Overrides, symbol.OverriddenMethod);
        }

        private void ProcessProperty(PropertyDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not IPropertySymbol symbol)
            {
                return;
            }

            EmitTypeDependency(SyntaxPass.GetSymbolId(symbol), symbol.Type);
        }

        private void ProcessFields(FieldDeclarationSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
            {
                if (_model.GetDeclaredSymbol(variable) is IFieldSymbol symbol)
                {
                    EmitTypeDependency(SyntaxPass.GetSymbolId(symbol), symbol.Type);
                }
            }
        }

        private void ProcessMemberAccess(MemberAccessExpressionSyntax node)
        {
            if (node.Parent is InvocationExpressionSyntax)
            {
                return;
            }

            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (symbol is null || symbol is IMethodSymbol)
            {
                return;
            }

            var referrerId = PassUtilities.GetContainingMemberId(_model, node);
            if (referrerId is null)
            {
                return;
            }

            var targetId = SyntaxPass.GetSymbolId(symbol);
            if (targetId == referrerId)
            {
                return;
            }

            EnsureExternalNode(symbol, targetId);
            AddEdge(referrerId, targetId, EdgeType.References, symbol);
        }

        private void EmitTypeDependency(string fromId, ITypeSymbol type)
        {
            var unwrappedType = UnwrapType(type);
            if (unwrappedType.SpecialType != SpecialType.None ||
                unwrappedType.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
            {
                return;
            }

            var targetId = SyntaxPass.GetSymbolId(unwrappedType);
            if (targetId == fromId)
            {
                return;
            }

            EnsureExternalNode(unwrappedType, targetId);
            AddEdge(fromId, targetId, EdgeType.DependsOn, unwrappedType);
        }

        private static ITypeSymbol UnwrapType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                return nullable.TypeArguments[0];
            }

            return type is IArrayTypeSymbol array
                ? UnwrapType(array.ElementType)
                : type;
        }

        private bool IsExternal(ISymbol symbol)
        {
            return symbol.ContainingAssembly is null
                || !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);
        }

        private void EnsureExternalNode(ISymbol symbol, string id)
        {
            if (_externalNodes.Contains(id) || !IsExternal(symbol))
            {
                return;
            }

            var metadata = new Dictionary<string, string>
            {
                ["assembly"] = symbol.ContainingAssembly?.Name ?? "Unknown"
            };

            _externalNodes.Add(id, () => PassUtilities.CreateExternalSymbolNode(symbol, id, metadata));
        }

        private void AddEdge(string fromId, string toId, EdgeType type, ISymbol targetSymbol)
        {
            if (!_seenEdges.Add((fromId, toId, type)))
            {
                return;
            }

            _edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Type = type,
                IsExternal = IsExternal(targetSymbol),
                Confidence = EdgeConfidence.Verified
            });
        }
    }
}
