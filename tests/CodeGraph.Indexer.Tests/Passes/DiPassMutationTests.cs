using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

/// <summary>
/// Additional mutation-killing tests for DiPass.
/// Targets: GetMethodName variations, ExternalNode dedup via seenExternalIds,
/// edge properties, GetRelativePath with real file paths.
/// </summary>
public class DiPassMutationTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CreateCompilation(syntaxTree);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree syntaxTree)
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

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string MakeGenericSource(string methodName)
    {
        return $@"
namespace MyApp
{{
    public interface IService {{ }}
    public class ServiceImpl : IService {{ }}
    public static class Ext
    {{
        public static void {methodName}<T1, T2>(this object services) where T2 : T1 {{ }}
    }}
    public class Startup
    {{
        public void Configure(object services)
        {{
            services.{methodName}<IService, ServiceImpl>();
        }}
    }}
}}";
    }

    // ── Edge has exactly EdgeType.ResolvesTo (not any other) ──

    [Fact]
    public void Edge_TypeIsExactlyResolvesTo()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.ResolvesTo, edge.Type);
        // Verify it's not another type
        Assert.NotEqual(EdgeType.Calls, edge.Type);
        Assert.NotEqual(EdgeType.DependsOn, edge.Type);
        Assert.NotEqual(EdgeType.Implements, edge.Type);
    }

    // ── Edge Confidence is Verified ──

    [Fact]
    public void Edge_ConfidenceIsVerified()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddSingleton<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddSingleton<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeConfidence.Verified, edge.Confidence);
    }

    // ── FromId is the service interface, ToId is the implementation ──

    [Fact]
    public void Edge_FromIdIsService_ToIdIsImplementation()
    {
        var source = @"
namespace MyApp
{
    public interface IService { }
    public class MyServiceImpl : IService { }
    public static class Ext
    {
        public static void AddTransient<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddTransient<IService, MyServiceImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        // Direction: FromId = abstraction, ToId = implementation
        Assert.Equal("MyApp.IService", edge.FromId);
        Assert.Equal("MyApp.MyServiceImpl", edge.ToId);
        // NOT reversed:
        Assert.NotEqual("MyApp.MyServiceImpl", edge.FromId);
    }

    // ── registrationFile metadata contains relative path ──

    [Fact]
    public void Edge_RegistrationFileMetadata_ContainsRelativePath()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        // Use a solutionRoot to test relative path computation
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        // Key must be exactly "registrationFile"
        Assert.True(edge.Metadata.ContainsKey("registrationFile"));
        // Key must NOT be misspelled
        Assert.False(edge.Metadata.ContainsKey("registration_file"));
        Assert.False(edge.Metadata.ContainsKey("RegistrationFile"));
    }

    // ── lifetime metadata key is exactly "lifetime" ──

    [Fact]
    public void Edge_LifetimeMetadataKey_IsExactString()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.True(edge.Metadata.ContainsKey("lifetime"));
        // Verify exact key name (kills string mutations)
        Assert.False(edge.Metadata.ContainsKey("Lifetime"));
        Assert.False(edge.Metadata.ContainsKey("life_time"));
    }

    // ── ExternalNode dedup: same type registered twice only adds one external node ──

    [Fact]
    public void DuplicateRegistration_CreatesOnlyOneExternalNodePerType()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
        public static void AddSingleton<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IFoo, FooImpl>();
            services.AddSingleton<IFoo, FooImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        // Two edges (different lifetimes)
        Assert.Equal(2, edges.Count);

        // But only one external node per type (dedup by seenExternalIds)
        var fooNodes = externalNodes.Where(n => n.Id == "MyApp.IFoo").ToList();
        Assert.Single(fooNodes);
        var implNodes = externalNodes.Where(n => n.Id == "MyApp.FooImpl").ToList();
        Assert.Single(implNodes);
    }

    // ── Method invocation without member access (standalone function name) produces no edge ──

    [Fact]
    public void StaticMethodCall_NotDiMethod_NoEdge()
    {
        var source = @"
namespace MyApp
{
    public class Helper
    {
        public static void DoSomething() { }
    }
    public class Caller
    {
        public void Run() { Helper.DoSomething(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }

    // ── Generic overload with 1 type argument (not DI pattern) ──

    [Fact]
    public void GenericMethodWithOneTypeArg_NoEdge()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public static class Ext
    {
        public static void AddScoped<T>(this object s) { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        // Single type arg overload should NOT produce a ResolvesTo edge
        Assert.Empty(edges);
    }

    // ── typeof overload with less than 2 arguments ──

    [Fact]
    public void TypeofOverload_OnlyOneArg_NoEdge()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public static class Ext
    {
        public static void AddScoped(this object s, System.Type t) { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped(typeof(IFoo)); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }

    // ── ExternalNode has empty FilePath ──

    [Fact]
    public void ExternalNode_HasEmptyFilePath()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.All(externalNodes, n => Assert.Equal(string.Empty, n.FilePath));
    }

    // ── ExternalNode has NodeKind.Type ──

    [Fact]
    public void ExternalNode_HasTypeKind()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.All(externalNodes, n => Assert.Equal(NodeKind.Type, n.Kind));
    }

    // ── TryAdd variants produce edges same as Add ──

    [Fact]
    public void TryAddScoped_ProducesEdgeSameAsAddScoped()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void TryAddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.TryAddScoped<IFoo, FooImpl>(); }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.ResolvesTo, edge.Type);
        Assert.Equal("Scoped", edge.Metadata["lifetime"]);
    }

    // ── GetRelativePath with non-empty solutionRoot ──

    [Fact]
    public void GetRelativePath_WithSolutionRoot_ProducesRelativePath()
    {
        // Create compilation from a file with a known path
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }
    public class Startup
    {
        public void Configure(object services) { services.AddScoped<IFoo, FooImpl>(); }
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"D:\repo\src\Startup.cs");
        var compilation = CreateCompilation(syntaxTree);

        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(Path.Combine("src", "Startup.cs"), edge.Metadata["registrationFile"]);
    }

    [Fact]
    public void StandaloneGenericInvocation_DoesNotProduceRegistrationEdge()
    {
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public class Startup
    {
        public static void AddScoped<T1, T2>(object services) where T2 : T1 { }

        public void Configure(object services)
        {
            AddScoped<IFoo, FooImpl>(services);
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, @"D:\repo", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void TypeofOverload_WithAdditionalArguments_StillUsesFirstTwoTypes()
    {
        var source = @"
namespace MyApp
{
    public interface IService { }
    public class ServiceImpl : IService { }
    public static class Ext
    {
        public static void AddScoped(this object services, System.Type serviceType, System.Type implementationType, int version) { }
    }
    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(typeof(IService), typeof(ServiceImpl), 7);
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("MyApp.IService", edge.FromId);
        Assert.Equal("MyApp.ServiceImpl", edge.ToId);
        Assert.Equal("Scoped", edge.Metadata["lifetime"]);
    }

    [Fact]
    public void KnownNodeIds_SuppressOnlyMatchingExternalNode()
    {
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var knownIds = new HashSet<string> { "MyApp.IService" };
        var (edges, externalNodes) = pass.Execute(compilation, @"D:\repo", knownIds);

        Assert.Single(edges);
        var externalNode = Assert.Single(externalNodes);
        Assert.Equal("MyApp.ServiceImpl", externalNode.Id);
    }

    [Fact]
    public void GlobalNamespaceTypes_HaveNullContainingNamespaceId()
    {
        var source = @"
public interface IService { }
public class ServiceImpl : IService { }
public static class Ext
{
    public static void AddScoped<T1, T2>(this object services) where T2 : T1 { }
}
public class Startup
{
    public void Configure(object services)
    {
        services.AddScoped<IService, ServiceImpl>();
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("IService", edge.FromId);
        Assert.Equal("ServiceImpl", edge.ToId);
        Assert.Collection(externalNodes.OrderBy(n => n.Id),
            service =>
            {
                Assert.Equal("IService", service.Id);
                Assert.Null(service.ContainingNamespaceId);
            },
            implementation =>
            {
                Assert.Equal("ServiceImpl", implementation.Id);
                Assert.Null(implementation.ContainingNamespaceId);
            });
    }
}
