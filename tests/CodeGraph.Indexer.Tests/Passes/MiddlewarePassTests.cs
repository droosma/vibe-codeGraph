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

    [Fact]
    public void UseCall_OnNonApplicationBuilder_IsIgnored()
    {
        var source = @"
namespace MyApp
{
    public class FakeBuilder
    {
        public FakeBuilder UseAuthentication() => this;
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new FakeBuilder();
            app.UseAuthentication();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void UseMiddlewareWithoutGenericType_UsesPlaceholderId()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseMiddleware() => this;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = new Microsoft.AspNetCore.Builder.WebApplication();
            app.UseMiddleware();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("[Middleware:UseMiddleware]", edge.ToId);
        Assert.Equal("UseMiddleware", edge.Metadata["middlewareName"]);
        Assert.Equal("1", edge.Metadata["pipelineOrder"]);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void DuplicateUseMiddlewareCalls_CreateOneExternalNodeAndTwoOrderedEdges()
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
            app.UseMiddleware<CustomMiddleware>();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);
        var middlewareNode = Assert.Single(externalNodes, n => n.Id == "MyApp.CustomMiddleware");
        Assert.Equal("CustomMiddleware", middlewareNode.Name);
        Assert.Equal(NodeKind.Type, middlewareNode.Kind);
        Assert.Equal(string.Empty, middlewareNode.FilePath);
        Assert.Equal("MyApp.CustomMiddleware", middlewareNode.Signature);
        Assert.Equal(CodeGraph.Core.Models.Accessibility.Public, middlewareNode.Accessibility);
        Assert.Equal("1", edges[0].Metadata["pipelineOrder"]);
        Assert.Equal("2", edges[1].Metadata["pipelineOrder"]);
        Assert.All(edges, edge => Assert.Equal("CustomMiddleware", edge.Metadata["middlewareName"]));
    }

    [Fact]
    public void PipelineOrder_ResetsPerMethod()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseAuthentication() => this;
        public WebApplication MapControllers() => this;
    }
}

namespace MyApp
{
    public class Startup
    {
        public void Configure(Microsoft.AspNetCore.Builder.WebApplication app)
        {
            app.UseAuthentication();
            app.MapControllers();
        }

        public void ConfigureAdmin(Microsoft.AspNetCore.Builder.WebApplication app)
        {
            app.UseAuthentication();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(3, edges.Count);

        var configureEdges = edges.Where(e => e.FromId.Contains(".Configure(")).ToList();
        Assert.Equal(2, configureEdges.Count);
        Assert.Equal(new[] { "1", "2" }, configureEdges.Select(e => e.Metadata["pipelineOrder"]).ToArray());

        var adminEdge = Assert.Single(edges.Where(e => e.FromId.Contains(".ConfigureAdmin(")));
        Assert.Equal("1", adminEdge.Metadata["pipelineOrder"]);
        Assert.NotEqual(configureEdges[0].FromId, adminEdge.FromId);
    }

    [Fact]
    public void InterfaceTypedApplicationBuilder_EmitsMiddlewareEdge()
    {
        var source = @"
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder { }
    public static class AppBuilderExtensions
    {
        public static IApplicationBuilder UseAuthentication(this IApplicationBuilder app) => app;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IApplicationBuilder app = new WebApplication();
            app.UseAuthentication();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("[Middleware:UseAuthentication]", edge.ToId);
        Assert.Equal("UseAuthentication", edge.Metadata["middlewareName"]);
    }

    [Fact]
    public void EndpointRouteBuilder_MapCall_EmitsEdge()
    {
        var source = @"
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Builder
{
    public interface IEndpointRouteBuilder { }
    public class WebApplication : IEndpointRouteBuilder { }
    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapControllers(this IEndpointRouteBuilder app) => app;
    }
}

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IEndpointRouteBuilder app = new WebApplication();
            app.MapControllers();
        }
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("[Middleware:MapControllers]", edge.ToId);
        Assert.Equal("MapControllers", edge.Metadata["middlewareName"]);
        Assert.Equal("1", edge.Metadata["pipelineOrder"]);
    }

    [Fact]
    public void FieldInitializerUseCall_DoesNotEmitEdge()
    {
        var source = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder { }
    public class WebApplication : IApplicationBuilder
    {
        public WebApplication UseAuthentication() => this;
    }
}

namespace MyApp
{
    public class Startup
    {
        private readonly Microsoft.AspNetCore.Builder.WebApplication _app = new Microsoft.AspNetCore.Builder.WebApplication().UseAuthentication();
    }
}";

        var compilation = CreateCompilation(source);
        var pass = new MiddlewarePass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void KnownNodeIds_SuppressUseMiddlewareExternalNode()
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
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string> { "MyApp.CustomMiddleware" });

        Assert.Single(edges);
        Assert.Empty(externalNodes);
    }
}
