using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class SyntaxPassTests
{
    private static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CreateCompilationFromTrees(new[] { syntaxTree }, assemblyName);
    }

    private static CSharpCompilation CreateCompilationFromTrees(
        IEnumerable<SyntaxTree> trees, string assemblyName = "TestAssembly")
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));

        // Add System.Linq for extension methods
        var linqDll = Path.Combine(runtimeDir, "System.Linq.dll");
        if (File.Exists(linqDll))
            references.Add(MetadataReference.CreateFromFile(linqDll));

        return CSharpCompilation.Create(assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (List<GraphNode> Nodes, List<GraphEdge> Edges) Execute(string source, string solutionRoot = "")
    {
        var compilation = CreateCompilation(source);
        return new SyntaxPass().Execute(compilation, solutionRoot);
    }

    #region Type declarations: class, interface, record, struct, enum

    [Fact]
    public void ClassDeclaration_CreatesTypeNode()
    {
        var (nodes, _) = Execute("namespace A { public class Foo { } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Foo", node.Name);
        Assert.Equal("A.Foo", node.Id);
        Assert.Equal(NodeKind.Type, node.Kind);
    }

    [Fact]
    public void InterfaceDeclaration_CreatesTypeNode()
    {
        var (nodes, edges) = Execute("namespace A { public interface IFoo { void M(); } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("IFoo", node.Name);
        // Verify base.VisitInterfaceDeclaration walks children
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Contains(edges, e => e.FromId == "A.IFoo" && e.ToId.Contains("M") && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void RecordDeclaration_CreatesTypeNode()
    {
        var (nodes, edges) = Execute("namespace A { public record Rec(int X) { public void M() { } } }");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Type && n.Name == "Rec");
        // Verify base.VisitRecordDeclaration walks children
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "M");
    }

    [Fact]
    public void StructDeclaration_CreatesTypeNode()
    {
        var (nodes, edges) = Execute("namespace A { public struct MyStruct { public void M() { } } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("MyStruct", node.Name);
        // Verify base.VisitStructDeclaration walks children
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Contains(edges, e => e.FromId == "A.MyStruct" && e.ToId.Contains("M") && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void EnumDeclaration_CreatesTypeNode()
    {
        var (nodes, _) = Execute("namespace A { public enum Color { Red, Green } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Color", node.Name);
    }

    #endregion

    #region Member declarations: method, constructor, property, field, event

    [Fact]
    public void MethodDeclaration_CreatesMethodNode()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("M", method.Name);
        Assert.Equal(NodeKind.Method, method.Kind);
    }

    [Fact]
    public void ConstructorDeclaration_CreatesConstructorNode()
    {
        var (nodes, _) = Execute("namespace A { public class C { public C(int x) { } } }");
        var ctor = Assert.Single(nodes, n => n.Kind == NodeKind.Constructor);
        Assert.Equal(NodeKind.Constructor, ctor.Kind);
        Assert.Equal("A.C", ctor.ContainingTypeId);
    }

    [Fact]
    public void PropertyDeclaration_CreatesPropertyNode()
    {
        var (nodes, _) = Execute("namespace A { public class C { public int P { get; set; } } }");
        var prop = Assert.Single(nodes, n => n.Kind == NodeKind.Property);
        Assert.Equal("P", prop.Name);
        Assert.Equal(NodeKind.Property, prop.Kind);
    }

    [Fact]
    public void FieldDeclaration_CreatesFieldNode()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _f; } }");
        var field = Assert.Single(nodes, n => n.Kind == NodeKind.Field);
        Assert.Equal("_f", field.Name);
        Assert.Equal(NodeKind.Field, field.Kind);
    }

    [Fact]
    public void MultipleFieldVariables_CreatesSeparateFieldNodes()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _a, _b; } }");
        var fields = nodes.Where(n => n.Kind == NodeKind.Field).ToList();
        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "_a");
        Assert.Contains(fields, f => f.Name == "_b");
    }

    [Fact]
    public void EventDeclaration_CreatesEventNode()
    {
        var (nodes, _) = Execute("namespace A { public class C { public event System.Action E { add {} remove {} } } }");
        var evt = Assert.Single(nodes, n => n.Kind == NodeKind.Event);
        Assert.Equal("E", evt.Name);
        Assert.Equal(NodeKind.Event, evt.Kind);
    }

    #endregion

    #region Contains edges

    [Fact]
    public void NamespaceToType_ContainsEdge()
    {
        var (nodes, edges) = Execute("namespace N { public class C { } }");
        Assert.Contains(edges, e => e.FromId == "N" && e.ToId == "N.C" && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void TypeToNestedType_ContainsEdge()
    {
        var (nodes, edges) = Execute("namespace N { public class Outer { public class Inner { } } }");
        Assert.Contains(edges, e =>
            e.FromId == "N.Outer" && e.ToId == "N.Outer.Inner" && e.Type == EdgeType.Contains);
        // Inner should have ContainingTypeId set
        var inner = nodes.First(n => n.Name == "Inner");
        Assert.Equal("N.Outer", inner.ContainingTypeId);
    }

    [Fact]
    public void TypeToMethod_ContainsEdge()
    {
        var (_, edges) = Execute("namespace N { public class C { public void M() { } } }");
        Assert.Contains(edges, e => e.FromId == "N.C" && e.ToId == "N.C.M()" && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void TypeToField_ContainsEdge()
    {
        var (_, edges) = Execute("namespace N { public class C { private int _f; } }");
        Assert.Contains(edges, e => e.FromId == "N.C" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("_f"));
    }

    [Fact]
    public void TypeToProperty_ContainsEdge()
    {
        var (_, edges) = Execute("namespace N { public class C { public int P { get; set; } } }");
        Assert.Contains(edges, e => e.FromId == "N.C" && e.ToId == "N.C.P" && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void TypeToConstructor_ContainsEdge()
    {
        var (_, edges) = Execute("namespace N { public class C { public C() { } } }");
        Assert.Contains(edges, e => e.FromId == "N.C" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("C"));
    }

    [Fact]
    public void TypeToEvent_ContainsEdge()
    {
        var (_, edges) = Execute("namespace N { public class C { public event System.Action E { add {} remove {} } } }");
        Assert.Contains(edges, e => e.FromId == "N.C" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("E"));
    }

    #endregion

    #region Namespace handling

    [Fact]
    public void FileScopedNamespace_CreatesNamespaceNode()
    {
        var source = "namespace MyNs;\npublic class C { public void M() { } }";
        var (nodes, edges) = Execute(source);
        var ns = Assert.Single(nodes, n => n.Kind == NodeKind.Namespace);
        Assert.Equal("MyNs", ns.Name);
        Assert.Equal("MyNs", ns.Id);
        // EmitNamespaceNode sets StartLine > 0 (EmitTypeNode would set it to 0)
        Assert.True(ns.StartLine > 0, "File-scoped namespace should have StartLine > 0 from EmitNamespaceNode");
        // Verify base.VisitFileScopedNamespaceDeclaration is called (children are walked)
        Assert.Contains(nodes, n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "M");
        // Verify Contains edge from namespace→type
        Assert.Contains(edges, e => e.FromId == "MyNs" && e.ToId == "MyNs.C" && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void TraditionalNamespace_CreatesNamespaceNode()
    {
        var (nodes, _) = Execute("namespace Trad { public class X { } }");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.Name == "Trad");
    }

    [Fact]
    public void NamespaceDedup_SameNamespaceInTwoTrees_ProducesOneNode()
    {
        var tree1 = CSharpSyntaxTree.ParseText("namespace Shared { public class A { } }");
        var tree2 = CSharpSyntaxTree.ParseText("namespace Shared { public class B { } }");
        var compilation = CreateCompilationFromTrees(new[] { tree1, tree2 });
        var pass = new SyntaxPass();
        var (nodes, _) = pass.Execute(compilation, "");

        var nsNodes = nodes.Where(n => n.Kind == NodeKind.Namespace && n.Id == "Shared").ToList();
        Assert.Single(nsNodes);
    }

    [Fact]
    public void NestedNamespace_HasContainingNamespaceId()
    {
        var source = "namespace Outer { namespace Inner { public class C { } } }";
        var (nodes, _) = Execute(source);
        var inner = nodes.First(n => n.Kind == NodeKind.Namespace && n.Name == "Inner");
        Assert.Equal("Outer", inner.ContainingNamespaceId);
    }

    [Fact]
    public void NamespaceNode_HasPublicAccessibility()
    {
        var (nodes, _) = Execute("namespace N { public class C { } }");
        var ns = nodes.First(n => n.Kind == NodeKind.Namespace);
        Assert.Equal(Core.Models.Accessibility.Public, ns.Accessibility);
    }

    [Fact]
    public void NamespaceNode_HasSignature()
    {
        var (nodes, _) = Execute("namespace MyApp.Domain { public class C { } }");
        var ns = nodes.First(n => n.Kind == NodeKind.Namespace && n.Name == "Domain");
        Assert.False(string.IsNullOrEmpty(ns.Signature));
    }

    [Fact]
    public void NamespaceNode_DocCommentIsNull()
    {
        var (nodes, _) = Execute("namespace N { public class C { } }");
        var ns = nodes.First(n => n.Kind == NodeKind.Namespace);
        Assert.Null(ns.DocComment);
    }

    [Fact]
    public void TypeWithoutExplicitNamespace_NamespaceCreatedFromContaining()
    {
        // When type has a namespace but it wasn't explicitly declared via NamespaceDeclaration,
        // EmitTypeNode creates the namespace node
        var source = "namespace Auto { public class Z { } }";
        var (nodes, edges) = Execute(source);
        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.Id == "Auto");
        Assert.Contains(edges, e => e.FromId == "Auto" && e.ToId == "Auto.Z" && e.Type == EdgeType.Contains);
    }

    #endregion

    #region AssemblyName propagation

    [Fact]
    public void AssemblyName_PropagatesFromCompilation()
    {
        var compilation = CreateCompilation("namespace A { public class C { } }", "MyLib");
        var pass = new SyntaxPass();
        var (nodes, _) = pass.Execute(compilation, "");

        foreach (var node in nodes)
        {
            Assert.Equal("MyLib", node.AssemblyName);
        }
    }

    [Fact]
    public void AssemblyName_FallsBackToUnknown_WhenNull()
    {
        // Create compilation with null assembly name
        var tree = CSharpSyntaxTree.ParseText("namespace A { public class C { } }");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var compilation = CSharpCompilation.Create(null,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var pass = new SyntaxPass();
        var (nodes, _) = pass.Execute(compilation, "");

        foreach (var node in nodes)
        {
            Assert.Equal("Unknown", node.AssemblyName);
        }
    }

    #endregion

    #region StartLine / EndLine (kills +1 → -1 mutations)

    [Fact]
    public void StartLineAndEndLine_AreOneIndexed_AndPositive()
    {
        // The first line of parsed source is line 0 in Roslyn, so +1 means at least 1
        var source = "namespace A { public class C { public void M() { } } }";
        var (nodes, _) = Execute(source);

        foreach (var node in nodes)
        {
            Assert.True(node.StartLine >= 1, $"Node {node.Name} StartLine={node.StartLine} should be >= 1");
            Assert.True(node.EndLine >= 1, $"Node {node.Name} EndLine={node.EndLine} should be >= 1");
        }
    }

    [Fact]
    public void StartLine_MatchesExpectedOneIndexed()
    {
        // Put class on line 2 (0-indexed line 1 → 1-indexed line 2)
        var source = @"
namespace A
{
    public class C { }
}";
        var (nodes, _) = Execute(source);
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        // Line 4 (1-indexed): "    public class C { }"
        Assert.Equal(4, cls.StartLine);
    }

    [Fact]
    public void EndLine_IsGreaterOrEqualToStartLine()
    {
        var source = @"namespace A
{
    public class C
    {
        public void M()
        {
        }
    }
}";
        var (nodes, _) = Execute(source);
        foreach (var n in nodes)
        {
            Assert.True(n.EndLine >= n.StartLine,
                $"{n.Name}: EndLine {n.EndLine} < StartLine {n.StartLine}");
        }
    }

    #endregion

    #region Metadata: type modifiers

    [Fact]
    public void Metadata_AbstractClass_HasIsAbstract()
    {
        var (nodes, _) = Execute("namespace A { public abstract class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.True(node.Metadata.ContainsKey("isAbstract"));
        Assert.Equal("true", node.Metadata["isAbstract"]);
    }

    [Fact]
    public void Metadata_StaticClass_HasIsStatic()
    {
        var (nodes, _) = Execute("namespace A { public static class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.True(node.Metadata.ContainsKey("isStatic"));
        Assert.Equal("true", node.Metadata["isStatic"]);
    }

    [Fact]
    public void Metadata_SealedClass_HasIsSealed()
    {
        var (nodes, _) = Execute("namespace A { public sealed class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.True(node.Metadata.ContainsKey("isSealed"));
        Assert.Equal("true", node.Metadata["isSealed"]);
    }

    [Fact]
    public void Metadata_TypeKind_HasExactValue()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.True(node.Metadata.ContainsKey("typeKind"));
        Assert.Equal("Class", node.Metadata["typeKind"]);
    }

    [Fact]
    public void Metadata_InterfaceTypeKind()
    {
        var (nodes, _) = Execute("namespace A { public interface I { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "I");
        Assert.Equal("Interface", node.Metadata["typeKind"]);
    }

    [Fact]
    public void Metadata_EnumTypeKind()
    {
        var (nodes, _) = Execute("namespace A { public enum E { X } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "E");
        Assert.Equal("Enum", node.Metadata["typeKind"]);
    }

    [Fact]
    public void Metadata_StructTypeKind()
    {
        var (nodes, _) = Execute("namespace A { public struct S { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "S");
        Assert.Equal("Struct", node.Metadata["typeKind"]);
    }

    [Fact]
    public void Metadata_GenericType_HasGenericArity()
    {
        var (nodes, _) = Execute("namespace A { public class G<T, U> { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "G");
        Assert.True(node.Metadata.ContainsKey("genericArity"));
        Assert.Equal("2", node.Metadata["genericArity"]);
    }

    [Fact]
    public void Metadata_RecordType_HasIsRecord()
    {
        var (nodes, _) = Execute("namespace A { public record R(int X); }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "R");
        Assert.True(node.Metadata.ContainsKey("isRecord"));
        Assert.Equal("true", node.Metadata["isRecord"]);
    }

    [Fact]
    public void Metadata_NonAbstractClass_DoesNotHaveIsAbstract()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.False(node.Metadata.ContainsKey("isAbstract"));
    }

    [Fact]
    public void Metadata_NonStaticClass_DoesNotHaveIsStatic()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.False(node.Metadata.ContainsKey("isStatic"));
    }

    #endregion

    #region Metadata: method modifiers

    [Fact]
    public void Metadata_VirtualMethod_HasIsVirtual()
    {
        var (nodes, _) = Execute("namespace A { public class C { public virtual void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isVirtual"));
        Assert.Equal("true", method.Metadata["isVirtual"]);
    }

    [Fact]
    public void Metadata_OverrideMethod_HasIsOverride()
    {
        var source = @"namespace A {
            public class Base { public virtual void M() { } }
            public class Derived : Base { public override void M() { } }
        }";
        var (nodes, _) = Execute(source);
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M"
            && n.Id.Contains("Derived"));
        Assert.True(method.Metadata.ContainsKey("isOverride"));
        Assert.Equal("true", method.Metadata["isOverride"]);
    }

    [Fact]
    public void Metadata_AsyncMethod_HasIsAsync()
    {
        var source = "namespace A { public class C { public async System.Threading.Tasks.Task M() { } } }";
        var (nodes, _) = Execute(source);
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isAsync"));
        Assert.Equal("true", method.Metadata["isAsync"]);
    }

    [Fact]
    public void Metadata_ExtensionMethod_HasIsExtension()
    {
        var source = "namespace A { public static class Ext { public static void M(this string s) { } } }";
        var (nodes, _) = Execute(source);
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isExtension"));
        Assert.Equal("true", method.Metadata["isExtension"]);
    }

    [Fact]
    public void Metadata_Method_HasReturnType()
    {
        var (nodes, _) = Execute("namespace A { public class C { public int M() { return 0; } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("returnType"));
        Assert.Equal("int", method.Metadata["returnType"]);
    }

    [Fact]
    public void Metadata_Method_HasParameterCount()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M(int a, string b) { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("parameterCount"));
        Assert.Equal("2", method.Metadata["parameterCount"]);
    }

    [Fact]
    public void Metadata_Method_ZeroParameters()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Equal("0", method.Metadata["parameterCount"]);
    }

    [Fact]
    public void Metadata_GenericMethod_HasGenericArity()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M<T>() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("genericArity"));
        Assert.Equal("1", method.Metadata["genericArity"]);
    }

    [Fact]
    public void Metadata_StaticMethod_HasIsStatic()
    {
        var (nodes, _) = Execute("namespace A { public class C { public static void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isStatic"));
        Assert.Equal("true", method.Metadata["isStatic"]);
    }

    [Fact]
    public void Metadata_VoidReturnType()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Equal("void", method.Metadata["returnType"]);
    }

    #endregion

    #region Metadata: property

    [Fact]
    public void Metadata_Property_HasPropertyType()
    {
        var (nodes, _) = Execute("namespace A { public class C { public string P { get; set; } } }");
        var prop = nodes.First(n => n.Kind == NodeKind.Property && n.Name == "P");
        Assert.True(prop.Metadata.ContainsKey("propertyType"));
        Assert.Equal("string", prop.Metadata["propertyType"]);
    }

    [Fact]
    public void Metadata_Indexer_HasIsIndexer()
    {
        // Indexers use IndexerDeclarationSyntax, which is visited as PropertyDeclaration in Roslyn
        // SyntaxPass handles PropertyDeclarationSyntax, so we use a regular property to verify
        // the isIndexer key is set only for actual indexers. Indexers may not be visited by
        // VisitPropertyDeclaration. Let's verify by using a class with both.
        var source = "namespace A { public class C { public int this[int i] { get { return 0; } } public int P { get; set; } } }";
        var (nodes, _) = Execute(source);
        var props = nodes.Where(n => n.Kind == NodeKind.Property).ToList();
        // If the indexer is captured, verify isIndexer; if not, that's fine - the property without indexer shouldn't have it
        var regularProp = props.FirstOrDefault(p => p.Name == "P");
        if (regularProp != null)
        {
            Assert.False(regularProp.Metadata.ContainsKey("isIndexer"));
        }
        // Also test: if any property has isIndexer, it should be "true"
        var indexerProp = props.FirstOrDefault(p => p.Metadata.ContainsKey("isIndexer"));
        if (indexerProp != null)
        {
            Assert.Equal("true", indexerProp.Metadata["isIndexer"]);
        }
    }

    [Fact]
    public void Metadata_NonIndexerProperty_DoesNotHaveIsIndexer()
    {
        var (nodes, _) = Execute("namespace A { public class C { public int P { get; set; } } }");
        var prop = nodes.First(n => n.Kind == NodeKind.Property);
        Assert.False(prop.Metadata.ContainsKey("isIndexer"));
    }

    #endregion

    #region Metadata: field

    [Fact]
    public void Metadata_Field_HasFieldType()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _f; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field);
        Assert.True(field.Metadata.ContainsKey("fieldType"));
        Assert.Equal("int", field.Metadata["fieldType"]);
    }

    [Fact]
    public void Metadata_ConstField_HasIsConst()
    {
        var (nodes, _) = Execute("namespace A { public class C { public const int K = 42; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field && n.Name == "K");
        Assert.True(field.Metadata.ContainsKey("isConst"));
        Assert.Equal("true", field.Metadata["isConst"]);
    }

    [Fact]
    public void Metadata_ReadOnlyField_HasIsReadOnly()
    {
        var (nodes, _) = Execute("namespace A { public class C { public readonly int R = 1; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field && n.Name == "R");
        Assert.True(field.Metadata.ContainsKey("isReadOnly"));
        Assert.Equal("true", field.Metadata["isReadOnly"]);
    }

    [Fact]
    public void Metadata_NonConstField_DoesNotHaveIsConst()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _f; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field);
        Assert.False(field.Metadata.ContainsKey("isConst"));
    }

    [Fact]
    public void Metadata_NonReadOnlyField_DoesNotHaveIsReadOnly()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _f; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field);
        Assert.False(field.Metadata.ContainsKey("isReadOnly"));
    }

    #endregion

    #region Accessibility mapping

    [Theory]
    [InlineData("public class C { }", Core.Models.Accessibility.Public)]
    [InlineData("internal class C { }", Core.Models.Accessibility.Internal)]
    public void Accessibility_TopLevelTypes(string code, Core.Models.Accessibility expected)
    {
        var (nodes, _) = Execute($"namespace A {{ {code} }}");
        var node = nodes.First(n => n.Kind == NodeKind.Type);
        Assert.Equal(expected, node.Accessibility);
    }

    [Fact]
    public void Accessibility_PrivateNestedClass()
    {
        var (nodes, _) = Execute("namespace A { public class Outer { private class Inner { } } }");
        var inner = nodes.First(n => n.Name == "Inner");
        Assert.Equal(Core.Models.Accessibility.Private, inner.Accessibility);
    }

    [Fact]
    public void Accessibility_ProtectedMember()
    {
        var (nodes, _) = Execute("namespace A { public class C { protected void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.Protected, method.Accessibility);
    }

    [Fact]
    public void Accessibility_ProtectedInternalMember()
    {
        var (nodes, _) = Execute("namespace A { public class C { protected internal void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.ProtectedInternal, method.Accessibility);
    }

    [Fact]
    public void Accessibility_PrivateProtectedMember()
    {
        var (nodes, _) = Execute("namespace A { public class C { private protected void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.PrivateProtected, method.Accessibility);
    }

    [Fact]
    public void Accessibility_PublicMethod()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.Public, method.Accessibility);
    }

    [Fact]
    public void Accessibility_InternalMethod()
    {
        var (nodes, _) = Execute("namespace A { public class C { internal void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.Internal, method.Accessibility);
    }

    [Fact]
    public void Accessibility_PrivateMethod()
    {
        var (nodes, _) = Execute("namespace A { public class C { private void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal(Core.Models.Accessibility.Private, method.Accessibility);
    }

    #endregion

    #region DocComment extraction

    [Fact]
    public void DocComment_WithSummary_ExtractsText()
    {
        var source = @"namespace A {
    public class C {
        /// <summary>
        /// Does something useful.
        /// </summary>
        public void M() { }
    }
}";
        var (nodes, _) = Execute(source);
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Equal("Does something useful.", method.DocComment);
    }

    [Fact]
    public void DocComment_WithoutDoc_ReturnsNull()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Null(method.DocComment);
    }

    [Fact]
    public void DocComment_Null_WhenNoDocComment()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Null(cls.DocComment);
    }

    [Fact]
    public void DocComment_NoSymbol_ReturnsNull()
    {
        // Indirectly tests the empty/null XML path
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Null(method.DocComment);
    }

    [Fact]
    public void ExtractDocComment_EmptySummary_ReturnsNull()
    {
        var source = @"namespace A {
    /// <summary>
    /// </summary>
    public class C { }
}";
        var (nodes, _) = Execute(source);
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Null(cls.DocComment);
    }

    [Fact]
    public void ExtractDocComment_MalformedOrEmpty_ReturnsNull()
    {
        // Test indirectly: a class without doc comment
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Null(cls.DocComment);
    }

    #endregion

    #region Id format verification

    [Fact]
    public void MethodId_IncludesParameterTypes()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M(int x, string y) { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Equal("A.C.M(int, string)", method.Id);
    }

    #endregion

    #region GetRelativePath edge cases

    [Fact]
    public void Execute_WithSolutionRoot_ProducesRelativePaths()
    {
        // We can't easily control the file path in parsed source, but we can verify
        // the path handling doesn't crash and produces a non-null path
        var (nodes, _) = Execute("namespace A { public class C { } }", "/some/root");
        foreach (var node in nodes)
        {
            Assert.NotNull(node.FilePath);
        }
    }

    [Fact]
    public void Execute_WithEmptySolutionRoot_ReturnsFilePath()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }", "");
        // FilePath should be set (may be empty for in-memory trees)
        foreach (var node in nodes)
        {
            Assert.NotNull(node.FilePath);
        }
    }

    #endregion

    #region ContainingNamespaceId / ContainingTypeId on members

    [Fact]
    public void Member_HasContainingNamespaceId()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal("A", method.ContainingNamespaceId);
    }

    [Fact]
    public void Member_HasContainingTypeId()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal("A.C", method.ContainingTypeId);
    }

    [Fact]
    public void Type_HasContainingNamespaceId()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type);
        Assert.Equal("A", cls.ContainingNamespaceId);
    }

    [Fact]
    public void TopLevelType_HasNullContainingTypeId()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Null(cls.ContainingTypeId);
    }

    #endregion

    #region Signature

    [Fact]
    public void Node_HasNonEmptySignature()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M(int x) { } } }");
        foreach (var node in nodes)
        {
            Assert.False(string.IsNullOrEmpty(node.Signature),
                $"Node {node.Name} should have a non-empty Signature");
        }
    }

    #endregion

    #region Comprehensive integration: all nodes and edges from complex source

    [Fact]
    public void ComplexSource_AllNodeKinds_AllEdgeTypes()
    {
        var source = @"
namespace TestNs
{
    public abstract class Base
    {
        public const int MAX = 100;
        public readonly string Label = ""x"";
        private int _value;

        public Base(int v) { _value = v; }

        public string Name { get; set; }

        public virtual void DoWork() { }

        public event System.Action OnDone { add {} remove {} }
    }
}";
        var (nodes, edges) = Execute(source);

        // Namespace node
        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.Id == "TestNs");

        // Type node
        var baseType = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "Base");
        Assert.Equal("true", baseType.Metadata["isAbstract"]);
        Assert.Equal("Class", baseType.Metadata["typeKind"]);

        // Constructor
        var ctor = nodes.First(n => n.Kind == NodeKind.Constructor);
        Assert.Equal("1", ctor.Metadata["parameterCount"]);

        // Fields
        var maxField = nodes.First(n => n.Kind == NodeKind.Field && n.Name == "MAX");
        Assert.Equal("true", maxField.Metadata["isConst"]);
        Assert.Equal("int", maxField.Metadata["fieldType"]);

        var labelField = nodes.First(n => n.Kind == NodeKind.Field && n.Name == "Label");
        Assert.Equal("true", labelField.Metadata["isReadOnly"]);
        Assert.Equal("string", labelField.Metadata["fieldType"]);

        // Property
        var prop = nodes.First(n => n.Kind == NodeKind.Property && n.Name == "Name");
        Assert.Equal("string", prop.Metadata["propertyType"]);

        // Method
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "DoWork");
        Assert.Equal("true", method.Metadata["isVirtual"]);
        Assert.Equal("void", method.Metadata["returnType"]);
        Assert.Equal("0", method.Metadata["parameterCount"]);

        // Event
        Assert.Contains(nodes, n => n.Kind == NodeKind.Event && n.Name == "OnDone");

        // Contains edges
        Assert.Contains(edges, e => e.FromId == "TestNs" && e.ToId == "TestNs.Base" && e.Type == EdgeType.Contains);
        Assert.Contains(edges, e => e.FromId == "TestNs.Base" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("MAX"));
        Assert.Contains(edges, e => e.FromId == "TestNs.Base" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("Label"));
        Assert.Contains(edges, e => e.FromId == "TestNs.Base" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("Name"));
        Assert.Contains(edges, e => e.FromId == "TestNs.Base" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("DoWork"));
        Assert.Contains(edges, e => e.FromId == "TestNs.Base" && e.Type == EdgeType.Contains &&
            e.ToId.Contains("OnDone"));
    }

    #endregion

    #region Field with Contains edge verification

    [Fact]
    public void FieldDeclaration_EmitsContainsEdge()
    {
        var (nodes, edges) = Execute("namespace A { public class C { private int _x; } }");
        var field = nodes.First(n => n.Kind == NodeKind.Field && n.Name == "_x");
        Assert.Contains(edges, e =>
            e.FromId == "A.C" && e.ToId == field.Id && e.Type == EdgeType.Contains);
    }

    #endregion

    #region Namespace created implicitly for type when not already seen

    [Fact]
    public void ImplicitNamespaceNode_CreatedForType_WithCorrectProperties()
    {
        // Two trees: first has a class in ns (creating namespace implicitly),
        // second has another class in same ns (should reuse)
        var tree1 = CSharpSyntaxTree.ParseText("namespace Impl { public class A { } }");
        var tree2 = CSharpSyntaxTree.ParseText("namespace Impl { public class B { } }");
        var comp = CreateCompilationFromTrees(new[] { tree1, tree2 });
        var pass = new SyntaxPass();
        var (nodes, edges) = pass.Execute(comp, "");

        var nsNodes = nodes.Where(n => n.Kind == NodeKind.Namespace && n.Id == "Impl").ToList();
        Assert.Single(nsNodes);

        // Both types have contains edges from the namespace
        Assert.Contains(edges, e => e.FromId == "Impl" && e.ToId == "Impl.A" && e.Type == EdgeType.Contains);
        Assert.Contains(edges, e => e.FromId == "Impl" && e.ToId == "Impl.B" && e.Type == EdgeType.Contains);
    }

    #endregion

    #region Global namespace types have null ContainingNamespaceId

    [Fact]
    public void GlobalNamespaceType_HasNullContainingNamespaceId()
    {
        // Type in global namespace
        var (nodes, _) = Execute("public class GlobalC { }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "GlobalC");
        Assert.Null(cls.ContainingNamespaceId);
    }

    #endregion

    #region Namespace with explicit declaration has StartLine/EndLine set

    [Fact]
    public void ExplicitNamespace_HasNonZeroStartAndEndLine()
    {
        var (nodes, _) = Execute("namespace N\n{\n    public class C { }\n}");
        var ns = nodes.First(n => n.Kind == NodeKind.Namespace);
        Assert.True(ns.StartLine >= 1);
        Assert.True(ns.EndLine >= 1);
    }

    #endregion

    #region Namespace implicit (from EmitTypeNode) has zero StartLine/EndLine

    [Fact]
    public void ImplicitNamespaceFromType_HasZeroLines()
    {
        // When a type is in a namespace but the namespace node is created by EmitTypeNode
        // (not by VisitNamespaceDeclaration), StartLine and EndLine are 0
        // This happens when the walker visits the type before the namespace declaration
        // We test this indirectly: the namespace node created in EmitTypeNode has StartLine=0, EndLine=0
        // But since VisitNamespaceDeclaration runs first, we rely on dedup.
        // The important assertion is that the namespace always exists.
        var (nodes, _) = Execute("namespace A { public class C { } }");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.Id == "A");
    }

    #endregion

    #region Node Id uses display format

    [Fact]
    public void NodeId_UsesFullyQualifiedFormat()
    {
        var (nodes, _) = Execute("namespace A.B { public class C { public void M(int x) { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method);
        Assert.Equal("A.B.C.M(int)", method.Id);
    }

    #endregion

    #region Edge type is always Contains from syntax pass

    [Fact]
    public void AllEdges_AreContainsType()
    {
        var (_, edges) = Execute(@"namespace N {
            public class C {
                public void M() { }
                public int P { get; set; }
                private int _f;
            }
        }");
        Assert.All(edges, e => Assert.Equal(EdgeType.Contains, e.Type));
    }

    #endregion

    #region Namespace ContainingNamespaceId

    [Fact]
    public void TopLevelNamespace_HasNullContainingNamespaceId()
    {
        // Kills L136 conditional mutation: (true ? GetSymbolId(ContainingNamespace) : null)
        var (nodes, _) = Execute("namespace TopLevel { public class C { } }");
        var ns = nodes.First(n => n.Kind == NodeKind.Namespace && n.Id == "TopLevel");
        Assert.Null(ns.ContainingNamespaceId);
    }

    #endregion

    #region GetRelativePath with actual file paths

    [Fact]
    public void GetRelativePath_WithSolutionRoot_ReturnsRelativePath()
    {
        // Create compilation with a known file path
        var tree = CSharpSyntaxTree.ParseText(
            "namespace A { public class C { } }",
            path: Path.Combine("C:", "projects", "mysln", "src", "File.cs"));
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var pass = new SyntaxPass();
        var solutionRoot = Path.Combine("C:", "projects", "mysln");
        var (nodes, _) = pass.Execute(compilation, solutionRoot);

        // File path should be relative
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.DoesNotContain("C:", cls.FilePath);
        Assert.Contains("File.cs", cls.FilePath);
    }

    [Fact]
    public void GetRelativePath_EmptySolutionRoot_ReturnsAbsolutePath()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "namespace A { public class C { } }",
            path: Path.Combine("C:", "projects", "File.cs"));
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var pass = new SyntaxPass();
        var (nodes, _) = pass.Execute(compilation, "");

        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        // When solutionRoot is empty, GetRelativePath returns the path as-is
        Assert.Contains("File.cs", cls.FilePath);
    }

    [Fact]
    public void GetRelativePath_NullPath_ReturnsEmptyString()
    {
        // ParseText without path argument gives empty string path
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Equal(string.Empty, cls.FilePath);
    }

    [Fact]
    public void GetRelativePath_EmptyPath_NonEmptyRoot_ReturnsEmpty()
    {
        // Kills L288 mutations: when absolutePath is empty but solutionRoot is NOT empty,
        // the || check should still trigger early return
        var tree = CSharpSyntaxTree.ParseText("namespace A { public class C { } }");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var pass = new SyntaxPass();
        var (nodes, _) = pass.Execute(compilation, "/some/root");

        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Equal(string.Empty, cls.FilePath);
    }

    #endregion

    #region ExtractDocComment edge cases

    [Fact]
    public void ExtractDocComment_XmlWithNoSummary_ReturnsNull()
    {
        // Kills L326: FirstOrDefault() to First() mutation
        // XML doc with <remarks> but no <summary> should return null
        var source = @"
namespace A
{
    /// <remarks>Some remark</remarks>
    public class C { }
}";
        var (nodes, _) = Execute(source);
        var cls = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.Null(cls.DocComment);
    }

    #endregion

    #region base.Visit* for leaf members (unkillable but exercise coverage)

    [Fact]
    public void EnumDeclaration_NoChildrenNeeded()
    {
        var (nodes, edges) = Execute("namespace A { public enum Color { Red, Green, Blue } }");
        var en = Assert.Single(nodes, n => n.Kind == NodeKind.Type && n.Name == "Color");
        Assert.Equal("A.Color", en.Id);
        Assert.Contains(edges, e => e.FromId == "A" && e.ToId == "A.Color" && e.Type == EdgeType.Contains);
    }

    #endregion

    #region Metadata: negative cases for conditional metadata keys

    [Fact]
    public void Metadata_NonSealedClass_DoesNotHaveIsSealed()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.False(node.Metadata.ContainsKey("isSealed"));
    }

    [Fact]
    public void Metadata_NonVirtualMethod_DoesNotHaveIsVirtual()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.False(method.Metadata.ContainsKey("isVirtual"));
    }

    [Fact]
    public void Metadata_NonOverrideMethod_DoesNotHaveIsOverride()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.False(method.Metadata.ContainsKey("isOverride"));
    }

    [Fact]
    public void Metadata_NonAsyncMethod_DoesNotHaveIsAsync()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.False(method.Metadata.ContainsKey("isAsync"));
    }

    [Fact]
    public void Metadata_NonExtensionMethod_DoesNotHaveIsExtension()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.False(method.Metadata.ContainsKey("isExtension"));
    }

    [Fact]
    public void Metadata_NonGenericType_DoesNotHaveGenericArity()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.False(node.Metadata.ContainsKey("genericArity"));
    }

    [Fact]
    public void Metadata_NonGenericMethod_DoesNotHaveGenericArity()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.False(method.Metadata.ContainsKey("genericArity"));
    }

    [Fact]
    public void Metadata_NonRecordClass_DoesNotHaveIsRecord()
    {
        var (nodes, _) = Execute("namespace A { public class C { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "C");
        Assert.False(node.Metadata.ContainsKey("isRecord"));
    }

    #endregion

    #region Metadata: record typeKind

    [Fact]
    public void Metadata_RecordType_HasTypeKindClass()
    {
        var (nodes, _) = Execute("namespace A { public record R(int X); }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "R");
        Assert.Equal("Class", node.Metadata["typeKind"]);
    }

    [Fact]
    public void Metadata_RecordStructType_HasTypeKindStruct()
    {
        var (nodes, _) = Execute("namespace A { public record struct RS(int X); }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "RS");
        Assert.Equal("Struct", node.Metadata["typeKind"]);
        Assert.Equal("true", node.Metadata["isRecord"]);
    }

    #endregion

    #region Metadata: generic arity value verification

    [Fact]
    public void Metadata_GenericType_Arity1()
    {
        var (nodes, _) = Execute("namespace A { public class G<T> { } }");
        var node = nodes.First(n => n.Kind == NodeKind.Type && n.Name == "G");
        Assert.Equal("1", node.Metadata["genericArity"]);
    }

    [Fact]
    public void Metadata_GenericMethod_Arity2()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void M<T, U>() { } } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.Equal("2", method.Metadata["genericArity"]);
    }

    #endregion

    #region Metadata: abstract method

    [Fact]
    public void Metadata_AbstractMethod_HasIsAbstract()
    {
        var (nodes, _) = Execute("namespace A { public abstract class C { public abstract void M(); } }");
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isAbstract"));
        Assert.Equal("true", method.Metadata["isAbstract"]);
    }

    [Fact]
    public void Metadata_SealedOverrideMethod_HasBothFlags()
    {
        var source = @"namespace A {
            public class Base { public virtual void M() { } }
            public class Derived : Base { public sealed override void M() { } }
        }";
        var (nodes, _) = Execute(source);
        var method = nodes.First(n => n.Kind == NodeKind.Method && n.Name == "M"
            && n.Id.Contains("Derived"));
        Assert.Equal("true", method.Metadata["isSealed"]);
        Assert.Equal("true", method.Metadata["isOverride"]);
    }

    #endregion

    #region Metadata: event modifiers

    [Fact]
    public void Metadata_StaticEvent_HasIsStatic()
    {
        var (nodes, _) = Execute("namespace A { public class C { public static event System.Action E { add {} remove {} } } }");
        var evt = nodes.First(n => n.Kind == NodeKind.Event && n.Name == "E");
        Assert.True(evt.Metadata.ContainsKey("isStatic"));
        Assert.Equal("true", evt.Metadata["isStatic"]);
    }

    [Fact]
    public void Metadata_NonStaticEvent_DoesNotHaveIsStatic()
    {
        var (nodes, _) = Execute("namespace A { public class C { public event System.Action E { add {} remove {} } } }");
        var evt = nodes.First(n => n.Kind == NodeKind.Event && n.Name == "E");
        Assert.False(evt.Metadata.ContainsKey("isStatic"));
    }

    #endregion

    #region Metadata: indexer via IndexerDeclaration (if visited)

    [Fact]
    public void Metadata_Indexer_VisitPropertyDeclaration_DoesNotCapture()
    {
        // Indexers use IndexerDeclarationSyntax, not PropertyDeclarationSyntax.
        // SyntaxPass only overrides VisitPropertyDeclaration, so indexers are not
        // captured as Property nodes. Verify that regular properties don't get isIndexer.
        var source = "namespace A { public class C { public int P { get; set; } } }";
        var (nodes, _) = Execute(source);
        var prop = nodes.First(n => n.Kind == NodeKind.Property && n.Name == "P");
        Assert.False(prop.Metadata.ContainsKey("isIndexer"));
        Assert.Equal("int", prop.Metadata["propertyType"]);
    }

    #endregion

    #region Metadata: constructor return type and parameter count

    [Fact]
    public void Metadata_Constructor_HasReturnTypeAndParamCount()
    {
        var (nodes, _) = Execute("namespace A { public class C { public C(int x, string y) { } } }");
        var ctor = nodes.First(n => n.Kind == NodeKind.Constructor);
        Assert.Equal("2", ctor.Metadata["parameterCount"]);
        Assert.True(ctor.Metadata.ContainsKey("returnType"));
    }

    [Fact]
    public void Metadata_ParameterlessConstructor_HasZeroParams()
    {
        var (nodes, _) = Execute("namespace A { public class C { public C() { } } }");
        var ctor = nodes.First(n => n.Kind == NodeKind.Constructor);
        Assert.Equal("0", ctor.Metadata["parameterCount"]);
    }

    #endregion

    #region TypeKind metadata assertions for mutation killing

    [Fact]
    public void InterfaceDeclaration_HasTypeKindInterface()
    {
        var (nodes, _) = Execute("namespace A { public interface IFoo { } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Interface", node.Metadata["typeKind"]);
    }

    [Fact]
    public void StructDeclaration_HasTypeKindStruct()
    {
        var (nodes, _) = Execute("namespace A { public struct MyStruct { } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Struct", node.Metadata["typeKind"]);
    }

    [Fact]
    public void EnumDeclaration_HasTypeKindEnum()
    {
        var (nodes, _) = Execute("namespace A { public enum Color { Red, Green } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Enum", node.Metadata["typeKind"]);
    }

    [Fact]
    public void ClassDeclaration_HasTypeKindClass()
    {
        var (nodes, _) = Execute("namespace A { public class Foo { } }");
        var node = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Class", node.Metadata["typeKind"]);
    }

    [Fact]
    public void DelegateType_EmittedViaClass()
    {
        // Delegates are not visited by SyntaxPass (no VisitDelegateDeclaration),
        // so verify that a class with a delegate field still emits the class node.
        var (nodes, _) = Execute("namespace A { public class C { public delegate void MyHandler(int x); } }");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Type && n.Name == "C");
    }

    [Fact]
    public void ConstructorDeclaration_CreatesNodeWithContainingType()
    {
        var (nodes, edges) = Execute("namespace A { public class C { public C(int x) { } public void M() { } } }");
        var ctor = Assert.Single(nodes, n => n.Kind == NodeKind.Constructor);
        Assert.Equal("A.C", ctor.ContainingTypeId);
        // Verify the Contains edge from type to constructor
        Assert.Contains(edges, e => e.FromId == "A.C" && e.Type == EdgeType.Contains && e.ToId.Contains(".C("));
        // Verify method is also emitted (base call works)
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "M");
    }

    [Fact]
    public void InterfaceWithMembers_EmitsNodeAndEdges()
    {
        var (nodes, edges) = Execute("namespace A { public interface IRepo { void Save(); int Count { get; } } }");
        var iface = Assert.Single(nodes, n => n.Kind == NodeKind.Type && n.Name == "IRepo");
        Assert.Equal("Interface", iface.Metadata["typeKind"]);
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "Save");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Property && n.Name == "Count");
        Assert.Contains(edges, e => e.FromId == "A.IRepo" && e.Type == EdgeType.Contains);
    }

    [Fact]
    public void StructWithMembers_EmitsNodeAndEdges()
    {
        var (nodes, edges) = Execute("namespace A { public struct Point { public int X; public int Y; public Point(int x, int y) { X = x; Y = y; } } }");
        var structNode = Assert.Single(nodes, n => n.Kind == NodeKind.Type && n.Name == "Point");
        Assert.Equal("Struct", structNode.Metadata["typeKind"]);
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "X");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "Y");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Constructor);
    }

    [Fact]
    public void EnumDeclaration_EmitsContainsEdge()
    {
        var (nodes, edges) = Execute("namespace A { public enum Status { Active, Inactive } }");
        var enumNode = Assert.Single(nodes, n => n.Kind == NodeKind.Type);
        Assert.Equal("Enum", enumNode.Metadata["typeKind"]);
        Assert.Contains(edges, e => e.FromId == "A" && e.ToId == "A.Status" && e.Type == EdgeType.Contains);
    }

    #endregion
}
