using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class RoutesPassTests
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

    private const string StubAttributes = @"
namespace Microsoft.AspNetCore.Mvc
{
    public class ControllerBase { }
    public class Controller : ControllerBase { }
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
    public class HttpPostAttribute : System.Attribute
    {
        public HttpPostAttribute() { }
        public HttpPostAttribute(string template) { Template = template; }
        public string Template { get; set; } = string.Empty;
    }
    public class HttpPutAttribute : System.Attribute
    {
        public HttpPutAttribute() { }
        public HttpPutAttribute(string template) { Template = template; }
        public string Template { get; set; } = string.Empty;
    }
    public class HttpDeleteAttribute : System.Attribute
    {
        public HttpDeleteAttribute() { }
        public HttpDeleteAttribute(string template) { Template = template; }
        public string Template { get; set; } = string.Empty;
    }
    public class HttpPatchAttribute : System.Attribute
    {
        public HttpPatchAttribute() { }
        public HttpPatchAttribute(string template) { Template = template; }
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
        public static object MapPost(this IEndpointRouteBuilder app, string pattern, System.Func<string> handler) => null;
        public static object MapPut(this IEndpointRouteBuilder app, string pattern, System.Func<string> handler) => null;
        public static object MapDelete(this IEndpointRouteBuilder app, string pattern, System.Func<string> handler) => null;
        public static object MapPatch(this IEndpointRouteBuilder app, string pattern, System.Func<string> handler) => null;
    }
}
";

    [Theory]
    [InlineData("HttpGet", "GET")]
    [InlineData("HttpPost", "POST")]
    [InlineData("HttpPut", "PUT")]
    [InlineData("HttpDelete", "DELETE")]
    [InlineData("HttpPatch", "PATCH")]
    public void HttpMethodAttributes_EmitCorrectHttpMethod(string attrName, string expectedMethod)
    {
        var code = StubAttributes + $@"
namespace MyApp
{{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {{
        [{attrName}(""{{id}}"")]
        public object GetById(int id) => null;
    }}
}}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.HandlesRoute, edge.Type);
        Assert.Equal(EdgeConfidence.Verified, edge.Confidence);
        Assert.Equal(expectedMethod, edge.Metadata["httpMethod"]);
    }

    [Fact]
    public void ClassRoute_CombinedWithMethodTemplate()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet(""{id}"")]
        public object GetById(int id) => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Order/{id}", edge.FromId);
        Assert.Equal("MyApp.OrderController.GetById(int)", edge.ToId);
        Assert.Equal("/api/Order/{id}", edge.Metadata["route"]);
        Assert.Equal("GET", edge.Metadata["httpMethod"]);

        var routeNode = Assert.Single(externalNodes.Where(node => node.Id == "GET /api/Order/{id}"));
        Assert.Equal("GET /api/Order/{id}", routeNode.Name);
        Assert.Equal(NodeKind.Property, routeNode.Kind);
        Assert.Equal("Route: GET /api/Order/{id}", routeNode.Signature);
    }

    [Fact]
    public void MethodWithoutTemplate_UsesClassRouteOnly()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpPost]
        public object Create(object order) => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("POST /api/Order", edge.FromId);
        Assert.Equal("MyApp.OrderController.Create(object)", edge.ToId);
        Assert.Equal("/api/Order", edge.Metadata["route"]);
    }

    [Fact]
    public void NonControllerClass_EmitsNoEdges()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    public class NotAController
    {
        [HttpGet(""test"")]
        public object Get() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }

    [Fact]
    public void MultipleControllers_EmitEdgesForAll()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class ProductController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.FromId == "GET /api/Order");
        Assert.Contains(edges, e => e.FromId == "GET /api/Product");
    }

    [Fact]
    public void ControllerPlaceholder_StripsControllerSuffix()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class CustomerController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Customer", edge.FromId);
    }

    [Fact]
    public void InheritsController_WithoutApiControllerAttribute_IsDetected()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [Route(""api/[controller]"")]
    public class ItemController : Controller
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Item", edge.FromId);
    }

    [Fact]
    public void NoClassRoute_UsesMethodTemplateOnly()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    public class SimpleController : ControllerBase
    {
        [HttpGet(""items/{id}"")]
        public object GetById(int id) => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /items/{id}", edge.FromId);
    }

    [Fact]
    public void MultipleHttpMethods_OnSameMethod_EmitMultipleEdges()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;
 
    [ApiController]
    [Route(""api/[controller]"")]
    public class DualController : ControllerBase
    {
        [HttpGet]
        [HttpPost]
        public object Handle() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.FromId == "GET /api/Dual" && e.Metadata["httpMethod"] == "GET");
        Assert.Contains(edges, e => e.FromId == "POST /api/Dual" && e.Metadata["httpMethod"] == "POST");
    }

    [Fact]
    public void ActionPlaceholder_IsResolvedInRouteTemplate()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]/[action]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object ListOpen() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Order/ListOpen", edge.FromId);
        Assert.Equal("/api/Order/ListOpen", edge.Metadata["route"]);
    }

    [Fact]
    public void NamedTemplateArguments_AreExtracted()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(Template = ""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet(Template = ""{id}"")]
        public object GetById(int id) => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /api/Order/{id}", edge.FromId);
    }

    [Fact]
    public void MethodRouteAttribute_WithoutHttpVerb_EmitsAnyRoute()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]"")]
    public class SearchController : ControllerBase
    {
        [Route(""find"")]
        public object Find() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("ANY /api/Search/find", edge.FromId);
        Assert.Equal("ANY", edge.Metadata["httpMethod"]);
    }

    [Fact]
    public void MinimalApi_MapGet_EmitsRouteToHandlerEdge()
    {
        var code = MinimalApiStubs + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Builder;

    public static class Program
    {
        public static void Configure(WebApplication app)
        {
            app.MapGet(""/orders/{id}"", HandleOrder);
        }

        public static string HandleOrder() => string.Empty;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("GET /orders/{id}", edge.FromId);
        Assert.Equal("MyApp.Program.HandleOrder()", edge.ToId);
        Assert.Equal("/orders/{id}", edge.Metadata["route"]);
        Assert.Equal("GET", edge.Metadata["httpMethod"]);

        Assert.Contains(externalNodes, node => node.Id == "GET /orders/{id}");
    }

    [Theory]
    [InlineData("MapPost", "POST")]
    [InlineData("MapPut", "PUT")]
    [InlineData("MapDelete", "DELETE")]
    [InlineData("MapPatch", "PATCH")]
    public void MinimalApi_AllMapMethods_EmitCorrectHttpMethod(string mapMethod, string expectedMethod)
    {
        var code = MinimalApiStubs + $@"
namespace MyApp
{{
    using Microsoft.AspNetCore.Builder;

    public static class Program
    {{
        public static void Configure(WebApplication app)
        {{
            app.{mapMethod}(""/items"", Handler);
        }}

        public static string Handler() => string.Empty;
    }}
}}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal($"{expectedMethod} /items", edge.FromId);
        Assert.Equal(expectedMethod, edge.Metadata["httpMethod"]);
    }

    [Fact]
    public void MinimalApi_WithTooFewArguments_EmitsNoEdges()
    {
        var code = @"
namespace Microsoft.AspNetCore.Builder
{
    public interface IEndpointRouteBuilder { }
    public sealed class WebApplication : IEndpointRouteBuilder { }
    public static class EndpointRouteBuilderExtensions
    {
        public static object MapGet(this IEndpointRouteBuilder app) => null;
    }
}
namespace MyApp
{
    using Microsoft.AspNetCore.Builder;
    public static class Program
    {
        public static void Configure(WebApplication app)
        {
            app.MapGet();
        }
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }

    [Fact]
    public void RouteWithDoubleSlashes_IsNormalized()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api//[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet(""//items"")]
        public object GetItems() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.DoesNotContain("//", edge.Metadata["route"]);
    }

    [Fact]
    public void RouteWithBackslashes_IsNormalized()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api\\[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.DoesNotContain("\\", edge.Metadata["route"]);
        Assert.StartsWith("/", edge.Metadata["route"]);
    }

    [Fact]
    public void RouteWithTrailingSlash_IsTrimmed()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]/"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.False(edge.Metadata["route"].EndsWith('/'));
    }

    [Fact]
    public void KnownNodeIds_SkipsExternalNodeCreation()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var knownIds = new HashSet<string> { "GET /api/Order", "MyApp.OrderController.GetAll()" };
        var (edges, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Single(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void EmptySolutionRoot_DoesNotThrow()
    {
        var code = StubAttributes + @"
namespace MyApp
{
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route(""api/[controller]"")]
    public class OrderController : ControllerBase
    {
        [HttpGet]
        public object GetAll() => null;
    }
}";

        var compilation = CreateCompilation(code);
        var pass = new RoutesPass();
        var (edges, _) = pass.Execute(compilation, "", new HashSet<string>());

        Assert.Single(edges);
    }
}
