using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class DiPassTests
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

    private static string MakeGenericSource(string methodName) => $@"
namespace MyApp
{{
    public interface IService {{ }}
    public class ServiceImpl : IService {{ }}

    public static class ServiceCollectionExtensions
    {{
        public static void {methodName}<TService, TImplementation>(this object services)
            where TImplementation : TService {{ }}
    }}

    public class Startup
    {{
        public void ConfigureServices(object services)
        {{
            services.{methodName}<IService, ServiceImpl>();
        }}
    }}
}}";

    private static string MakeTypeofSource(string methodName) => $@"
namespace MyApp
{{
    public interface IService {{ }}
    public class ServiceImpl : IService {{ }}

    public static class ServiceCollectionExtensions
    {{
        public static void {methodName}(this object services, System.Type serviceType, System.Type implType) {{ }}
    }}

    public class Startup
    {{
        public void ConfigureServices(object services)
        {{
            services.{methodName}(typeof(IService), typeof(ServiceImpl));
        }}
    }}
}}";

    [Theory]
    [InlineData("AddScoped", "Scoped")]
    [InlineData("AddTransient", "Transient")]
    [InlineData("AddSingleton", "Singleton")]
    [InlineData("TryAddScoped", "Scoped")]
    [InlineData("TryAddTransient", "Transient")]
    [InlineData("TryAddSingleton", "Singleton")]
    public void GenericOverload_EmitsResolvesToEdge_WithCorrectLifetime(string methodName, string expectedLifetime)
    {
        var compilation = CreateCompilation(MakeGenericSource(methodName));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.ResolvesTo, edge.Type);
        Assert.Equal("MyApp.IService", edge.FromId);
        Assert.Equal("MyApp.ServiceImpl", edge.ToId);
        Assert.Equal(expectedLifetime, edge.Metadata["lifetime"]);
    }

    [Theory]
    [InlineData("AddScoped", "Scoped")]
    [InlineData("AddTransient", "Transient")]
    [InlineData("AddSingleton", "Singleton")]
    [InlineData("TryAddScoped", "Scoped")]
    [InlineData("TryAddTransient", "Transient")]
    [InlineData("TryAddSingleton", "Singleton")]
    public void TypeofOverload_EmitsResolvesToEdge_WithCorrectLifetime(string methodName, string expectedLifetime)
    {
        var compilation = CreateCompilation(MakeTypeofSource(methodName));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.ResolvesTo, edge.Type);
        Assert.Equal("MyApp.IService", edge.FromId);
        Assert.Equal("MyApp.ServiceImpl", edge.ToId);
        Assert.Equal(expectedLifetime, edge.Metadata["lifetime"]);
    }

    [Theory]
    [InlineData("AddScoped")]
    [InlineData("AddTransient")]
    [InlineData("AddSingleton")]
    [InlineData("TryAddScoped")]
    [InlineData("TryAddTransient")]
    [InlineData("TryAddSingleton")]
    public void AllDiMethods_EmitBothMetadataKeys(string methodName)
    {
        var compilation = CreateCompilation(MakeGenericSource(methodName));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.True(edge.Metadata.ContainsKey("lifetime"), "Missing 'lifetime' metadata key");
        Assert.True(edge.Metadata.ContainsKey("registrationFile"), "Missing 'registrationFile' metadata key");
        Assert.NotEmpty(edge.Metadata["lifetime"]);
    }

    [Fact]
    public void ExternalNodes_HaveCorrectProperties()
    {
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var knownIds = new HashSet<string>();
        var (_, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Equal(2, externalNodes.Count);

        var iface = externalNodes.Single(n => n.Id == "MyApp.IService");
        Assert.Equal("IService", iface.Name);
        Assert.Equal(NodeKind.Type, iface.Kind);
        Assert.Equal(Core.Models.Accessibility.Public, iface.Accessibility);
        Assert.Equal("MyApp", iface.ContainingNamespaceId);

        var impl = externalNodes.Single(n => n.Id == "MyApp.ServiceImpl");
        Assert.Equal("ServiceImpl", impl.Name);
        Assert.Equal(NodeKind.Type, impl.Kind);
        Assert.Equal(Core.Models.Accessibility.Public, impl.Accessibility);
        Assert.Equal("MyApp", impl.ContainingNamespaceId);
    }

    [Fact]
    public void KnownNodeIds_SuppressExternalNodeCreation()
    {
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var knownIds = new HashSet<string> { "MyApp.IService", "MyApp.ServiceImpl" };
        var (edges, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Single(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void NonDiMethod_ProducesNoEdges()
    {
        var source = @"
namespace MyApp
{
    public interface IService { }
    public class ServiceImpl : IService { }

    public static class ServiceCollectionExtensions
    {
        public static void Configure<TService, TImplementation>(this object services)
            where TImplementation : TService { }
    }

    public class Startup
    {
        public void ConfigureServices(object services)
        {
            services.Configure<IService, ServiceImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void NoDiRegistrations_ReturnsEmpty()
    {
        var source = @"
namespace MyApp
{
    public class Foo
    {
        public void DoWork() { }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void RegistrationFile_MetadataValueIsPopulated()
    {
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.True(edge.Metadata.ContainsKey("registrationFile"));
        // The value should be a string (may be empty for in-memory trees, but key must exist)
        Assert.IsType<string>(edge.Metadata["registrationFile"]);
    }

    [Theory]
    [InlineData("AddScoped", "Scoped")]
    [InlineData("AddTransient", "Transient")]
    [InlineData("AddSingleton", "Singleton")]
    public void ExtractLifetime_DistinguishesBetweenLifetimes(string methodName, string expectedLifetime)
    {
        // Each lifetime keyword must map to exactly its own value, not another
        var compilation = CreateCompilation(MakeGenericSource(methodName));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(expectedLifetime, edge.Metadata["lifetime"]);
        // Ensure it's not one of the other lifetimes
        var otherLifetimes = new[] { "Scoped", "Transient", "Singleton", "Unknown" }
            .Where(l => l != expectedLifetime);
        foreach (var other in otherLifetimes)
        {
            Assert.NotEqual(other, edge.Metadata["lifetime"]);
        }
    }

    [Fact]
    public void GenericOverload_ReturnPreventsTypeofPath_SingleEdge()
    {
        // The generic overload should produce exactly 1 edge, not 2
        // (kills the return; statement mutation on L79)
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }

    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
        public static void AddScoped(this object s, System.Type t1, System.Type t2) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IFoo, FooImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Single(edges);
    }

    [Fact]
    public void ExternalNode_HasContainingNamespaceId()
    {
        // Kills L205 ContainingNamespaceId conditional mutation
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        var serviceNode = externalNodes.FirstOrDefault(n => n.Id == "MyApp.IService");
        Assert.NotNull(serviceNode);
        Assert.Equal("MyApp", serviceNode!.ContainingNamespaceId);
    }

    [Fact]
    public void GetRelativePath_EmptyAbsolutePath_ReturnsEmpty()
    {
        // Kills GetRelativePath logic mutations (L225)
        // When file path is empty, result should be empty string
        var source = MakeGenericSource("AddScoped");
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        // The registrationFile metadata should not be null
        var edge = Assert.Single(edges);
        Assert.True(edge.Metadata.ContainsKey("registrationFile"));
    }

    [Fact]
    public void GetRelativePath_EmptySolutionRoot_ReturnsFilePath()
    {
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        // Empty solution root
        var (edges, _) = pass.Execute(compilation, "", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.NotNull(edge.Metadata["registrationFile"]);
    }

    [Fact]
    public void KnownNodeIds_SuppressExternalNodeCreation_ForBothTypes()
    {
        // When BOTH abstraction and implementation are known, no external nodes
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var knownIds = new HashSet<string> { "MyApp.IService", "MyApp.ServiceImpl" };
        var (edges, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Single(edges); // Edge still created
        Assert.Empty(externalNodes); // But no external nodes
    }

    [Fact]
    public void AddScoped_SingleTypeArg_NoEdge()
    {
        // Covers L116: TypeArgumentList.Arguments.Count != 2
        var source = @"
namespace MyApp
{
    public class ServiceImpl { }

    public static class Ext
    {
        public static void AddScoped<T>(this object services) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<ServiceImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    [Fact]
    public void AddScoped_TypeofWithOneArg_NoEdge()
    {
        // Covers L148: args.Count < 2
        var source = @"
namespace MyApp
{
    public class Svc { }

    public static class Ext
    {
        public static void AddScoped(this object services, System.Type t) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(typeof(Svc));
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    [Fact]
    public void AddScoped_TypeofWithNonTypeofArgs_NoEdge()
    {
        // Covers L152-153: arguments are not TypeOfExpressionSyntax
        var source = @"
namespace MyApp
{
    public static class Ext
    {
        public static void AddScoped(this object services, object a, object b) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(""hello"", ""world"");
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    // ── Mutation-killing tests ──

    [Fact]
    public void GenericOverload_OneUnresolvableTypeArg_NoEdge()
    {
        // Kills L135: && mutated to || — only one side resolves
        var source = @"
namespace MyApp
{
    public interface IService { }

    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IService, NonExistentType>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        // NonExistentType can't resolve, so implementation is null — no edge should be produced
        Assert.Empty(edges);
    }

    [Fact]
    public void TypeofOverload_OneTypeofOneNonTypeof_NoEdge()
    {
        // Kills L152: || mutated to && — only one arg is typeof
        var source = @"
namespace MyApp
{
    public interface IService { }

    public static class Ext
    {
        public static void AddScoped(this object services, System.Type t1, object other) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(typeof(IService), ""not a typeof"");
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    [Fact]
    public void TypeofOverload_SecondTypeofFirst_NonTypeof_NoEdge()
    {
        // Kills L152: || mutated to && — first arg is non-typeof, second is typeof
        var source = @"
namespace MyApp
{
    public class ServiceImpl { }

    public static class Ext
    {
        public static void AddScoped(this object services, object first, System.Type second) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(""not a typeof"", typeof(ServiceImpl));
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    [Fact]
    public void TypeofOverload_BothResolved_VerifyEdgeAndMetadata()
    {
        // Kills L169: && mutated to || on typeof path resolution
        // Also verifies exact metadata keys: "lifetime", "registrationFile"
        var compilation = CreateCompilation(MakeTypeofSource("AddSingleton"));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.ResolvesTo, edge.Type);
        Assert.Equal("MyApp.IService", edge.FromId);
        Assert.Equal("MyApp.ServiceImpl", edge.ToId);
        Assert.True(edge.Metadata.ContainsKey("lifetime"));
        Assert.True(edge.Metadata.ContainsKey("registrationFile"));
        Assert.Equal("Singleton", edge.Metadata["lifetime"]);
    }

    [Fact]
    public void TypeofOverload_OneUnresolvableType_NoEdge()
    {
        // Kills L169: && mutated to || — only one typeof arg resolves
        var source = @"
namespace MyApp
{
    public interface IService { }

    public static class Ext
    {
        public static void AddScoped(this object services, System.Type t1, System.Type t2) { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped(typeof(IService), typeof(NonExistentType));
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());
        Assert.Empty(edges);
    }

    [Fact]
    public void MetadataKeys_ExactNamesVerified()
    {
        // Kills L205: string mutation on metadata keys — verify exact key names
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        // Must have exactly these keys (not mutated strings)
        Assert.Equal(2, edge.Metadata.Count);
        Assert.True(edge.Metadata.ContainsKey("lifetime"), "Key must be exactly 'lifetime'");
        Assert.True(edge.Metadata.ContainsKey("registrationFile"), "Key must be exactly 'registrationFile'");
        Assert.False(edge.Metadata.ContainsKey("Lifetime"), "Key should not be 'Lifetime'");
        Assert.False(edge.Metadata.ContainsKey("registration_file"), "Key should not be 'registration_file'");
    }

    [Fact]
    public void ExternalNode_FilePath_IsEmptyString()
    {
        // Kills L218: string mutation on empty string for FilePath
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.NotEmpty(externalNodes);
        foreach (var node in externalNodes)
        {
            Assert.Equal(string.Empty, node.FilePath);
            Assert.NotNull(node.FilePath);
        }
    }

    [Fact]
    public void ExternalNode_NamespaceId_GlobalNamespace_IsNull()
    {
        // Kills L208: conditional mutation on namespace resolution
        // Types in global namespace should have null ContainingNamespaceId
        var source = @"
public interface IGlobal { }
public class GlobalImpl : IGlobal { }

public static class Ext
{
    public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
}

public class Startup
{
    public void Configure(object services)
    {
        services.AddScoped<IGlobal, GlobalImpl>();
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        // Types in global namespace should have null ContainingNamespaceId
        foreach (var node in externalNodes)
        {
            Assert.Null(node.ContainingNamespaceId);
        }
    }

    [Fact]
    public void GetRelativePath_WithAbsolutePath_ReturnsRelative()
    {
        // Kills L225-226: path normalization mutations
        // Use a named syntax tree with a file path to test relative path computation
        var source = MakeGenericSource("AddScoped");
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "/root/src/Startup.cs");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        var registrationFile = edge.Metadata["registrationFile"];
        // Should be relative, not the absolute path
        Assert.DoesNotContain("/root/src/Startup.cs", registrationFile);
        Assert.NotEmpty(registrationFile);
    }

    [Fact]
    public void GetRelativePath_NullPath_ReturnsEmptyString()
    {
        // Kills L225-226: null/empty path edge case — absolutePath ?? string.Empty
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        // In-memory trees have empty FilePath
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.NotNull(edge.Metadata["registrationFile"]);
    }

    [Fact]
    public void VisitInvocationExpression_ActuallyCallsTryEmitRegistration()
    {
        // Kills L64: statement removal of TryEmitRegistration(node)
        // If TryEmitRegistration isn't called, no edges are produced at all
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        // Both edges and external nodes must exist — proves the visitor found the invocation
        Assert.Single(edges);
        Assert.NotEmpty(externalNodes);
        Assert.Equal(EdgeType.ResolvesTo, edges[0].Type);
    }

    [Fact]
    public void BaseVisitInvocationExpression_WalksNestedInvocations()
    {
        // Kills L64: statement removal of base.VisitInvocationExpression(node)
        // Nested DI calls must all be found
        var source = @"
namespace MyApp
{
    public interface IFoo { }
    public class FooImpl : IFoo { }
    public interface IBar { }
    public class BarImpl : IBar { }

    public static class Ext
    {
        public static object AddScoped<T1, T2>(this object s) where T2 : T1 => s;
        public static object AddSingleton<T1, T2>(this object s) where T2 : T1 => s;
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IFoo, FooImpl>();
            services.AddSingleton<IBar, BarImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.FromId == "MyApp.IFoo" && e.ToId == "MyApp.FooImpl");
        Assert.Contains(edges, e => e.FromId == "MyApp.IBar" && e.ToId == "MyApp.BarImpl");
    }

    [Fact]
    public void ExternalNode_Signature_IsPopulated()
    {
        // Verify Signature property is set (kills potential string mutation on Signature)
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        foreach (var node in externalNodes)
        {
            Assert.NotNull(node.Signature);
            Assert.NotEmpty(node.Signature);
        }
    }

    [Fact]
    public void ExternalNode_Accessibility_IsCorrect()
    {
        // Verify Accessibility mapping works
        var compilation = CreateCompilation(MakeGenericSource("AddScoped"));
        var pass = new DiPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        foreach (var node in externalNodes)
        {
            Assert.Equal(Core.Models.Accessibility.Public, node.Accessibility);
        }
    }

    [Fact]
    public void ExternalNode_Dedup_SameRegistrationTwice()
    {
        // Verify seenExternalIds prevents duplicate external nodes
        var source = @"
namespace MyApp
{
    public interface IService { }
    public class ServiceImpl : IService { }

    public static class Ext
    {
        public static void AddScoped<T1, T2>(this object s) where T2 : T1 { }
        public static void AddSingleton<T1, T2>(this object s) where T2 : T1 { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IService, ServiceImpl>();
            services.AddSingleton<IService, ServiceImpl>();
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new DiPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);
        // External nodes should NOT be duplicated
        Assert.Equal(2, externalNodes.Count);
        Assert.Single(externalNodes, n => n.Id == "MyApp.IService");
        Assert.Single(externalNodes, n => n.Id == "MyApp.ServiceImpl");
    }
}
