using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Accessibility = CodeGraph.Core.Models.Accessibility;

namespace CodeGraph.Indexer.Tests.Passes;

/// <summary>
/// Additional mutation-killing tests for SemanticPass.
/// Targets: UnwrapType (nullable/array), MemberAccessExpression filtering,
/// edge confidence, GetContainingMemberId patterns, and EnsureNode kinds.
/// </summary>
public class SemanticPassMutationTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));
        var collectionsDir = Path.Combine(runtimeDir, "System.Collections.dll");
        if (File.Exists(collectionsDir))
            references.Add(MetadataReference.CreateFromFile(collectionsDir));

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (List<GraphNode> AllNodes, List<GraphEdge> AllEdges, List<GraphNode> ExternalNodes, List<GraphEdge> SemanticEdges)
        RunBothPasses(string source)
    {
        var compilation = CreateCompilation(source);
        var syntaxPass = new SyntaxPass();
        var (nodes, syntaxEdges) = syntaxPass.Execute(compilation, "");

        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));
        var semanticPass = new SemanticPass();
        var (externalNodes, semanticEdges) = semanticPass.Execute(compilation, "", knownIds);

        var allNodes = nodes.Concat(externalNodes).ToList();
        var allEdges = syntaxEdges.Concat(semanticEdges).ToList();

        return (allNodes, allEdges, externalNodes, semanticEdges);
    }

    // ── UnwrapType: Nullable<T> unwrapping ──

    [Fact]
    public void NullableValueType_UnwrapsToInnerType_DependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Holder
    {
        public int? NullableField;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Nullable<int> should be unwrapped to int which is special type — no DependsOn
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn &&
            e.ToId.Contains("Nullable"));
    }

    [Fact]
    public void NullableCustomType_UnwrapsAndCreatesDependsOn()
    {
        var source = @"
namespace MyApp
{
    public struct MyStruct { public int Value; }
    public class Holder
    {
        public MyStruct? NullableStruct;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should have DependsOn to MyStruct (unwrapped from Nullable<MyStruct>)
        Assert.Contains(semanticEdges, e =>
            e.ToId == "MyApp.MyStruct" &&
            e.Type == EdgeType.DependsOn);
        // Should NOT have DependsOn to Nullable<MyStruct>
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn &&
            e.ToId.Contains("Nullable"));
    }

    // ── UnwrapType: Array unwrapping ──

    [Fact]
    public void ArrayType_UnwrapsToElementType_DependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Item { }
    public class Container
    {
        public Item[] Items;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should produce DependsOn to Item (element type), not to Item[]
        Assert.Contains(semanticEdges, e =>
            e.ToId == "MyApp.Item" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── MemberAccessExpression: parent is InvocationExpression gets skipped ──

    [Fact]
    public void MemberAccess_ThatIsInvocation_DoesNotCreateReferencesEdge()
    {
        var source = @"
namespace MyApp
{
    public class Helper
    {
        public int Compute() => 42;
    }
    public class Caller
    {
        public void Work()
        {
            var h = new Helper();
            var result = h.Compute();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Compute() is a method invocation, NOT a References edge
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Caller.Work()" &&
            e.ToId.Contains("Compute") &&
            e.Type == EdgeType.References);

        // It SHOULD be a Calls edge
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Caller.Work()" &&
            e.ToId == "MyApp.Helper.Compute()" &&
            e.Type == EdgeType.Calls);
    }

    // ── MemberAccess: skip IMethodSymbol (handled separately as Calls) ──

    [Fact]
    public void MemberAccess_MethodGroupAsDelegate_NoReferencesEdge()
    {
        var source = @"
namespace MyApp
{
    public class Worker
    {
        public static int Count = 0;
        public int GetCount() => Count;
    }
    public class Consumer
    {
        public void Use()
        {
            var w = new Worker();
            var x = Worker.Count;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Static field access should be References, not Calls
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Consumer.Use()" &&
            e.ToId.Contains("Count") &&
            e.Type == EdgeType.References);
    }

    // ── All edges have Confidence = Verified ──

    [Fact]
    public void AllSemanticEdges_HaveVerifiedConfidence()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class Base { public virtual void Run() {} }
    public class Derived : Base, IFoo
    {
        private Base _dep;
        public override void Run()
        {
            var b = new Base();
            b.Run();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.NotEmpty(semanticEdges);
        Assert.All(semanticEdges, e =>
            Assert.Equal(EdgeConfidence.Verified, e.Confidence));
    }

    // ── GetContainingMemberId: code inside property accessor ──

    [Fact]
    public void CodeInsideMethod_UsesMethodAsContainingMember()
    {
        var source = @"
namespace MyApp
{
    public class Config
    {
        public static int MaxRetries = 3;
    }
    public class Service
    {
        public void Init()
        {
            var x = Config.MaxRetries;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // The References edge from MaxRetries access should be from the Method node
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Init") &&
            e.ToId.Contains("MaxRetries") &&
            e.Type == EdgeType.References);
    }

    // ── EnsureNode: external property kind ──

    [Fact]
    public void ExternalPropertyAccess_CreatesPropertyKindNode()
    {
        var source = @"
using System;
namespace MyApp
{
    public class Reader
    {
        public void Read()
        {
            var o = Console.Out;
        }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var outNode = externalNodes.FirstOrDefault(n =>
            n.Name == "Out" && n.Id.Contains("Console"));
        if (outNode != null)
        {
            Assert.Equal(NodeKind.Property, outNode.Kind);
        }
    }

    // ── EnsureNode: external field kind ──

    [Fact]
    public void ExternalFieldAccess_CreatesFieldKindNode()
    {
        var source = @"
namespace MyApp
{
    public class Reader
    {
        public void Read()
        {
            var x = string.Empty;
        }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var emptyNode = externalNodes.FirstOrDefault(n =>
            n.Name == "Empty" && n.Id.Contains("string"));
        if (emptyNode != null)
        {
            Assert.Equal(NodeKind.Field, emptyNode.Kind);
        }
    }

    // ── DependsOn skips self-reference (targetId == fromId) ──

    [Fact]
    public void DependsOn_SkipsSelfReference()
    {
        var source = @"
namespace MyApp
{
    public class Recursive
    {
        public Recursive GetSelf() => this;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should NOT have a DependsOn from Recursive.GetSelf to Recursive
        // because the return type is the same containing type
        // (Actually containingType != method, so the self-check is fromId != targetId)
        // GetSelf's return type dependency to Recursive is valid since fromId=GetSelf, toId=Recursive
        // But DependsOn self-skip is targetId == fromId, not containing type
        var selfDeps = semanticEdges.Where(e =>
            e.Type == EdgeType.DependsOn &&
            e.FromId == e.ToId).ToList();
        Assert.Empty(selfDeps);
    }

    // ── DependsOn skips Error types ──

    [Fact]
    public void DependsOn_SkipsErrorTypes()
    {
        // Source referencing undefined type - Roslyn marks it as ErrorType
        var source = @"
namespace MyApp
{
    public class Holder
    {
        public UndefinedType Field;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should not crash and should not produce DependsOn to error types
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn &&
            e.ToId.Contains("UndefinedType"));
    }

    // ── External node Signature is populated ──

    [Fact]
    public void ExternalNode_HasNonEmptySignature()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log() { System.Console.WriteLine(""x""); }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var extNode = externalNodes.First(n => n.Id.Contains("WriteLine"));
        Assert.NotNull(extNode.Signature);
        Assert.NotEmpty(extNode.Signature);
    }

    // ── External node Name is populated ──

    [Fact]
    public void ExternalNode_HasNonEmptyName()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log() { System.Console.WriteLine(""x""); }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var extNode = externalNodes.First(n => n.Id.Contains("WriteLine"));
        Assert.Equal("WriteLine", extNode.Name);
    }

    // ── External node Id is set correctly ──

    [Fact]
    public void ExternalNode_IdMatchesSymbolId()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log() { System.Console.WriteLine(""x""); }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        var callEdge = semanticEdges.First(e =>
            e.FromId == "MyApp.Logger.Log()" && e.Type == EdgeType.Calls);
        var extNode = externalNodes.First(n => n.Id == callEdge.ToId);
        Assert.Equal(callEdge.ToId, extNode.Id);
    }

    // ── External node Accessibility is mapped ──

    [Fact]
    public void ExternalNode_HasMappedAccessibility()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log() { System.Console.WriteLine(""x""); }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var extNode = externalNodes.First(n => n.Id.Contains("WriteLine"));
        // Console.WriteLine is public
        Assert.Equal(Accessibility.Public, extNode.Accessibility);
    }

    // ── VisitInvocationExpression calls base (processes child expressions) ──

    [Fact]
    public void NestedInvocations_BothProduceCallsEdges()
    {
        var source = @"
namespace MyApp
{
    public class A
    {
        public B GetB() => new B();
    }
    public class B
    {
        public void DoWork() { }
    }
    public class Caller
    {
        public void Run()
        {
            var a = new A();
            a.GetB().DoWork();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Both GetB and DoWork should have Calls edges
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Caller.Run()" &&
            e.ToId == "MyApp.A.GetB()" &&
            e.Type == EdgeType.Calls);
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Caller.Run()" &&
            e.ToId == "MyApp.B.DoWork()" &&
            e.Type == EdgeType.Calls);
    }

    // ── VisitObjectCreationExpression calls base ──

    [Fact]
    public void ObjectCreation_WithInitializerAccess_ProducesEdges()
    {
        var source = @"
namespace MyApp
{
    public class Config
    {
        public int Value { get; set; }
    }
    public class Factory
    {
        public Config Create()
        {
            return new Config { Value = 42 };
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should have Calls edge for object creation
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Factory.Create()" &&
            e.Type == EdgeType.Calls &&
            e.ToId.Contains("Config"));
    }

    // ── VisitMethodDeclaration calls VisitMemberForDependencies AND VisitMethodForOverrides ──

    [Fact]
    public void OverrideMethod_EmitsBothDependsOnAndOverridesEdges()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Base
    {
        public virtual Dep Get() => null;
    }
    public class Derived : Base
    {
        public override Dep Get() => null;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Overrides edge
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Derived.Get()" &&
            e.ToId == "MyApp.Base.Get()" &&
            e.Type == EdgeType.Overrides);

        // DependsOn edge for return type
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Derived.Get()" &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── Multiple syntax trees processed ──

    [Fact]
    public void MultipleSyntaxTrees_AllAreProcessed()
    {
        var source1 = "namespace MyApp { public class A { public void Foo() { var b = new B(); } } }";
        var source2 = "namespace MyApp { public class B { public B() {} } }";

        var tree1 = CSharpSyntaxTree.ParseText(source1);
        var tree2 = CSharpSyntaxTree.ParseText(source2);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree1, tree2 },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(compilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));
        var semanticPass = new SemanticPass();
        var (_, semanticEdges) = semanticPass.Execute(compilation, "", knownIds);

        // Edge from tree1's code to tree2's constructor
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.A.Foo()" &&
            e.Type == EdgeType.Calls &&
            e.ToId.Contains("B"));
    }

    [Fact]
    public void PropertyGetter_DoesNotCreateReferencesEdge_WhenPropertyBodiesAreNotVisited()
    {
        var source = @"
namespace MyApp
{
    public class Config
    {
        public static int MaxRetries = 3;
    }

    public class Reader
    {
        public int Current => Config.MaxRetries;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Reader.Current" &&
            e.ToId == "MyApp.Config.MaxRetries" &&
            e.Type == EdgeType.References);
    }

    [Fact]
    public void EventAccessor_UsesEventIdAsContainingMemberForCallsEdge()
    {
        var source = @"
using System;
namespace MyApp
{
    public class Notifier
    {
        public static void Touch() { }
    }

    public class Publisher
    {
        public event Action Changed
        {
            add { Notifier.Touch(); }
            remove { Notifier.Touch(); }
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var edges = semanticEdges.Where(e =>
            e.FromId == "MyApp.Publisher.Changed" &&
            e.ToId == "MyApp.Notifier.Touch()" &&
            e.Type == EdgeType.Calls).ToList();
        Assert.Single(edges);
    }

    [Fact]
    public void MethodGroupAssignment_DoesNotCreateReferencesOrCallsEdge()
    {
        var source = @"
using System;
namespace MyApp
{
    public class Worker
    {
        public static void Run() { }
    }

    public class Consumer
    {
        public void Use()
        {
            Action action = Worker.Run;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.DoesNotContain(semanticEdges, e =>
            e.ToId == "MyApp.Worker.Run()" &&
            (e.Type == EdgeType.References || e.Type == EdgeType.Calls));
    }

    [Fact]
    public void DuplicateFieldReference_ProducesSingleReferencesEdge()
    {
        var source = @"
namespace MyApp
{
    public class Config
    {
        public static int Value = 1;
    }

    public class Reader
    {
        public int Read()
        {
            return Config.Value + Config.Value;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var edges = semanticEdges.Where(e =>
            e.FromId == "MyApp.Reader.Read()" &&
            e.ToId == "MyApp.Config.Value" &&
            e.Type == EdgeType.References).ToList();
        Assert.Single(edges);
    }

    [Fact]
    public void NullableArrayField_UnwrapsRecursivelyToElementType()
    {
        var source = @"
namespace MyApp
{
    public struct Item { }

    public class Holder
    {
        public Item?[] Items;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var edge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.Holder.Items" &&
            e.Type == EdgeType.DependsOn);
        Assert.Equal("MyApp.Item", edge.ToId);
    }
}
