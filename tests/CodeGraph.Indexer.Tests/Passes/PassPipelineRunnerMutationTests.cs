using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using CodeGraph.Indexer.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class PassPipelineRunnerMutationTests
{
    private const string RouteSource = @"
namespace Microsoft.AspNetCore.Mvc
{
    public class ControllerBase { }
    public class ApiControllerAttribute : System.Attribute { }
    public class RouteAttribute : System.Attribute
    {
        public RouteAttribute(string template) { }
    }
    public class HttpGetAttribute : System.Attribute
    {
        public HttpGetAttribute(string template) { }
    }
}
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

    private const string ConfigurationSource = @"
namespace Microsoft.Extensions.Configuration
{
    public interface IConfiguration
    {
        IConfigurationSection GetSection(string key);
    }
    public interface IConfigurationSection : IConfiguration { }
}
namespace Microsoft.Extensions.DependencyInjection
{
    public static class OptionsConfigurationServiceCollectionExtensions
    {
        public static void Configure<T>(this object services, Microsoft.Extensions.Configuration.IConfigurationSection section) where T : class { }
    }
}
namespace MyApp
{
    public class PaymentOptions { }
    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment:Gateway""));
        }
    }
}";

    private const string MiddlewareSource = @"
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

    private const string DbContextSource = @"
using System.Collections.Generic;
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
    }
    public class DbSet<T> where T : class { }
    public class ModelBuilder
    {
        public EntityTypeBuilder<T> Entity<T>() where T : class => new EntityTypeBuilder<T>();
    }
    public class EntityTypeBuilder<T> where T : class
    {
        public EntityTypeBuilder<T> ToTable(string name) => this;
    }
}
namespace MyApp
{
    public class Product { }
    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; set; }
    }
}";

    private static List<MetadataReference> CreateReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dllName in new[] { "System.Runtime.dll", "System.Linq.dll", "System.Collections.dll", "System.Linq.Expressions.dll" })
        {
            var path = Path.Combine(runtimeDir, dllName);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, path: $@"D:\repo\{assemblyName}.cs") },
            CreateReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ProjectCompilation CreateProjectCompilation(string source, string assemblyName = "TestAssembly")
    {
        var compilation = CreateCompilation(source, assemblyName);
        return new ProjectCompilation(assemblyName, $@"D:\repo\{assemblyName}.csproj", assemblyName, "net8.0", compilation);
    }

    private static ProjectCompilation CreateTwoAssemblyProjectCompilation(string productionSource, string testSource)
    {
        var references = CreateReferences();
        var prodCompilation = CSharpCompilation.Create(
            "ProductionAssembly",
            new[] { CSharpSyntaxTree.ParseText(productionSource, path: @"D:\repo\Production.cs") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var testReferences = new List<MetadataReference>(references)
        {
            prodCompilation.ToMetadataReference()
        };

        var testCompilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(testSource, path: @"D:\repo\Tests.cs") },
            testReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return new ProjectCompilation("TestAssembly", @"D:\repo\Tests.csproj", "TestAssembly", "net8.0", testCompilation);
    }

    [Fact]
    public void OptionalConstructorParameters_DefaultUnspecifiedFlagsToTrue()
    {
        var options = new PassPipelineOptions(EnableDbContextPass: false);

        Assert.True(options.EnableRoutesPass);
        Assert.True(options.EnableConfigurationPass);
        Assert.True(options.EnableMiddlewarePass);
        Assert.False(options.EnableDbContextPass);
    }

    [Fact]
    public void RoutesOption_Disabled_RemovesRouteEdges()
    {
        var project = CreateProjectCompilation(RouteSource);
        var enabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: true,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));
        var disabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));

        var routeEdge = Assert.Single(enabled.Edges.Where(e => e.Type == EdgeType.HandlesRoute));
        Assert.Equal("GET /api/Order/{id}", routeEdge.FromId);
        Assert.Equal("MyApp.OrderController.GetById(int)", routeEdge.ToId);
        Assert.DoesNotContain(disabled.Edges, e => e.Type == EdgeType.HandlesRoute);
    }

    [Fact]
    public void ConfigurationOption_Disabled_RemovesConfigurationEdges()
    {
        var project = CreateProjectCompilation(ConfigurationSource);
        var enabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: true,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));
        var disabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));

        var edge = Assert.Single(enabled.Edges.Where(e => e.Type == EdgeType.BindsConfiguration));
        Assert.Equal("MyApp.PaymentOptions", edge.FromId);
        Assert.Equal("[Config:Payment:Gateway]", edge.ToId);
        Assert.DoesNotContain(disabled.Edges, e => e.Type == EdgeType.BindsConfiguration);
    }

    [Fact]
    public void MiddlewareOption_Disabled_RemovesMiddlewareEdges()
    {
        var project = CreateProjectCompilation(MiddlewareSource);
        var enabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: true,
            EnableDbContextPass: false));
        var disabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));

        Assert.Equal(4, enabled.Edges.Count(e => e.Type == EdgeType.UsesMiddleware));
        Assert.DoesNotContain(disabled.Edges, e => e.Type == EdgeType.UsesMiddleware);
    }

    [Fact]
    public void DbContextOption_Disabled_RemovesDbContextEdges()
    {
        var project = CreateProjectCompilation(DbContextSource);
        var enabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: true));
        var disabled = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));

        var edge = Assert.Single(enabled.Edges.Where(e => e.Type == EdgeType.MapsToTable));
        Assert.Equal("MyApp.Product", edge.FromId);
        Assert.Equal("[Table:Product]", edge.ToId);
        Assert.DoesNotContain(disabled.Edges, e => e.Type == EdgeType.MapsToTable);
    }

    [Fact]
    public void ExternalNodesFromEarlierPasses_AreNotDuplicatedByLaterPasses()
    {
        const string productionSource = @"
namespace MyApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}";
        const string testSource = @"
namespace Xunit { public class FactAttribute : System.Attribute { } }
namespace MyApp.Tests
{
    public class CalculatorTests
    {
        public int Helper()
        {
            var c = new MyApp.Calculator();
            return c.Add(3, 4);
        }

        [Xunit.Fact]
        public void Add_IsCovered()
        {
            var c = new MyApp.Calculator();
            c.Add(1, 2);
        }
    }
}";
        var project = CreateTwoAssemblyProjectCompilation(productionSource, testSource);

        var result = PassPipelineRunner.Execute(project, @"D:\repo", new PassPipelineOptions(
            EnableRoutesPass: false,
            EnableConfigurationPass: false,
            EnableMiddlewarePass: false,
            EnableDbContextPass: false));

        Assert.Equal(1, result.Nodes.Count(n => n.Id == "MyApp.Calculator.Add(int, int)"));
        Assert.Contains(result.Edges, e =>
            e.FromId == "MyApp.Tests.CalculatorTests.Helper()" &&
            e.ToId == "MyApp.Calculator.Add(int, int)" &&
            e.Type == EdgeType.Calls);
        Assert.Contains(result.Edges, e =>
            e.FromId == "MyApp.Tests.CalculatorTests.Add_IsCovered()" &&
            e.ToId == "MyApp.Calculator.Add(int, int)" &&
            e.Type == EdgeType.Covers);
        Assert.Contains(result.Edges, e =>
            e.FromId == "MyApp.Calculator.Add(int, int)" &&
            e.ToId == "MyApp.Tests.CalculatorTests.Add_IsCovered()" &&
            e.Type == EdgeType.CoveredBy);
    }
}
