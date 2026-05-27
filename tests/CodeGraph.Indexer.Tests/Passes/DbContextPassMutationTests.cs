using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class DbContextPassMutationTests
{
    private const string StubTypes = @"
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
        public CollectionNavigationBuilder<T, TRelated> HasMany<TRelated>() where TRelated : class => new CollectionNavigationBuilder<T, TRelated>();
    }

    public class CollectionNavigationBuilder<T, TRelated> where T : class where TRelated : class
    {
        public CollectionNavigationBuilder<T, TRelated> WithOne(System.Linq.Expressions.Expression<System.Func<TRelated, T>> navigationExpression = null) => this;
    }
}

namespace FakeEntityFramework
{
    public class DbSet<T> where T : class { }
}
";

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"D:\repo\DbContext.cs");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dllName in new[] { "System.Runtime.dll", "System.Linq.Expressions.dll", "System.Collections.dll" })
        {
            var path = Path.Combine(runtimeDir, dllName);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void DerivedDbContextSubclass_StillEmitsConventionTableMapping()
    {
        var source = StubTypes + @"
namespace MyApp
{
    public class Product { }

    public class BaseContext : Microsoft.EntityFrameworkCore.DbContext { }

    public class AppDbContext : BaseContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; set; }
    }
}";
        var pass = new DbContextPass();

        var (edges, _) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var edge = Assert.Single(edges, candidate => candidate.Type == EdgeType.MapsToTable);
        Assert.Equal("MyApp.Product", edge.FromId);
        Assert.Equal("[Table:Product]", edge.ToId);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal("Product", edge.Metadata["tableName"]);
    }

    [Fact]
    public void NonEntityFrameworkDbSetProperty_DoesNotEmitTableEdge()
    {
        var source = StubTypes + @"
namespace MyApp
{
    public class Product { }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public FakeEntityFramework.DbSet<Product> Products { get; set; }
    }
}";
        var pass = new DbContextPass();

        var (edges, externalNodes) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void HasManyWithoutLambdaArgument_OmitsPropertyMetadata()
    {
        var source = StubTypes + @"
namespace MyApp
{
    public class Order { }
    public class OrderLine
    {
        public Order Order { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<OrderLine> OrderLines { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasMany<OrderLine>().WithOne(line => line.Order);
        }
    }
}";
        var pass = new DbContextPass();

        var (edges, _) = pass.Execute(CreateCompilation(source), @"D:\repo", new HashSet<string>());

        var navigationEdge = Assert.Single(edges, candidate => candidate.Type == EdgeType.NavigatesTo);
        Assert.Equal("MyApp.Order", navigationEdge.FromId);
        Assert.Equal("MyApp.OrderLine", navigationEdge.ToId);
        Assert.Equal("one-to-many", navigationEdge.Metadata["relationship"]);
        Assert.False(navigationEdge.Metadata.ContainsKey("property"));
    }
}
