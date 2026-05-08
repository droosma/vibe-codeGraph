using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class MiddlewarePassTests
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

    private const string StandardPipelineSource = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseHttpsRedirection() => this;
        public WebApplication UseAuthentication() => this;
        public WebApplication UseAuthorization() => this;
        public WebApplication MapControllers() => this;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Microsoft.AspNetCore.Builder.WebApplication();
            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
        }
    }
}";

    [Fact]
    public void MultipleUseCalls_EmitEdgesWithSequentialPipelineOrder()
    {
        var compilation = CreateCompilation(StandardPipelineSource);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(4, edges.Count);

        Assert.All(edges, e =>
        {
            Assert.Equal(EdgeType.UsesMiddleware, e.Type);
            Assert.Equal(EdgeConfidence.Verified, e.Confidence);
            Assert.Equal("MyApp.Program.Main(string[])", e.FromId);
        });
    }

    [Fact]
    public void PipelineOrder_MatchesSourceCodeOrder()
    {
        var compilation = CreateCompilation(StandardPipelineSource);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(4, edges.Count);

        Assert.Equal("[Middleware:UseHttpsRedirection]", edges[0].ToId);
        Assert.Equal("1", edges[0].Metadata["pipelineOrder"]);
        Assert.Equal("UseHttpsRedirection", edges[0].Metadata["middlewareName"]);

        Assert.Equal("[Middleware:UseAuthentication]", edges[1].ToId);
        Assert.Equal("2", edges[1].Metadata["pipelineOrder"]);
        Assert.Equal("UseAuthentication", edges[1].Metadata["middlewareName"]);

        Assert.Equal("[Middleware:UseAuthorization]", edges[2].ToId);
        Assert.Equal("3", edges[2].Metadata["pipelineOrder"]);
        Assert.Equal("UseAuthorization", edges[2].Metadata["middlewareName"]);

        Assert.Equal("[Middleware:MapControllers]", edges[3].ToId);
        Assert.Equal("4", edges[3].Metadata["pipelineOrder"]);
        Assert.Equal("MapControllers", edges[3].Metadata["middlewareName"]);
    }

    [Fact]
    public void UseMiddlewareGeneric_ResolvesToMiddlewareType()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseMiddleware<T>() => this;
    }
}

namespace MyApp
{
    public class CustomMiddleware { }

    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Microsoft.AspNetCore.Builder.WebApplication();
            app.UseMiddleware<CustomMiddleware>();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.UsesMiddleware, edge.Type);
        Assert.Equal("MyApp.Program.Main(string[])", edge.FromId);
        Assert.Equal("MyApp.CustomMiddleware", edge.ToId);
        Assert.Equal("1", edge.Metadata["pipelineOrder"]);
        Assert.Equal("CustomMiddleware", edge.Metadata["middlewareName"]);
    }

    [Fact]
    public void NonMiddlewareCalls_AreNotCaptured()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseAuthentication() => this;
        public void Run() { }
        public void Build() { }
        public string GetSetting(string key) => key;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Microsoft.AspNetCore.Builder.WebApplication();
            app.UseAuthentication();
            app.Run();
            app.Build();
            app.GetSetting(""test"");
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("[Middleware:UseAuthentication]", edge.ToId);
    }

    [Fact]
    public void NoMiddlewareCalls_NoEdges()
    {
        var source = @"
namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var x = 42;
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }
}
