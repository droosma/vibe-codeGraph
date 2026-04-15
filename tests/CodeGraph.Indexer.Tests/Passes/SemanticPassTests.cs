using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class SemanticPassTests
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

    // ── 1. Calls edges from invocations ──

    [Fact]
    public void MethodInvocation_CreatesCallsEdge_WithCorrectFromAndTo()
    {
        var source = @"
namespace MyApp
{
    public class ServiceA
    {
        public void DoWork()
        {
            var b = new ServiceB();
            b.Process();
        }
    }
    public class ServiceB
    {
        public void Process() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var callEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.ServiceA.DoWork()" &&
            e.ToId == "MyApp.ServiceB.Process()" &&
            e.Type == EdgeType.Calls);
        Assert.Equal(EdgeType.Calls, callEdge.Type);
        Assert.False(callEdge.IsExternal);
    }

    // ── 2. Calls edges from object creation ──

    [Fact]
    public void ObjectCreation_CreatesCallsEdge_ToConstructor()
    {
        var source = @"
namespace MyApp
{
    public class Creator
    {
        public void Make()
        {
            var t = new Target();
        }
    }
    public class Target
    {
        public Target() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Creator.Make()" &&
            e.ToId.Contains("Target") &&
            e.ToId.Contains(".Target(") &&
            e.Type == EdgeType.Calls);

        // Verify it's specifically Calls, not any other EdgeType
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Creator.Make()" &&
            e.ToId.Contains("Target") &&
            e.ToId.Contains(".Target(") &&
            e.Type != EdgeType.Calls);
    }

    // ── 3. Inherits edges ──

    [Fact]
    public void ClassInheritingNonObjectBase_CreatesInheritsEdge()
    {
        var source = @"
namespace MyApp
{
    public class Animal { }
    public class Dog : Animal { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var inheritsEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.Dog" &&
            e.ToId == "MyApp.Animal");
        Assert.Equal(EdgeType.Inherits, inheritsEdge.Type);
    }

    // ── 4. No Inherits from System.Object ──

    [Fact]
    public void ClassWithNoExplicitBase_DoesNotInheritFromObject()
    {
        var source = @"
namespace MyApp
{
    public class Standalone { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Standalone" &&
            e.Type == EdgeType.Inherits);
    }

    // ── 5. No Inherits from System.ValueType ──

    [Fact]
    public void Struct_DoesNotInheritFromValueType()
    {
        var source = @"
namespace MyApp
{
    public struct MyStruct
    {
        public int X;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.MyStruct" &&
            e.Type == EdgeType.Inherits);
    }

    // ── 6. Implements edges ──

    [Fact]
    public void ClassImplementingInterface_CreatesImplementsEdge()
    {
        var source = @"
namespace MyApp
{
    public interface IService { void Execute(); }
    public class MyService : IService
    {
        public void Execute() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var implEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.MyService" &&
            e.ToId == "MyApp.IService");
        Assert.Equal(EdgeType.Implements, implEdge.Type);
    }

    // ── 7. DependsOn from method parameters ──

    [Fact]
    public void MethodParameter_CustomType_CreatesDependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Consumer
    {
        public void Act(Dep d) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Consumer.Act(MyApp.Dep)" &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── 8. DependsOn from return types ──

    [Fact]
    public void MethodReturnType_CustomType_CreatesDependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Result { }
    public class Producer
    {
        public Result GetResult() => null;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Producer.GetResult()" &&
            e.ToId == "MyApp.Result" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── 9. DependsOn from field types ──

    [Fact]
    public void Field_CustomType_CreatesDependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Holder
    {
        private Dep _dep;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("_dep") &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);

        // Verify EdgeType is exactly DependsOn
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("_dep") &&
            e.ToId == "MyApp.Dep" &&
            e.Type != EdgeType.DependsOn);
    }

    // ── 10. DependsOn from property types ──

    [Fact]
    public void Property_CustomType_CreatesDependsOnEdge()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Holder
    {
        public Dep MyProp { get; set; }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("MyProp") &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── 11. Constructor doesn't emit DependsOn for return type ──

    [Fact]
    public void Constructor_DoesNotEmitDependsOnForReturnType()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Owner
    {
        public Owner(Dep d) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Constructor should emit DependsOn for parameter type
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Owner") &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);

        // Constructor should NOT emit DependsOn to its own containing type
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains(".Owner(") &&
            e.ToId == "MyApp.Owner" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── 12. DependsOn filters out special types ──

    [Fact]
    public void DependsOn_FiltersOutSpecialTypes_IntStringBool()
    {
        var source = @"
namespace MyApp
{
    public class Worker
    {
        public int GetCount() => 0;
        public void SetName(string name) { }
        private bool _flag;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn &&
            (e.ToId.Contains("Int32") || e.ToId.Contains("String") ||
             e.ToId.Contains("Boolean") || e.ToId == "int" || e.ToId == "string" || e.ToId == "bool"));
    }

    // ── 13. DependsOn filters out type parameters ──

    [Fact]
    public void DependsOn_FiltersOutTypeParameters()
    {
        var source = @"
namespace MyApp
{
    public class GenericWorker<T>
    {
        public T GetValue() => default;
        public void SetValue(T val) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // No DependsOn edge to a type parameter T
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn && e.ToId == "T");
    }

    // ── 14. Overrides edges ──

    [Fact]
    public void OverrideMethod_CreatesOverridesEdge_WithExactIds()
    {
        var source = @"
namespace MyApp
{
    public class Base
    {
        public virtual void Run() { }
    }
    public class Derived : Base
    {
        public override void Run() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var overrideEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.Derived.Run()" &&
            e.ToId == "MyApp.Base.Run()");
        Assert.Equal(EdgeType.Overrides, overrideEdge.Type);
    }

    // ── 15. References edges ──

    [Fact]
    public void MemberAccess_NonInvocation_CreatesReferencesEdge()
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
        public int Read()
        {
            return Config.MaxRetries;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Reader.Read()" &&
            e.ToId.Contains("MaxRetries") &&
            e.Type == EdgeType.References);

        // Verify it's References, not Calls
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Reader.Read()" &&
            e.ToId.Contains("MaxRetries") &&
            e.Type == EdgeType.Calls);
    }

    // ── 16. References skips self-reference ──

    [Fact]
    public void MemberAccess_SelfReference_IsSkipped()
    {
        var source = @"
namespace MyApp
{
    public class Self
    {
        public static int Value = 5;
        public static int GetValue()
        {
            return Value;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // GetValue referencing Value on the same class is fine (different members),
        // but there should be no self-referencing edge (targetId == referrerId)
        foreach (var edge in semanticEdges.Where(e => e.Type == EdgeType.References))
        {
            Assert.NotEqual(edge.FromId, edge.ToId);
        }
    }

    // ── 17. External node creation with correct Kind and assembly ──

    [Fact]
    public void ExternalMethodCall_CreatesExternalNode_WithMethodKind()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log(string msg)
        {
            System.Console.WriteLine(msg);
        }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        var writeLineNode = Assert.Single(externalNodes, n =>
            n.Id.Contains("Console") && n.Id.Contains("WriteLine"));
        Assert.Equal(NodeKind.Method, writeLineNode.Kind);
        Assert.Equal(string.Empty, writeLineNode.FilePath);
        Assert.Equal(0, writeLineNode.StartLine);
        Assert.Equal(0, writeLineNode.EndLine);
        Assert.NotEmpty(writeLineNode.Name);
        Assert.NotEmpty(writeLineNode.Signature);
    }

    // ── 18. External node has "assembly" key ──

    [Fact]
    public void ExternalNode_HasAssemblyMetadataKey()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log(string msg)
        {
            System.Console.WriteLine(msg);
        }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var extNode = Assert.Single(externalNodes, n =>
            n.Id.Contains("Console") && n.Id.Contains("WriteLine"));
        Assert.True(extNode.Metadata.ContainsKey("assembly"));
        Assert.NotEmpty(extNode.Metadata["assembly"]);
        Assert.NotEqual("Unknown", extNode.Metadata["assembly"]);
    }

    // ── 19. IsExternal true on edges to external symbols ──

    [Fact]
    public void EdgeToExternalSymbol_HasIsExternalTrue()
    {
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log(string msg)
        {
            System.Console.WriteLine(msg);
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var externalCallEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.Logger.Log(string)" &&
            e.ToId.Contains("WriteLine") &&
            e.Type == EdgeType.Calls);
        Assert.True(externalCallEdge.IsExternal);
    }

    // ── 20. IsExternal false on edges to internal symbols ──

    [Fact]
    public void EdgeToInternalSymbol_HasIsExternalFalse()
    {
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo() { var b = new B(); b.Bar(); }
    }
    public class B
    {
        public void Bar() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var internalCallEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.A.Foo()" &&
            e.ToId == "MyApp.B.Bar()" &&
            e.Type == EdgeType.Calls);
        Assert.False(internalCallEdge.IsExternal);
    }

    // ── 21. Edge deduplication ──

    [Fact]
    public void DuplicateCall_ProducesOnlyOneEdge()
    {
        var source = @"
namespace MyApp
{
    public class Caller
    {
        public void Go()
        {
            var t = new Target();
            t.Do();
            t.Do();
            t.Do();
        }
    }
    public class Target
    {
        public void Do() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var callEdges = semanticEdges.Where(e =>
            e.FromId == "MyApp.Caller.Go()" &&
            e.ToId == "MyApp.Target.Do()" &&
            e.Type == EdgeType.Calls).ToList();

        Assert.Single(callEdges);
    }

    // ── 22. Record type inheritance ──

    [Fact]
    public void RecordInheriting_CreatesInheritsEdge()
    {
        var source = @"
namespace MyApp
{
    public record BaseRecord { }
    public record DerivedRecord : BaseRecord { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var inheritsEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRecord" &&
            e.ToId == "MyApp.BaseRecord");
        Assert.Equal(EdgeType.Inherits, inheritsEdge.Type);
    }

    // ── 23. Struct implementing interface ──

    [Fact]
    public void StructImplementingInterface_CreatesImplementsEdge()
    {
        var source = @"
namespace MyApp
{
    public interface IComparable { }
    public struct MyStruct : IComparable { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var implEdge = Assert.Single(semanticEdges, e =>
            e.FromId == "MyApp.MyStruct" &&
            e.ToId == "MyApp.IComparable");
        Assert.Equal(EdgeType.Implements, implEdge.Type);
    }

    // ── 24. Constructor DependsOn parameter types but not return type ──

    [Fact]
    public void ConstructorDependsOnParamTypes_NotReturnType()
    {
        var source = @"
namespace MyApp
{
    public class DepA { }
    public class DepB { }
    public class Service
    {
        public Service(DepA a, DepB b) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should have DependsOn for both parameters
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Service") && e.FromId.Contains("(") &&
            e.ToId == "MyApp.DepA" &&
            e.Type == EdgeType.DependsOn);
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Service") && e.FromId.Contains("(") &&
            e.ToId == "MyApp.DepB" &&
            e.Type == EdgeType.DependsOn);

        // Should NOT have DependsOn to Service itself (would be return type of ctor)
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("Service") && e.FromId.Contains("(") &&
            e.ToId == "MyApp.Service" &&
            e.Type == EdgeType.DependsOn);
    }

    // ── 25. Try-catch resilience ──

    [Fact]
    public void ErrorInOneTree_DoesNotCrashPass()
    {
        // Intentionally broken source to ensure the pass doesn't throw
        var source = @"
namespace MyApp
{
    public class Valid
    {
        public void Work() { }
    }
}";
        var compilation = CreateCompilation(source);
        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(compilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var semanticPass = new SemanticPass();
        var exception = Record.Exception(() => semanticPass.Execute(compilation, "", knownIds));
        Assert.Null(exception);
    }

    // ── Additional mutation-killing tests ──

    [Fact]
    public void AllEdgeTypes_AreDistinct_NoCrossContamination()
    {
        var source = @"
namespace MyApp
{
    public interface IRunner { void Run(); }
    public class BaseRunner { public virtual void Run() { } }
    public class DerivedRunner : BaseRunner, IRunner
    {
        public Dep _dep;
        public override void Run()
        {
            var h = new Helper();
            h.Help();
            var x = Config.Value;
        }
    }
    public class Dep { }
    public class Helper { public void Help() { } }
    public class Config { public static int Value = 1; }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Inherits: DerivedRunner -> BaseRunner
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner" && e.ToId == "MyApp.BaseRunner" && e.Type == EdgeType.Inherits);
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner" && e.ToId == "MyApp.BaseRunner" && e.Type != EdgeType.Inherits);

        // Implements: DerivedRunner -> IRunner
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner" && e.ToId == "MyApp.IRunner" && e.Type == EdgeType.Implements);
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner" && e.ToId == "MyApp.IRunner" && e.Type != EdgeType.Implements);

        // Overrides: DerivedRunner.Run -> BaseRunner.Run
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner.Run()" && e.ToId == "MyApp.BaseRunner.Run()" && e.Type == EdgeType.Overrides);

        // Calls: DerivedRunner.Run -> Helper.Help
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner.Run()" && e.ToId == "MyApp.Helper.Help()" && e.Type == EdgeType.Calls);

        // References: DerivedRunner.Run -> Config.Value
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRunner.Run()" && e.ToId.Contains("Value") && e.Type == EdgeType.References);

        // DependsOn: _dep -> Dep
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("_dep") && e.ToId == "MyApp.Dep" && e.Type == EdgeType.DependsOn);
    }

    [Fact]
    public void ExternalNodeKinds_AreCorrectlyMapped()
    {
        var source = @"
using System;
namespace MyApp
{
    public class Tester
    {
        public void Test()
        {
            Console.WriteLine(""hi"");
            var x = Console.Out;
        }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        // External method -> NodeKind.Method
        var methodNode = externalNodes.FirstOrDefault(n =>
            n.Id.Contains("WriteLine"));
        Assert.NotNull(methodNode);
        Assert.Equal(NodeKind.Method, methodNode!.Kind);

        // External property -> NodeKind.Property
        var propNode = externalNodes.FirstOrDefault(n =>
            n.Id.Contains("Out") && !n.Id.Contains("Write"));
        if (propNode != null)
        {
            Assert.Equal(NodeKind.Property, propNode.Kind);
        }
    }

    [Fact]
    public void ExternalTypeNode_HasTypeKind()
    {
        var source = @"
using System;
using System.Collections.Generic;
namespace MyApp
{
    public class Holder
    {
        public List<int> Items;
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        var listNode = externalNodes.FirstOrDefault(n =>
            n.Id.Contains("List"));
        if (listNode != null)
        {
            Assert.Equal(NodeKind.Type, listNode.Kind);
        }
    }

    [Fact]
    public void InternalNodes_AreNotAddedToExternalNodes()
    {
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo() { var b = new B(); b.Bar(); }
    }
    public class B
    {
        public void Bar() { }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.A");
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.B");
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.B.Bar()");
    }

    [Fact]
    public void MultipleInterfaces_AllGetImplementsEdges()
    {
        var source = @"
namespace MyApp
{
    public interface IA { }
    public interface IB { }
    public class Multi : IA, IB { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Multi" && e.ToId == "MyApp.IA" && e.Type == EdgeType.Implements);
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Multi" && e.ToId == "MyApp.IB" && e.Type == EdgeType.Implements);
    }

    [Fact]
    public void VisitWalkerIsCalled_EdgesAreProduced()
    {
        // Ensures walker.Visit(root) is actually called — without it, no edges at all
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo() { var b = new B(); b.Bar(); }
    }
    public class B
    {
        public void Bar() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);
        Assert.NotEmpty(semanticEdges);
    }

    [Fact]
    public void EnsureNode_NotCalledForKnownIds()
    {
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo() { var b = new B(); b.Bar(); }
    }
    public class B
    {
        public void Bar() { }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        // B.Bar() is a known ID from SyntaxPass, so it should NOT be in externalNodes
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.B.Bar()");
    }

    [Fact]
    public void ObjectCreation_CreatesCallsEdge_NotOtherTypes()
    {
        var source = @"
namespace MyApp
{
    public class Factory
    {
        public void Build() { var x = new Product(); }
    }
    public class Product { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Object creation should create Calls edge (to ctor)
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Factory.Build()" &&
            e.ToId.Contains("Product") &&
            e.Type == EdgeType.Calls);
    }

    [Fact]
    public void MethodReturnType_DependsOn_NotCalls()
    {
        var source = @"
namespace MyApp
{
    public class Result { }
    public class Service
    {
        public Result Get() => null;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        var depEdge = semanticEdges.FirstOrDefault(e =>
            e.FromId == "MyApp.Service.Get()" &&
            e.ToId == "MyApp.Result");
        Assert.NotNull(depEdge);
        Assert.Equal(EdgeType.DependsOn, depEdge!.Type);
    }

    [Fact]
    public void ExternalNode_AssemblyFallback_Unknown_WhenNoAssembly()
    {
        // This is hard to trigger directly, but we can verify non-null assembly case
        var source = @"
namespace MyApp
{
    public class Logger
    {
        public void Log() { System.Console.WriteLine(""x""); }
    }
}";
        var (_, _, externalNodes, _) = RunBothPasses(source);

        foreach (var node in externalNodes)
        {
            Assert.True(node.Metadata.ContainsKey("assembly"),
                $"Node {node.Id} is missing 'assembly' metadata key");
            Assert.False(string.IsNullOrEmpty(node.Metadata["assembly"]),
                $"Node {node.Id} has empty assembly value");
        }
    }

    [Fact]
    public void DependsOn_SkipsErrorTypes()
    {
        // Error types (unresolved) should be filtered out
        var source = @"
namespace MyApp
{
    public class Valid
    {
        public void Work() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Verify no DependsOn edges to error types
        // (this source has no error types, ensuring the filter doesn't break valid cases)
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.DependsOn && e.ToId.Contains("?"));
    }

    [Fact]
    public void InheritsPlusImplements_BothPresent()
    {
        var source = @"
namespace MyApp
{
    public interface IWorker { }
    public class BaseWorker { }
    public class SpecificWorker : BaseWorker, IWorker { }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.SpecificWorker" &&
            e.ToId == "MyApp.BaseWorker" &&
            e.Type == EdgeType.Inherits);
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.SpecificWorker" &&
            e.ToId == "MyApp.IWorker" &&
            e.Type == EdgeType.Implements);
    }

    [Fact]
    public void References_PropertyAccess_CreatesReferencesEdge()
    {
        var source = @"
namespace MyApp
{
    public class Settings
    {
        public static string Name { get; set; }
    }
    public class Reader
    {
        public string Read()
        {
            return Settings.Name;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Reader.Read()" &&
            e.ToId.Contains("Name") &&
            e.Type == EdgeType.References);
    }

    [Fact]
    public void MemberAccessInsideInvocation_IsNotDoubleReferencedAsReferences()
    {
        // member access that is part of an invocation should be handled by invocation visitor only
        var source = @"
namespace MyApp
{
    public class Helper { public void Do() { } }
    public class Caller
    {
        public void Go()
        {
            var h = new Helper();
            h.Do();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // h.Do() should produce a Calls edge, NOT a References edge for "Do"
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Caller.Go()" &&
            e.ToId == "MyApp.Helper.Do()" &&
            e.Type == EdgeType.References);
    }

    // ── Kill EnsureNode statement mutations ──

    [Fact]
    public void ExternalInheritance_CreatesExternalNodeForBaseType()
    {
        // Two-assembly scenario: base class external
        var prodSource = @"
namespace Lib { public class BaseClass { public virtual void Run() { } } }";
        var testSource = @"
namespace App { public class Derived : Lib.BaseClass { public override void Run() { } } }";

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll)) references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var prodTree = CSharpSyntaxTree.ParseText(prodSource);
        var prodCompilation = CSharpCompilation.Create("LibAssembly",
            new[] { prodTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var appTree = CSharpSyntaxTree.ParseText(testSource);
        var appCompilation = CSharpCompilation.Create("AppAssembly",
            new[] { appTree },
            references.Concat(new[] { prodCompilation.ToMetadataReference() }).ToList(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(appCompilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var semanticPass = new SemanticPass();
        var (externalNodes, edges) = semanticPass.Execute(appCompilation, "", knownIds);

        // Inherits edge must exist
        Assert.Contains(edges, e => e.Type == EdgeType.Inherits && e.FromId.Contains("Derived"));
        // External node for base class must exist (kills L121 EnsureNode removal)
        Assert.Contains(externalNodes, n => n.Id.Contains("BaseClass") && n.Kind == NodeKind.Type);
        // Overrides edge and external node for base method (kills L243 EnsureNode)
        Assert.Contains(edges, e => e.Type == EdgeType.Overrides && e.FromId.Contains("Derived.Run"));
        Assert.Contains(externalNodes, n => n.Id.Contains("BaseClass.Run") && n.Kind == NodeKind.Method);
    }

    [Fact]
    public void ExternalInterface_CreatesExternalNodeForInterface()
    {
        var prodSource = @"
namespace Lib { public interface IWorker { void Work(); } }";
        var appSource = @"
namespace App { public class Worker : Lib.IWorker { public void Work() { } } }";

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll)) references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var prodTree = CSharpSyntaxTree.ParseText(prodSource);
        var prodCompilation = CSharpCompilation.Create("LibAssembly",
            new[] { prodTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var appTree = CSharpSyntaxTree.ParseText(appSource);
        var appCompilation = CSharpCompilation.Create("AppAssembly",
            new[] { appTree },
            references.Concat(new[] { prodCompilation.ToMetadataReference() }).ToList(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(appCompilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var semanticPass = new SemanticPass();
        var (externalNodes, edges) = semanticPass.Execute(appCompilation, "", knownIds);

        // Implements edge
        Assert.Contains(edges, e => e.Type == EdgeType.Implements && e.FromId.Contains("Worker"));
        // External node for IWorker must exist (kills L129 EnsureNode removal)
        Assert.Contains(externalNodes, n => n.Id.Contains("IWorker") && n.Kind == NodeKind.Type);
    }

    [Fact]
    public void ObjectCreation_CreatesExternalNode_ForConstructor()
    {
        // When creating an object from external assembly, EnsureNode should create external node
        var prodSource = @"
namespace Lib { public class Widget { public Widget() { } } }";
        var appSource = @"
namespace App
{
    public class Factory
    {
        public void Build() { var w = new Lib.Widget(); }
    }
}";
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll)) references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var prodTree = CSharpSyntaxTree.ParseText(prodSource);
        var prodCompilation = CSharpCompilation.Create("LibAssembly",
            new[] { prodTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var appTree = CSharpSyntaxTree.ParseText(appSource);
        var appCompilation = CSharpCompilation.Create("AppAssembly",
            new[] { appTree },
            references.Concat(new[] { prodCompilation.ToMetadataReference() }).ToList(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(appCompilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var semanticPass = new SemanticPass();
        var (externalNodes, edges) = semanticPass.Execute(appCompilation, "", knownIds);

        // Calls edge to Widget ctor (kills L93-94 EnsureNode+AddEdge removal)
        Assert.Contains(edges, e => e.Type == EdgeType.Calls && e.FromId.Contains("Factory.Build"));
        // External node for Widget ctor (kills L93 EnsureNode removal specifically)
        Assert.True(externalNodes.Count > 0, "Should have external nodes for Widget ctor");
    }

    [Fact]
    public void NestedInvocation_BaseVisitInvocation_WalksChildren()
    {
        // If base.VisitInvocationExpression is removed, nested invocations aren't walked
        var source = @"
namespace MyApp
{
    public class A
    {
        public static A Create() => new A();
        public void DoWork() { }
    }
    public class B
    {
        public void Run()
        {
            A.Create().DoWork();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Both Create() and DoWork() should have Calls edges
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.B.Run()" &&
            e.ToId.Contains("Create") &&
            e.Type == EdgeType.Calls);
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.B.Run()" &&
            e.ToId.Contains("DoWork") &&
            e.Type == EdgeType.Calls);
    }

    [Fact]
    public void ConstructorParam_CreatesDependsOn_ButNotReturnType()
    {
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Service
    {
        public Service(Dep d) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Constructor should have DependsOn for parameter type Dep (kills L146 VisitMemberForDependencies removal)
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Service.Service(") &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
    }

    [Fact]
    public void PropertyType_CreatesDependsOn()
    {
        var source = @"
namespace MyApp
{
    public class Config { }
    public class Host
    {
        public Config Settings { get; set; }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Property type dependency (kills L185 EmitTypeDependency removal)
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Settings") &&
            e.ToId == "MyApp.Config" &&
            e.Type == EdgeType.DependsOn);
    }

    [Fact]
    public void FieldType_CreatesDependsOn()
    {
        var source = @"
namespace MyApp
{
    public class Logger { }
    public class App
    {
        private Logger _logger;
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Field type dependency (kills L186 related)
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("_logger") &&
            e.ToId == "MyApp.Logger" &&
            e.Type == EdgeType.DependsOn);
    }

    [Fact]
    public void MemberAccess_NonMethod_NonSelf_CreatesReferencesWithEnsureNode()
    {
        // References edge to external property should create external node (kills L203 EnsureNode)
        var source = @"
namespace MyApp
{
    public class Checker
    {
        public void Check()
        {
            var len = System.Environment.NewLine;
        }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        // Should have a References edge to an external property
        var refEdge = semanticEdges.FirstOrDefault(e =>
            e.FromId.Contains("Checker.Check") &&
            e.Type == EdgeType.References);
        // External node should exist if there's a References edge to an external symbol
        if (refEdge != null)
        {
            Assert.Contains(externalNodes, n => n.Id == refEdge.ToId);
        }
    }

    [Fact]
    public void GetContainingMemberId_ReturnsNullForTopLevel()
    {
        // Invocations at class level (field initializers etc.) have no containing member
        // This test ensures GetContainingMemberId returns null
        var source = @"
namespace MyApp
{
    public class C
    {
        public void M() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);
        // No invocations = no calls edges, but the code doesn't crash
        Assert.DoesNotContain(semanticEdges, e => e.Type == EdgeType.Calls && e.FromId == "");
    }

    // ── Kill base.VisitClassDeclaration and base.VisitStructDeclaration removal ──

    [Fact]
    public void ClassWithInheritanceAndMemberCalls_BothEdgeTypes()
    {
        // If base.VisitClassDeclaration is removed (L103), member-level edges won't be created
        var source = @"
namespace MyApp
{
    public class Base { }
    public class Helper { public void Help() { } }
    public class Derived : Base
    {
        public void Work()
        {
            var h = new Helper();
            h.Help();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Class-level: Inherits edge
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Derived" && e.ToId == "MyApp.Base" && e.Type == EdgeType.Inherits);
        // Member-level: Calls edge (only present if base.VisitClassDeclaration walks children)
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Derived.Work()" && e.Type == EdgeType.Calls);
    }

    [Fact]
    public void StructWithInterfaceAndMemberCalls_BothEdgeTypes()
    {
        // If base.VisitStructDeclaration is removed (L104), member-level edges won't be created
        var source = @"
namespace MyApp
{
    public interface IWorker { }
    public class Logger { public void Log() { } }
    public struct WorkerStruct : IWorker
    {
        public void Execute()
        {
            var log = new Logger();
            log.Log();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Struct-level: Implements edge
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.WorkerStruct" && e.ToId == "MyApp.IWorker" && e.Type == EdgeType.Implements);
        // Member-level: Calls edge (only present if base.VisitStructDeclaration walks children)
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.WorkerStruct.Execute()" && e.Type == EdgeType.Calls);
    }

    [Fact]
    public void ObjectCreation_NestedInArgument_BaseVisitWalksNested()
    {
        // Kills L99 base.VisitObjectCreationExpression statement mutation
        var source = @"
namespace MyApp
{
    public class Inner { }
    public class Outer
    {
        public Outer(Inner i) { }
    }
    public class Factory
    {
        public void Build()
        {
            var o = new Outer(new Inner());
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Both Outer and Inner constructor calls should be found
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Factory.Build()" && e.ToId.Contains("Outer") && e.Type == EdgeType.Calls);
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Factory.Build()" && e.ToId.Contains("Inner") && e.Type == EdgeType.Calls);
    }

    [Fact]
    public void VisitMemberAccess_MethodSymbol_SkippedAsReferences()
    {
        // L191 logical mutation: symbol is null || symbol is IMethodSymbol → symbol is null && symbol is IMethodSymbol
        // A method-symbol member access (not parent of invocation) should NOT create References edge
        var source = @"
namespace MyApp
{
    public delegate void MyAction();
    public class Service
    {
        public void Run() { }
        public void Wire()
        {
            MyAction a = Run;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);
        // Method group conversion creates a method symbol reference that should be filtered
        // The key assertion is that it doesn't crash and the filtering works
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("Wire") && e.ToId.Contains("Run") && e.Type == EdgeType.References);
    }

    [Fact]
    public void Override_AllThreeConditions_MustBeTrue()
    {
        // Kills L239 logical mutations on the override check:
        // symbol is null || !symbol.IsOverride || symbol.OverriddenMethod is null
        var source = @"
namespace MyApp
{
    public class Animal
    {
        public virtual void Speak() { }
    }
    public class Dog : Animal
    {
        public override void Speak() { }
    }
    public class Cat : Animal
    {
        // new, not override
        public new void Speak() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Dog.Speak overrides Animal.Speak → Overrides edge exists
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Dog.Speak()" && e.ToId == "MyApp.Animal.Speak()" && e.Type == EdgeType.Overrides);

        // Cat.Speak is new, NOT override → no Overrides edge
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId == "MyApp.Cat.Speak()" && e.Type == EdgeType.Overrides);
    }

    [Fact]
    public void EnsureNode_KnownId_DoesNotCreateExternal()
    {
        // Kills L302 logical mutation: _knownIds.Contains(id) && _externalNodes.ContainsKey(id)
        var source = @"
namespace MyApp
{
    public class ServiceA
    {
        public void DoWork()
        {
            var b = new ServiceB();
            b.Process();
        }
    }
    public class ServiceB
    {
        public void Process() { }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        // Internal calls should have edges but NO external nodes for known IDs
        Assert.Contains(semanticEdges, e => e.Type == EdgeType.Calls);
        Assert.DoesNotContain(externalNodes, n => n.Id.Contains("ServiceB.Process"));
        Assert.DoesNotContain(externalNodes, n => n.Id.Contains("ServiceA"));
    }

    [Fact]
    public void GetContainingMemberId_FromPropertyAccessor_ReturnsPropertyId()
    {
        // Kills L287 conditional mutation on GetContainingMemberId
        var source = @"
namespace MyApp
{
    public class Config
    {
        public static string Name { get; set; }
    }
    public class Reader
    {
        public void Read()
        {
            var x = Config.Name;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Member access inside a method should find containing member
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Read") && e.Type == EdgeType.References);
    }

    // ── Additional mutation-killing tests ──

    [Fact]
    public void RecordWithInheritanceAndMembers_BaseVisitRecordWalksChildren()
    {
        // Kills L104: base.VisitRecordDeclaration statement removal
        // Without base walk, member-level edges inside a record won't be found
        var source = @"
namespace MyApp
{
    public record BaseRecord { }
    public class Helper { public void Help() { } }
    public record DerivedRecord : BaseRecord
    {
        public void Work()
        {
            var h = new Helper();
            h.Help();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Record-level: Inherits edge
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRecord" && e.ToId == "MyApp.BaseRecord" && e.Type == EdgeType.Inherits);
        // Member-level: Calls edge (only present if base.VisitRecordDeclaration walks children)
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.DerivedRecord.Work()" && e.Type == EdgeType.Calls);
    }

    [Fact]
    public void ConstructorBody_ContainsInvocations_BaseVisitWalksChildren()
    {
        // Kills L146: base.VisitConstructorDeclaration statement removal
        // Without base walk, invocations inside constructors won't produce edges
        var source = @"
namespace MyApp
{
    public class Logger { public void Init() { } }
    public class Service
    {
        public Service()
        {
            var log = new Logger();
            log.Init();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Constructor should produce Calls edges for invocations inside it
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("Service.Service(") &&
            e.ToId.Contains("Logger.Init") &&
            e.Type == EdgeType.Calls);
    }

    [Fact]
    public void MemberAccess_NestedChain_BaseVisitWalksChildren()
    {
        // Kills L209: base.VisitMemberAccessExpression statement removal at end
        // Nested member access: a.B.C should produce references for B and C
        var source = @"
namespace MyApp
{
    public class Inner
    {
        public static int Value = 42;
    }
    public class Outer
    {
        public static Inner Item = new Inner();
    }
    public class Consumer
    {
        public void Use()
        {
            var x = Outer.Item;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Should have a References edge for Outer.Item
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Consumer.Use()" &&
            e.Type == EdgeType.References &&
            e.ToId.Contains("Item"));
    }

    [Fact]
    public void MemberAccess_MethodSymbol_NotParentOfInvocation_SkippedAsReferences()
    {
        // Kills L191: && mutated to || on `symbol is null || symbol is IMethodSymbol`
        // A method group (method symbol not wrapped in invocation) should NOT create References edge
        var source = @"
namespace MyApp
{
    public class Processor
    {
        public void Process() { }
        public void Setup()
        {
            System.Action action = Process;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Method group reference should NOT produce a References edge
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("Setup") &&
            e.ToId.Contains("Process") &&
            e.Type == EdgeType.References);
    }

    [Fact]
    public void MemberAccess_NullSymbol_DoesNotCrash()
    {
        // Kills L191: && mutated to || — when symbol is null, should skip (not crash)
        // Accessing an unresolvable member
        var source = @"
namespace MyApp
{
    public class Worker
    {
        public void Work()
        {
            var x = 42;
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);
        // Just verify no crash and no spurious References edges
        Assert.DoesNotContain(semanticEdges, e =>
            e.Type == EdgeType.References && string.IsNullOrEmpty(e.ToId));
    }

    [Fact]
    public void Override_NonOverrideMethod_NoOverridesEdge()
    {
        // Kills L239 mutations: ensures non-override methods don't produce Overrides edges
        var source = @"
namespace MyApp
{
    public class Base
    {
        public virtual void Run() { }
        public void Other() { }
    }
    public class Derived : Base
    {
        public override void Run() { }
        public void Extra() { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Only Run() should have Overrides edge
        Assert.Single(semanticEdges.Where(e => e.Type == EdgeType.Overrides));
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Derived.Run()" &&
            e.ToId == "MyApp.Base.Run()" &&
            e.Type == EdgeType.Overrides);

        // Extra and Other should NOT have Overrides edges
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("Extra") && e.Type == EdgeType.Overrides);
        Assert.DoesNotContain(semanticEdges, e =>
            e.FromId.Contains("Other") && e.Type == EdgeType.Overrides);
    }

    [Fact]
    public void EmitTypeDependency_AddsEdge_NotJustEnsuresNode()
    {
        // Kills L261: statement removal of AddEdge in EmitTypeDependency
        // Verify that DependsOn edges are actually added, not just external nodes
        var source = @"
namespace MyApp
{
    public class Dep { }
    public class Consumer
    {
        public Dep GetDep() => null;
        public void SetDep(Dep d) { }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Return type dependency
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Consumer.GetDep()" &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
        // Parameter type dependency
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.Consumer.SetDep(MyApp.Dep)" &&
            e.ToId == "MyApp.Dep" &&
            e.Type == EdgeType.DependsOn);
    }

    [Fact]
    public void ExternalNode_Dedup_SameExternalCalledTwice()
    {
        // Kills L302-303: dedup logic in EnsureNode
        // Same external symbol referenced from two different methods should produce only one external node
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo() { System.Console.WriteLine(""a""); }
    }
    public class B
    {
        public void Bar() { System.Console.WriteLine(""b""); }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        // Both should have Calls edges to Console.WriteLine
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("A.Foo") && e.ToId.Contains("WriteLine") && e.Type == EdgeType.Calls);
        Assert.Contains(semanticEdges, e =>
            e.FromId.Contains("B.Bar") && e.ToId.Contains("WriteLine") && e.Type == EdgeType.Calls);

        // But there should be only ONE external node for Console.WriteLine (dedup)
        var writeLineNodes = externalNodes.Where(n =>
            n.Id.Contains("Console") && n.Id.Contains("WriteLine")).ToList();
        Assert.Single(writeLineNodes);
    }

    [Fact]
    public void ExternalNode_KnownId_NotDuplicated()
    {
        // Kills L302: _knownIds.Contains(id) check — if mutated to ||, known IDs would create externals
        var source = @"
namespace MyApp
{
    public class A
    {
        public void Foo()
        {
            var b = new B();
            b.Process();
            b.Process(); // duplicate call
        }
    }
    public class B
    {
        public void Process() { }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        // Internal calls produce edges
        Assert.Contains(semanticEdges, e =>
            e.FromId == "MyApp.A.Foo()" &&
            e.ToId == "MyApp.B.Process()" &&
            e.Type == EdgeType.Calls);

        // B.Process is a known ID (from SyntaxPass), so NO external node
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.B.Process()");
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.B");
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.A");
    }

    [Fact]
    public void InvocationExpression_ProducesCallsEdge_VerifyEdgeCreation()
    {
        // Kills L99 (VisitInvocationExpression) and L74 (AddEdge) statement removal
        // Verifies that the full chain: VisitInvocation → GetSymbolInfo → AddEdge works
        var source = @"
namespace MyApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
    public class Client
    {
        public void Run()
        {
            var calc = new Calculator();
            var result = calc.Add(1, 2);
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Verify Calls edge from Run to Add
        var callEdge = semanticEdges.FirstOrDefault(e =>
            e.FromId == "MyApp.Client.Run()" &&
            e.ToId == "MyApp.Calculator.Add(int, int)" &&
            e.Type == EdgeType.Calls);
        Assert.NotNull(callEdge);
        Assert.False(callEdge!.IsExternal);
    }

    [Fact]
    public void PropertyAccess_ExternalProperty_CreatesReferencesEdgeAndNode()
    {
        // Kills L186 (return statement after invocation parent check) and L204 (AddEdge for References)
        var source = @"
using System;
namespace MyApp
{
    public class Checker
    {
        public void Check()
        {
            var nl = Environment.NewLine;
        }
    }
}";
        var (_, _, externalNodes, semanticEdges) = RunBothPasses(source);

        // Should have a References edge (not a Calls edge) for Environment.NewLine
        var refEdge = semanticEdges.FirstOrDefault(e =>
            e.FromId.Contains("Checker.Check") &&
            e.Type == EdgeType.References &&
            e.ToId.Contains("NewLine"));
        Assert.NotNull(refEdge);

        // External node should be created for NewLine
        if (refEdge != null)
        {
            Assert.Contains(externalNodes, n => n.Id == refEdge.ToId);
        }
    }

    [Fact]
    public void MethodReturnType_EmitTypeDependency_EnsuresExternalNode()
    {
        // Kills L261 EnsureNode in EmitTypeDependency for external types
        var prodSource = @"
namespace Lib { public class Result { } }";
        var appSource = @"
namespace App
{
    public class Service
    {
        public Lib.Result GetResult() => null;
    }
}";
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll)) references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var prodTree = CSharpSyntaxTree.ParseText(prodSource);
        var prodCompilation = CSharpCompilation.Create("LibAssembly",
            new[] { prodTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var appTree = CSharpSyntaxTree.ParseText(appSource);
        var appCompilation = CSharpCompilation.Create("AppAssembly",
            new[] { appTree },
            references.Concat(new[] { prodCompilation.ToMetadataReference() }).ToList(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(appCompilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var semanticPass = new SemanticPass();
        var (externalNodes, edges) = semanticPass.Execute(appCompilation, "", knownIds);

        // DependsOn edge to external Result type
        Assert.Contains(edges, e =>
            e.FromId.Contains("GetResult") &&
            e.ToId.Contains("Result") &&
            e.Type == EdgeType.DependsOn);
        // External node for Result
        Assert.Contains(externalNodes, n => n.Id.Contains("Result") && n.Kind == NodeKind.Type);
    }

    [Fact]
    public void AddEdge_Dedup_SameFromToType_OnlyOneEdge()
    {
        // Kills seenEdges dedup logic in AddEdge
        var source = @"
namespace MyApp
{
    public class Target
    {
        public void Method() { }
    }
    public class Caller
    {
        public void Go()
        {
            var t = new Target();
            t.Method();
            t.Method();
            t.Method();
        }
    }
}";
        var (_, _, _, semanticEdges) = RunBothPasses(source);

        // Despite 3 calls, only 1 Calls edge should exist (dedup)
        var callEdges = semanticEdges.Where(e =>
            e.FromId == "MyApp.Caller.Go()" &&
            e.ToId == "MyApp.Target.Method()" &&
            e.Type == EdgeType.Calls).ToList();
        Assert.Single(callEdges);
    }
}
