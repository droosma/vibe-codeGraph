using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Accessibility = CodeGraph.Core.Models.Accessibility;

namespace CodeGraph.Indexer.Tests.Passes;

/// <summary>
/// Additional mutation-killing tests for SyntaxPass.
/// Targets: metadata key/value correctness, edge confidence values,
/// accessibility mapping boundary cases, relative path logic, 
/// and doc comment extraction.
/// </summary>
public class SyntaxPassMutationTests
{
    private static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CreateCompilation(syntaxTree, assemblyName);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree syntaxTree, string assemblyName = "TestAssembly")
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

        return CSharpCompilation.Create(assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (List<GraphNode> Nodes, List<GraphEdge> Edges) Execute(string source, string solutionRoot = "")
    {
        var compilation = CreateCompilation(source);
        return new SyntaxPass().Execute(compilation, solutionRoot);
    }

    // ── Metadata key names are exact (kills string mutations) ──

    [Fact]
    public void AbstractClass_HasIsAbstractMetadataKey()
    {
        var (nodes, _) = Execute("namespace A { public abstract class Base { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Base");
        Assert.True(node.Metadata.ContainsKey("isAbstract"));
        Assert.Equal("true", node.Metadata["isAbstract"]);
    }

    [Fact]
    public void StaticClass_HasIsStaticMetadataKey()
    {
        var (nodes, _) = Execute("namespace A { public static class Util { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Util");
        Assert.True(node.Metadata.ContainsKey("isStatic"));
        Assert.Equal("true", node.Metadata["isStatic"]);
    }

    [Fact]
    public void SealedClass_HasIsSealedMetadataKey()
    {
        var (nodes, _) = Execute("namespace A { public sealed class Final { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Final");
        Assert.True(node.Metadata.ContainsKey("isSealed"));
        Assert.Equal("true", node.Metadata["isSealed"]);
    }

    [Fact]
    public void VirtualMethod_HasIsVirtualMetadataKey()
    {
        var (nodes, _) = Execute("namespace A { public class C { public virtual void M() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isVirtual"));
        Assert.Equal("true", method.Metadata["isVirtual"]);
    }

    [Fact]
    public void OverrideMethod_HasIsOverrideMetadataKey()
    {
        var (nodes, _) = Execute(@"
namespace A { 
    public class Base { public virtual void M() { } }
    public class Derived : Base { public override void M() { } }
}");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "M" &&
            n.ContainingTypeId == "A.Derived");
        Assert.True(method.Metadata.ContainsKey("isOverride"));
        Assert.Equal("true", method.Metadata["isOverride"]);
    }

    // ── Type metadata keys ──

    [Fact]
    public void TypeNode_HasTypeKindMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class Foo { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Foo");
        Assert.True(node.Metadata.ContainsKey("typeKind"));
        Assert.Equal("Class", node.Metadata["typeKind"]);
    }

    [Fact]
    public void InterfaceNode_HasTypeKindInterface()
    {
        var (nodes, _) = Execute("namespace A { public interface IFoo { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "IFoo");
        Assert.Equal("Interface", node.Metadata["typeKind"]);
    }

    [Fact]
    public void GenericType_HasGenericArityMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class Gen<T1, T2> { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Gen");
        Assert.True(node.Metadata.ContainsKey("genericArity"));
        Assert.Equal("2", node.Metadata["genericArity"]);
    }

    [Fact]
    public void RecordType_HasIsRecordMetadata()
    {
        var (nodes, _) = Execute("namespace A { public record MyRecord(int X); }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "MyRecord");
        Assert.True(node.Metadata.ContainsKey("isRecord"));
        Assert.Equal("true", node.Metadata["isRecord"]);
    }

    // ── Method metadata keys ──

    [Fact]
    public void AsyncMethod_HasIsAsyncMetadata()
    {
        var source = @"
using System.Threading.Tasks;
namespace A { public class C { public async Task M() { await Task.Delay(1); } } }";
        var (nodes, _) = Execute(source);
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "M");
        Assert.True(method.Metadata.ContainsKey("isAsync"));
        Assert.Equal("true", method.Metadata["isAsync"]);
    }

    [Fact]
    public void ExtensionMethod_HasIsExtensionMetadata()
    {
        var source = @"
namespace A { 
    public static class Ext { 
        public static void DoIt(this string s) { } 
    } 
}";
        var (nodes, _) = Execute(source);
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "DoIt");
        Assert.True(method.Metadata.ContainsKey("isExtension"));
        Assert.Equal("true", method.Metadata["isExtension"]);
    }

    [Fact]
    public void Method_HasReturnTypeMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public int Compute() => 0; } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "Compute");
        Assert.True(method.Metadata.ContainsKey("returnType"));
        Assert.Equal("int", method.Metadata["returnType"]);
    }

    [Fact]
    public void Method_HasParameterCountMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public void Act(int a, string b) { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "Act");
        Assert.True(method.Metadata.ContainsKey("parameterCount"));
        Assert.Equal("2", method.Metadata["parameterCount"]);
    }

    [Fact]
    public void GenericMethod_HasGenericArityMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public T Get<T>() => default; } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "Get");
        Assert.True(method.Metadata.ContainsKey("genericArity"));
        Assert.Equal("1", method.Metadata["genericArity"]);
    }

    // ── Property metadata ──

    [Fact]
    public void Property_HasPropertyTypeMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public string Name { get; set; } } }");
        var prop = nodes.Single(n => n.Kind == NodeKind.Property && n.Name == "Name");
        Assert.True(prop.Metadata.ContainsKey("propertyType"));
        Assert.Equal("string", prop.Metadata["propertyType"]);
    }

    // ── Field metadata ──

    [Fact]
    public void Field_HasFieldTypeMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { private int _x; } }");
        var field = nodes.Single(n => n.Kind == NodeKind.Field && n.Name == "_x");
        Assert.True(field.Metadata.ContainsKey("fieldType"));
        Assert.Equal("int", field.Metadata["fieldType"]);
    }

    [Fact]
    public void ConstField_HasIsConstMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public const int Max = 100; } }");
        var field = nodes.Single(n => n.Kind == NodeKind.Field && n.Name == "Max");
        Assert.True(field.Metadata.ContainsKey("isConst"));
        Assert.Equal("true", field.Metadata["isConst"]);
    }

    [Fact]
    public void ReadOnlyField_HasIsReadOnlyMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class C { public readonly int Val = 5; } }");
        var field = nodes.Single(n => n.Kind == NodeKind.Field && n.Name == "Val");
        Assert.True(field.Metadata.ContainsKey("isReadOnly"));
        Assert.Equal("true", field.Metadata["isReadOnly"]);
    }

    // ── Accessibility mapping (boundary values) ──

    [Theory]
    [InlineData("public class C { }", CodeGraph.Core.Models.Accessibility.Public)]
    [InlineData("internal class C { }", CodeGraph.Core.Models.Accessibility.Internal)]
    public void MapAccessibility_TypeLevel(string source, CodeGraph.Core.Models.Accessibility expected)
    {
        var (nodes, _) = Execute($"namespace A {{ {source} }}");
        var node = nodes.Single(n => n.Kind == NodeKind.Type);
        Assert.Equal(expected, node.Accessibility);
    }

    [Fact]
    public void MapAccessibility_ProtectedMember()
    {
        var (nodes, _) = Execute("namespace A { public class C { protected void M() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method);
        Assert.Equal(Accessibility.Protected, method.Accessibility);
    }

    [Fact]
    public void MapAccessibility_PrivateMember()
    {
        var (nodes, _) = Execute("namespace A { public class C { private void M() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method);
        Assert.Equal(Accessibility.Private, method.Accessibility);
    }

    [Fact]
    public void MapAccessibility_ProtectedInternal()
    {
        var (nodes, _) = Execute("namespace A { public class C { protected internal void M() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method);
        Assert.Equal(Accessibility.ProtectedInternal, method.Accessibility);
    }

    [Fact]
    public void MapAccessibility_PrivateProtected()
    {
        var (nodes, _) = Execute("namespace A { public class C { private protected void M() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method);
        Assert.Equal(Accessibility.PrivateProtected, method.Accessibility);
    }

    // ── Contains edges have Verified confidence ──

    [Fact]
    public void ContainsEdges_HaveVerifiedConfidence()
    {
        var (_, edges) = Execute("namespace N { public class C { public void M() { } } }");

        var containsEdges = edges.Where(e => e.Type == EdgeType.Contains).ToList();
        Assert.NotEmpty(containsEdges);
        Assert.All(containsEdges, e =>
            Assert.Equal(EdgeConfidence.Verified, e.Confidence));
    }

    // ── Doc comment extraction ──

    [Fact]
    public void ExtractDocComment_ReturnsSummaryText()
    {
        var source = @"
namespace A
{
    /// <summary>
    /// Does important work.
    /// </summary>
    public class Documented { }
}";
        var (nodes, _) = Execute(source);
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Documented");
        Assert.NotNull(node.DocComment);
        Assert.Contains("Does important work", node.DocComment);
    }

    [Fact]
    public void ExtractDocComment_NoSummary_ReturnsNull()
    {
        var (nodes, _) = Execute("namespace A { public class NoDoc { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "NoDoc");
        Assert.Null(node.DocComment);
    }

    [Fact]
    public void ExtractDocComment_EmptySummary_ReturnsNull()
    {
        var source = @"
namespace A
{
    /// <summary>
    /// </summary>
    public class EmptyDoc { }
}";
        var (nodes, _) = Execute(source);
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "EmptyDoc");
        Assert.Null(node.DocComment);
    }

    // ── GetRelativePath edge cases ──

    [Fact]
    public void RelativePath_EmptySolutionRoot_ReturnsFilePath()
    {
        var source = "namespace A { public class C { } }";
        var (nodes, _) = Execute(source, solutionRoot: "");
        var typeNode = nodes.Single(n => n.Kind == NodeKind.Type);
        // When solutionRoot is empty, filePath is returned as-is (which is empty for in-memory trees)
        Assert.NotNull(typeNode.FilePath);
    }

    // ── ContainingTypeId and ContainingNamespaceId correctness ──

    [Fact]
    public void MemberNode_HasCorrectContainingTypeId()
    {
        var (nodes, _) = Execute("namespace N { public class MyClass { public void MyMethod() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "MyMethod");
        Assert.Equal("N.MyClass", method.ContainingTypeId);
    }

    [Fact]
    public void MemberNode_HasCorrectContainingNamespaceId()
    {
        var (nodes, _) = Execute("namespace N { public class MyClass { public void MyMethod() { } } }");
        var method = nodes.Single(n => n.Kind == NodeKind.Method && n.Name == "MyMethod");
        Assert.Equal("N", method.ContainingNamespaceId);
    }

    [Fact]
    public void TypeInGlobalNamespace_HasNullContainingNamespaceId()
    {
        var (nodes, _) = Execute("public class GlobalClass { }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "GlobalClass");
        Assert.Null(node.ContainingNamespaceId);
    }

    [Fact]
    public void TopLevelType_HasNullContainingTypeId()
    {
        var (nodes, _) = Execute("namespace N { public class TopLevel { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "TopLevel");
        Assert.Null(node.ContainingTypeId);
    }

    // ── Non-abstract class should NOT have isAbstract ──

    [Fact]
    public void ConcreteClass_DoesNotHaveIsAbstractMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class Concrete { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Concrete");
        Assert.False(node.Metadata.ContainsKey("isAbstract"));
    }

    [Fact]
    public void NonStaticClass_DoesNotHaveIsStaticMetadata()
    {
        var (nodes, _) = Execute("namespace A { public class Normal { } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Normal");
        Assert.False(node.Metadata.ContainsKey("isStatic"));
    }

    // ── Node.Kind is correct for each member type ──

    [Fact]
    public void Constructor_HasConstructorKind()
    {
        var (nodes, _) = Execute("namespace A { public class C { public C() { } } }");
        var ctor = nodes.Single(n => n.Kind == NodeKind.Constructor);
        Assert.Equal(NodeKind.Constructor, ctor.Kind);
    }

    [Fact]
    public void Event_HasEventKind()
    {
        var (nodes, _) = Execute("namespace A { public class C { public event System.Action Ev { add {} remove {} } } }");
        var evt = nodes.Single(n => n.Kind == NodeKind.Event);
        Assert.Equal(NodeKind.Event, evt.Kind);
    }

    // ── Enum type has correct typeKind ──

    [Fact]
    public void EnumType_HasTypeKindEnum()
    {
        var (nodes, _) = Execute("namespace A { public enum Color { Red, Green } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Color");
        Assert.Equal("Enum", node.Metadata["typeKind"]);
    }

    // ── Struct has correct typeKind ──

    [Fact]
    public void StructType_HasTypeKindStruct()
    {
        var (nodes, _) = Execute("namespace A { public struct Point { public int X; } }");
        var node = nodes.Single(n => n.Kind == NodeKind.Type && n.Name == "Point");
        Assert.Equal("Struct", node.Metadata["typeKind"]);
    }

    [Fact]
    public void RelativePath_WithKnownTreePath_ReturnsExactRelativePath()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace A { public class C { } }", path: @"D:\repo\src\MyFile.cs");
        var compilation = CreateCompilation(tree);
        var (nodes, _) = new SyntaxPass().Execute(compilation, @"D:\repo");

        var expectedPath = Path.Combine("src", "MyFile.cs");
        var typeNode = nodes.Single(n => n.Id == "A.C");
        var namespaceNode = nodes.Single(n => n.Id == "A");

        Assert.Equal(expectedPath, typeNode.FilePath);
        Assert.Equal(expectedPath, namespaceNode.FilePath);
    }

    [Fact]
    public void EmptySolutionRoot_PreservesAbsoluteFilePath()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace A { public class C { } }", path: @"D:\repo\src\MyFile.cs");
        var compilation = CreateCompilation(tree);
        var (nodes, _) = new SyntaxPass().Execute(compilation, string.Empty);

        var typeNode = nodes.Single(n => n.Id == "A.C");
        Assert.Equal(@"D:\repo\src\MyFile.cs", typeNode.FilePath);
    }

    [Fact]
    public void MapAccessibility_NotApplicable_DefaultsToPrivate()
    {
        var mapped = SyntaxPass.MapAccessibility(Microsoft.CodeAnalysis.Accessibility.NotApplicable);
        Assert.Equal(Accessibility.Private, mapped);
    }
}
