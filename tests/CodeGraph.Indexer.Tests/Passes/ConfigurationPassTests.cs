using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class ConfigurationPassTests
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

    private const string StubTypes = @"
namespace Microsoft.Extensions.Configuration
{
    public interface IConfiguration
    {
        IConfigurationSection GetSection(string key);
    }
    public interface IConfigurationSection : IConfiguration
    {
        string Key { get; }
        string Value { get; }
    }
}
namespace Microsoft.Extensions.DependencyInjection
{
    public static class OptionsConfigurationServiceCollectionExtensions
    {
        public static void Configure<T>(this object services, Microsoft.Extensions.Configuration.IConfigurationSection section) where T : class { }
    }
    public static class OptionsServiceCollectionExtensions
    {
        public static OptionsBuilder<T> AddOptions<T>(this object services) where T : class => new();
    }
    public class OptionsBuilder<T> where T : class
    {
        public OptionsBuilder<T> Bind(Microsoft.Extensions.Configuration.IConfigurationSection section) => this;
        public OptionsBuilder<T> ValidateDataAnnotations() => this;
    }
}
";

    [Fact]
    public void Configure_WithGetSection_EmitsBindsConfigurationEdge()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions
    {
        public string ApiKey { get; set; }
    }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment:Gateway""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.BindsConfiguration, edge.Type);
        Assert.Equal("MyApp.PaymentOptions", edge.FromId);
        Assert.Equal("[Config:Payment:Gateway]", edge.ToId);
        Assert.Equal(EdgeConfidence.Verified, edge.Confidence);
        Assert.Equal("Payment:Gateway", edge.Metadata["section"]);
        Assert.Equal("Configure", edge.Metadata["registrationMethod"]);
    }

    [Fact]
    public void AddOptions_Bind_WithGetSection_EmitsBindsConfigurationEdge()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class CacheOptions
    {
        public int Duration { get; set; }
    }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.AddOptions<CacheOptions>().Bind(config.GetSection(""Caching:Redis""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.BindsConfiguration, edge.Type);
        Assert.Equal("MyApp.CacheOptions", edge.FromId);
        Assert.Equal("[Config:Caching:Redis]", edge.ToId);
        Assert.Equal("Caching:Redis", edge.Metadata["section"]);
        Assert.Equal("AddOptions", edge.Metadata["registrationMethod"]);
    }

    [Fact]
    public void MultipleBindings_EmitMultipleEdges()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }
    public class LoggingOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment""));
            services.Configure<LoggingOptions>(config.GetSection(""Logging""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, edges.Count);

        var payment = edges.Single(e => e.FromId == "MyApp.PaymentOptions");
        Assert.Equal("[Config:Payment]", payment.ToId);
        Assert.Equal("Payment", payment.Metadata["section"]);

        var logging = edges.Single(e => e.FromId == "MyApp.LoggingOptions");
        Assert.Equal("[Config:Logging]", logging.ToId);
        Assert.Equal("Logging", logging.Metadata["section"]);
    }

    [Fact]
    public void NoConfigurationCalls_ReturnsEmpty()
    {
        var code = @"
namespace MyApp
{
    public class PaymentOptions
    {
        public string ApiKey { get; set; }
    }

    public class Startup
    {
        public void ConfigureServices()
        {
            var x = 42;
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void DynamicSectionPath_SkipsEdge()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            var sectionName = ""Payment"";
            services.Configure<PaymentOptions>(config.GetSection(sectionName));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
    }

    [Fact]
    public void ExternalNodes_CreatedForUnknownIds()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Equal(2, externalNodes.Count);

        var optionsNode = externalNodes.Single(n => n.Id == "MyApp.PaymentOptions");
        Assert.Equal("PaymentOptions", optionsNode.Name);
        Assert.Equal(NodeKind.Type, optionsNode.Kind);
        Assert.Equal("MyApp", optionsNode.ContainingNamespaceId);

        var configNode = externalNodes.Single(n => n.Id == "[Config:Payment]");
        Assert.Equal("Payment", configNode.Name);
    }

    [Fact]
    public void KnownNodeIds_SuppressExternalNodeCreation()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var knownIds = new HashSet<string> { "MyApp.PaymentOptions", "[Config:Payment]" };
        var (edges, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Single(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void AddOptions_WithValidateDataAnnotations_StoresValidationMetadata()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.AddOptions<PaymentOptions>().Bind(config.GetSection(""Payment"")).ValidateDataAnnotations();
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.Equal(EdgeType.BindsConfiguration, edge.Type);
        Assert.Equal("MyApp.PaymentOptions", edge.FromId);
        Assert.Equal("[Config:Payment]", edge.ToId);
        Assert.Equal("AddOptions", edge.Metadata["registrationMethod"]);
        Assert.Equal("DataAnnotations", edge.Metadata["validation"]);
    }

    [Fact]
    public void AllEdges_HaveRequiredMetadataKeys()
    {
        var code = StubTypes + @"
namespace MyApp
{
    public class PaymentOptions { }

    public class Startup
    {
        public void ConfigureServices(object services, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            services.Configure<PaymentOptions>(config.GetSection(""Payment""));
        }
    }
}";
        var compilation = CreateCompilation(code);
        var pass = new ConfigurationPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var edge = Assert.Single(edges);
        Assert.True(edge.Metadata.ContainsKey("section"), "Missing 'section' metadata key");
        Assert.True(edge.Metadata.ContainsKey("registrationMethod"), "Missing 'registrationMethod' metadata key");
        Assert.True(edge.Metadata.ContainsKey("registrationFile"), "Missing 'registrationFile' metadata key");
        Assert.NotEmpty(edge.Metadata["section"]);
        Assert.NotEmpty(edge.Metadata["registrationMethod"]);
    }
}
