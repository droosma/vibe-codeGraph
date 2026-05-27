using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class MiddlewarePassMutationTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"D:\repo\Middleware.cs");
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
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void ApplicationBuilderTypeInWrongNamespace_DoesNotEmitEdge()
    {
        var source = @"
namespace Fake.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseAuthentication() => this;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Fake.AspNetCore.Builder.WebApplication();
            app.UseAuthentication();
        }
    }
}";
        var pass = new MiddlewarePass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void UseMiddlewareWithTwoTypeArguments_UsesPlaceholderId()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseMiddleware<TFirst, TSecond>() => this;
    }
}

namespace MyApp
{
    public class FirstMiddleware { }
    public class SecondMiddleware { }

    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Microsoft.AspNetCore.Builder.WebApplication();
            app.UseMiddleware<FirstMiddleware, SecondMiddleware>();
        }
    }
}";
        var pass = new MiddlewarePass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("[Middleware:UseMiddleware]", edge.ToId);
        Assert.Equal("UseMiddleware", edge.Metadata["middlewareName"]);
        Assert.Equal("1", edge.Metadata["pipelineOrder"]);
        Assert.Empty(externalNodes);
    }
}
