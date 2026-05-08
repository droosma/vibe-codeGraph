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
        public RouteAttribute(string template) { Template = template; }
        public string Template { get; }
    }
    public class HttpGetAttribute : System.Attribute
    {
        public HttpGetAttribute() { }
        public HttpGetAttribute(string template) { Template = template; }
        public string Template { get; }
    }
    public class HttpPostAttribute : System.Attribute
    {
        public HttpPostAttribute() { }
        public HttpPostAttribute(string template) { Template = template; }
        public string Template { get; }
    }
    public class HttpPutAttribute : System.Attribute
    {
        public HttpPutAttribute() { }
        public HttpPutAttribute(string template) { Template = template; }
        public string Template { get; }
    }
    public class HttpDeleteAttribute : System.Attribute
    {
        public HttpDeleteAttribute() { }
        public HttpDeleteAttribute(string template) { Template = template; }
        public string Template { get; }
    }
    public class HttpPatchAttribute : System.Attribute
    {
        public HttpPatchAttribute() { }
        public HttpPatchAttribute(string template) { Template = template; }
        public string Template { get; }
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
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal("MyApp.OrderController.GetById(int)", edge.FromId);
        Assert.Equal("GET /api/Order/{id}", edge.ToId);
        Assert.Equal("/api/Order/{id}", edge.Metadata["route"]);
        Assert.Equal("GET", edge.Metadata["httpMethod"]);
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
        Assert.Equal("POST /api/Order", edge.ToId);
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
        Assert.Contains(edges, e => e.ToId == "GET /api/Order");
        Assert.Contains(edges, e => e.ToId == "GET /api/Product");
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
        Assert.Equal("GET /api/Customer", edge.ToId);
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
        Assert.Equal("GET /api/Item", edge.ToId);
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
        Assert.Equal("GET /items/{id}", edge.ToId);
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
        Assert.Contains(edges, e => e.Metadata["httpMethod"] == "GET");
        Assert.Contains(edges, e => e.Metadata["httpMethod"] == "POST");
    }
}
