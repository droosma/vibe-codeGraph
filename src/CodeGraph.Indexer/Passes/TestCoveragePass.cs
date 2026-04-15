using CodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Passes;

public class TestCoveragePass
{
    private static readonly HashSet<string> XUnitAttributes = new() { "Fact", "FactAttribute", "Theory", "TheoryAttribute" };
    private static readonly HashSet<string> NUnitAttributes = new() { "Test", "TestAttribute", "TestCase", "TestCaseAttribute" };
    private static readonly HashSet<string> MSTestAttributes = new() { "TestMethod", "TestMethodAttribute" };

    public (List<GraphEdge> Edges, List<GraphNode> ExternalNodes) Execute(
        CSharpCompilation compilation,
        string solutionRoot,
        HashSet<string> knownNodeIds)
    {
        var edges = new List<GraphEdge>();
        var externalNodes = new Dictionary<string, GraphNode>();
        var seenEdges = new HashSet<(string, string, EdgeType)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            SemanticModel model;
            try
            {
                model = compilation.GetSemanticModel(tree);
            }
            catch
            {
                continue;
            }

            var root = tree.GetRoot();
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                IMethodSymbol? methodSymbol;
                try
                {
                    methodSymbol = model.GetDeclaredSymbol(method) as IMethodSymbol;
                }
                catch
                {
                    continue;
                }

                if (methodSymbol is null) continue;

                var framework = DetectTestFramework(method);
                if (framework is null) continue;

                var testMethodId = SyntaxPass.GetSymbolId(methodSymbol);
                var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    IMethodSymbol? targetSymbol;
                    try
                    {
                        var symbolInfo = model.GetSymbolInfo(invocation);
                        targetSymbol = symbolInfo.Symbol as IMethodSymbol;
                    }
                    catch
                    {
                        continue;
                    }

                    if (targetSymbol is null) continue;

                    // Skip calls within the same assembly (test infrastructure)
                    if (targetSymbol.ContainingAssembly is not null &&
                        SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingAssembly, compilation.Assembly))
                        continue;

                    var targetId = SyntaxPass.GetSymbolId(targetSymbol);
                    var metadata = new Dictionary<string, string> { ["testFramework"] = framework };

                    // Covers: test → target
                    if (seenEdges.Add((testMethodId, targetId, EdgeType.Covers)))
                    {
                        edges.Add(new GraphEdge
                        {
                            FromId = testMethodId,
                            ToId = targetId,
                            Type = EdgeType.Covers,
                            IsExternal = true,
                            Metadata = metadata
                        });
                    }

                    // CoveredBy: target → test
                    if (seenEdges.Add((targetId, testMethodId, EdgeType.CoveredBy)))
                    {
                        edges.Add(new GraphEdge
                        {
                            FromId = targetId,
                            ToId = testMethodId,
                            Type = EdgeType.CoveredBy,
                            IsExternal = true,
                            Metadata = new Dictionary<string, string>(metadata)
                        });
                    }

                    // Ensure external node exists for target
                    if (!knownNodeIds.Contains(targetId) && !externalNodes.ContainsKey(targetId))
                    {
                        var assemblyName = targetSymbol.ContainingAssembly?.Name ?? "Unknown";
                        externalNodes[targetId] = new GraphNode
                        {
                            Id = targetId,
                            Name = targetSymbol.Name,
                            Kind = NodeKind.Method,
                            FilePath = string.Empty,
                            Signature = targetSymbol.ToDisplayString(),
                            Accessibility = SyntaxPass.MapAccessibility(targetSymbol.DeclaredAccessibility),
                            Metadata = new Dictionary<string, string> { ["assembly"] = assemblyName }
                        };
                    }
                }
            }
        }

        return (edges, externalNodes.Values.ToList());
    }

    private static string? DetectTestFramework(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                // Handle fully qualified and simple names
                var simpleName = name.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name;

                if (XUnitAttributes.Contains(simpleName)) return "xUnit";
                if (NUnitAttributes.Contains(simpleName)) return "NUnit";
                if (MSTestAttributes.Contains(simpleName)) return "MSTest";
            }
        }

        return null;
    }
}
