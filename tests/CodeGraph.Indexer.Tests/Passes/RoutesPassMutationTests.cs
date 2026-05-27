using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class RoutesPassMutationTests
{
    private const string StubAttributes = @"
namespace Microsoft.AspNetCore.Mvc
{
    public class ControllerBase { }
    public class ApiControllerAttribute : System.Attribute { }
    public class RouteAttribute : System.Attribute
    {
        public RouteAttribute() { }
        public RouteAttribute(string template) { Template = template; }
        public string Template { get; set; } = string.Empty;
    }
    public class HttpGetAttribute : System.Attribute
    {
        public HttpGetAttribute() { }
        public HttpGetAttribute(string template) { Template = template; }
        public string Template { get; set; } = string.Empty;
    }
}
";

    private const string MinimalApiStubs = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IEndpointRouteBuilder { }
    public sealed class WebApplication : IEndpointRouteBuilder { }

    public static class EndpointRouteBuilderExtensions
    {
        public static object MapGet(this IEndpointRouteBuilder app, string pattern, System.Func<string> handler) => null;
    }
}
";

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"D:\repo\Routes.cs");
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
    public void EmptyControllerRoute_EmitsRootRoute()
    {
        var source = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("""")]
    public class RootController : ControllerBase
    {
        [HttpGet]
        public object Index() => null;
    }
}";
        var pass = new RoutesPass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /", edge.FromId);
        Assert.Equal("/", edge.Metadata["route"]);
        Assert.Equal("GET", edge.Metadata["httpMethod"]);
        var routeNode = Assert.Single(externalNodes.Where(node => node.Id == "GET /"));
        Assert.Equal("Route: GET /", routeNode.Signature);
        Assert.Equal("/", routeNode.Metadata["route"]);
    }

    [Fact]
    public void DuplicateRouteTemplates_EmitSingleUniqueEdge()
    {
        var source = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]"")]
    [Route(""api/[controller]"")]
    public class OrdersController : ControllerBase
    {
        [HttpGet(""items"")]
        [Route(""items"")]
        public object GetItems() => null;
    }
}";
        var pass = new RoutesPass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Orders/items", edge.FromId);
        Assert.Single(externalNodes.Where(node => node.Id == "GET /api/Orders/items"));
        Assert.Single(externalNodes.Where(node => node.Id == "MyApp.OrdersController.GetItems()"));
    }

    [Fact]
    public void ApiControllerAttributeWithoutControllerBase_EmitsRouteEdge()
    {
        var source = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]"")]
    public class StatusController
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";
        var pass = new RoutesPass();

        var (edges, _) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Status", edge.FromId);
        Assert.Equal("MyApp.StatusController.GetAll()", edge.ToId);
    }

    [Fact]
    public void MinimalApi_InterfaceTypedEndpointRouteBuilder_EmitsRouteEdge()
    {
        var source = MinimalApiStubs + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Builder;

    public static class Program
    {
        public static void Configure(IEndpointRouteBuilder app)
        {
            app.MapGet(""/status"", HandleStatus);
        }

        public static string HandleStatus() => string.Empty;
    }
}";
        var pass = new RoutesPass();

        var (edges, _) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /status", edge.FromId);
        Assert.Equal("MyApp.Program.HandleStatus()", edge.ToId);
    }

    [Fact]
    public void MinimalApi_NonLiteralRoute_DoesNotEmitEdge()
    {
        var source = MinimalApiStubs + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Builder;

    public static class Program
    {
        public static void Configure(WebApplication app)
        {
            var pattern = ""/status"";
            app.MapGet(pattern, HandleStatus);
        }

        public static string HandleStatus() => string.Empty;
    }
}";
        var pass = new RoutesPass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }
}
