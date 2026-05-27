using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGraph.Indexer.Tests.Passes;

public class PassUtilitiesMutationTests
{
    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
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

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (SemanticModel Model, SyntaxTree Tree) CreateModel(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: @"D:\repo\Test.cs");
        var compilation = CreateCompilation(tree);
        return (compilation.GetSemanticModel(tree), tree);
    }

    [Fact]
    public void GetInvocationMethodName_MemberAccessIdentifier_ReturnsIdentifierText()
    {
        var (_, tree) = CreateModel("namespace MyApp { public class Startup { public void Configure(App app) { app.UseAuthentication(); } } public class App { public void UseAuthentication() { } } }");
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var methodName = PassUtilities.GetInvocationMethodName(invocation);

        Assert.Equal("UseAuthentication", methodName);
    }

    [Fact]
    public void GetInvocationMethodName_MemberAccessGeneric_ReturnsGenericNameText()
    {
        var (_, tree) = CreateModel(@"
namespace MyApp
{
    public interface IService { }
    public class ServiceImpl : IService { }
    public static class ServiceCollectionExtensions
    {
        public static void AddScoped<TService, TImplementation>(this object services) where TImplementation : TService { }
    }

    public class Startup
    {
        public void Configure(object services)
        {
            services.AddScoped<IService, ServiceImpl>();
        }
    }
}");
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single(node => node.ToString().Contains("AddScoped", StringComparison.Ordinal));

        var methodName = PassUtilities.GetInvocationMethodName(invocation);

        Assert.Equal("AddScoped", methodName);
    }

    [Fact]
    public void GetInvocationMethodName_IdentifierInvocation_ReturnsNull()
    {
        var (_, tree) = CreateModel("class C { void M() { Helper(); } void Helper() { } }");
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var methodName = PassUtilities.GetInvocationMethodName(invocation);

        Assert.Null(methodName);
    }

    [Fact]
    public void GetContainingMethodId_InvocationInsideMethod_ReturnsMethodId()
    {
        var source = @"
namespace MyApp
{
    public static class Helper { public static void Touch() { } }
    public class Service
    {
        public void Run()
        {
            Helper.Touch();
        }
    }
}";
        var (model, tree) = CreateModel(source);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var containingMethodId = PassUtilities.GetContainingMethodId(model, invocation);

        Assert.Equal("MyApp.Service.Run()", containingMethodId);
    }

    [Fact]
    public void GetContainingMethodId_InvocationInsideConstructor_ReturnsNull()
    {
        var source = @"
namespace MyApp
{
    public static class Helper { public static void Touch() { } }
    public class Service
    {
        public Service()
        {
            Helper.Touch();
        }
    }
}";
        var (model, tree) = CreateModel(source);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var containingMethodId = PassUtilities.GetContainingMethodId(model, invocation);

        Assert.Null(containingMethodId);
    }

    [Fact]
    public void GetContainingMemberId_InvocationInsideConstructor_ReturnsConstructorId()
    {
        var source = @"
namespace MyApp
{
    public static class Helper { public static void Touch() { } }
    public class Service
    {
        public Service()
        {
            Helper.Touch();
        }
    }
}";
        var (model, tree) = CreateModel(source);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var containingMemberId = PassUtilities.GetContainingMemberId(model, invocation);

        Assert.Equal("MyApp.Service.Service()", containingMemberId);
    }

    [Fact]
    public void GetContainingMemberId_InvocationInsidePropertyGetter_ReturnsPropertyId()
    {
        var source = @"
namespace MyApp
{
    public static class Helper { public static int Read() => 42; }
    public class Service
    {
        public int Value => Helper.Read();
    }
}";
        var (model, tree) = CreateModel(source);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());

        var containingMemberId = PassUtilities.GetContainingMemberId(model, invocation);

        Assert.Equal("MyApp.Service.Value", containingMemberId);
    }

    [Fact]
    public void GetContainingMemberId_InvocationInsideEventAccessor_ReturnsEventId()
    {
        var source = @"
using System;
namespace MyApp
{
    public static class Helper { public static void Touch() { } }
    public class Service
    {
        public event Action Changed
        {
            add { Helper.Touch(); }
            remove { Helper.Touch(); }
        }
    }
}";
        var (model, tree) = CreateModel(source);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var containingMemberId = PassUtilities.GetContainingMemberId(model, invocation);

        Assert.Equal("MyApp.Service.Changed", containingMemberId);
    }

    [Fact]
    public void CreateExternalSymbolNode_GlobalNamespaceType_HasExpectedDefaults()
    {
        var (model, tree) = CreateModel("public class GlobalType { }");
        var declaration = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>());
        var symbol = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration));

        var node = PassUtilities.CreateExternalSymbolNode(symbol, SyntaxPass.GetSymbolId(symbol));

        Assert.Equal("GlobalType", node.Id);
        Assert.Equal("GlobalType", node.Name);
        Assert.Equal(NodeKind.Type, node.Kind);
        Assert.Equal(string.Empty, node.FilePath);
        Assert.Equal("GlobalType", node.Signature);
        Assert.Null(node.ContainingNamespaceId);
        Assert.NotNull(node.Metadata);
        Assert.Empty(node.Metadata);
    }

    [Fact]
    public void GetNodeKind_ConstructorSymbol_ReturnsConstructor()
    {
        var (model, tree) = CreateModel("namespace MyApp { public class Service { public Service() { } } }");
        var declaration = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>());
        var symbol = Assert.IsAssignableFrom<IMethodSymbol>(model.GetDeclaredSymbol(declaration));

        var nodeKind = PassUtilities.GetNodeKind(symbol);

        Assert.Equal(NodeKind.Constructor, nodeKind);
    }
}
