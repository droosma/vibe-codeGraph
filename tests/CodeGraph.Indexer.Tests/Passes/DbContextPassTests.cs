using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class DbContextPassTests
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

        var linqExpressionsDll = Path.Combine(runtimeDir, "System.Linq.Expressions.dll");
        if (File.Exists(linqExpressionsDll))
            references.Add(MetadataReference.CreateFromFile(linqExpressionsDll));

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static readonly string StubTypes = @"
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
        public EntityTypeBuilder<T> HasKey(System.Linq.Expressions.Expression<System.Func<T, object>> keyExpression) => this;
        public ReferenceNavigationBuilder<T, TRelated> HasOne<TRelated>(System.Linq.Expressions.Expression<System.Func<T, TRelated>> navigationExpression) where TRelated : class => new();
        public CollectionNavigationBuilder<T, TRelated> HasMany<TRelated>(System.Linq.Expressions.Expression<System.Func<T, IEnumerable<TRelated>>> navigationExpression) where TRelated : class => new();
    }
    public class ReferenceNavigationBuilder<T, TRelated> where T : class where TRelated : class
    {
        public ReferenceNavigationBuilder<T, TRelated> WithMany() => this;
    }
    public class CollectionNavigationBuilder<T, TRelated> where T : class where TRelated : class
    {
        public CollectionNavigationBuilder<T, TRelated> WithOne(System.Linq.Expressions.Expression<System.Func<TRelated, T>> navigationExpression = null) => this;
    }
    public interface IEntityTypeConfiguration<T> where T : class
    {
        void Configure(EntityTypeBuilder<T> builder);
    }
}
";

    private static string MakeFullSource(string appCode)
    {
        return StubTypes + appCode;
    }

    [Fact]
    public void DbSetProperty_DiscoverEntityType_EmitsConventionMapsToTable()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Product
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; set; }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var tableEdge = Assert.Single(edges, e => e.Type == EdgeType.MapsToTable);
        Assert.Equal("MyApp.Product", tableEdge.FromId);
        Assert.Equal("[Table:Product]", tableEdge.ToId);
        Assert.Equal("Product", tableEdge.Metadata["tableName"]);
        Assert.Equal(EdgeConfidence.Inferred, tableEdge.Confidence);
    }

    [Fact]
    public void ToTable_EmitsMapsToTableEdge_WithTableName()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Order
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable(""Orders"");
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var tableEdge = Assert.Single(edges, e => e.Type == EdgeType.MapsToTable);
        Assert.Equal("MyApp.Order", tableEdge.FromId);
        Assert.Equal("[Table:Orders]", tableEdge.ToId);
        Assert.Equal("Orders", tableEdge.Metadata["tableName"]);
        Assert.Equal(EdgeConfidence.Verified, tableEdge.Confidence);
    }

    [Fact]
    public void HasMany_WithOne_EmitsNavigatesToEdge()
    {
        var source = MakeFullSource(@"
using System.Collections.Generic;

namespace MyApp
{
    public class Order
    {
        public int Id { get; set; }
        public List<OrderLine> Lines { get; set; }
    }
    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order Order { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<OrderLine> OrderLines { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasMany(o => o.Lines).WithOne(l => l.Order);
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var navEdge = Assert.Single(edges, e => e.Type == EdgeType.NavigatesTo);
        Assert.Equal("MyApp.Order", navEdge.FromId);
        Assert.Equal("MyApp.OrderLine", navEdge.ToId);
        Assert.Equal("one-to-many", navEdge.Metadata["relationship"]);
        Assert.Equal("Lines", navEdge.Metadata["property"]);
    }

    [Fact]
    public void HasOne_EmitsNavigatesToEdge_WithManyToOne()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order Order { get; set; }
    }
    public class Order
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<OrderLine> OrderLines { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderLine>().HasOne<Order>(ol => ol.Order);
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var navEdge = Assert.Single(edges, e => e.Type == EdgeType.NavigatesTo);
        Assert.Equal("MyApp.OrderLine", navEdge.FromId);
        Assert.Equal("MyApp.Order", navEdge.ToId);
        Assert.Equal("many-to-one", navEdge.Metadata["relationship"]);
        Assert.Equal("Order", navEdge.Metadata["property"]);
    }

    [Fact]
    public void MultipleDbContexts_EmitsEdgesForEach()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class User { public int Id { get; set; } }
    public class Product { public int Id { get; set; } }

    public class UserDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable(""Users"");
        }
    }

    public class ProductDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().ToTable(""Products"");
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var tableEdges = edges.Where(e => e.Type == EdgeType.MapsToTable).ToList();
        Assert.Equal(2, tableEdges.Count);

        Assert.Contains(tableEdges, e => e.FromId == "MyApp.User" && e.Metadata["tableName"] == "Users");
        Assert.Contains(tableEdges, e => e.FromId == "MyApp.Product" && e.Metadata["tableName"] == "Products");
    }

    [Fact]
    public void NoDbContext_ProducesNoEdges()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class SomeClass
    {
        public int Id { get; set; }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void DbSetWithoutFluentConfig_UsesConventionTableName()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Customer
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Customer> Customers { get; set; }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var tableEdge = Assert.Single(edges, e => e.Type == EdgeType.MapsToTable);
        Assert.Equal("MyApp.Customer", tableEdge.FromId);
        Assert.Equal("[Table:Customer]", tableEdge.ToId);
        Assert.Equal("Customer", tableEdge.Metadata["tableName"]);
        Assert.Equal(EdgeConfidence.Inferred, tableEdge.Confidence);
    }

    [Fact]
    public void EntityTypeConfiguration_EmitsConfiguredByEdge()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Invoice
    {
        public int Id { get; set; }
    }

    public class InvoiceConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<Invoice>
    {
        public void Configure(Microsoft.EntityFrameworkCore.EntityTypeBuilder<Invoice> builder)
        {
            builder.ToTable(""Invoices"");
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var configEdge = Assert.Single(edges, e => e.Type == EdgeType.ConfiguredBy);
        Assert.Equal("MyApp.Invoice", configEdge.FromId);
        Assert.Equal("MyApp.InvoiceConfiguration", configEdge.ToId);
        Assert.Equal("MyApp.InvoiceConfiguration", configEdge.Metadata["configurationClass"]);
    }

    [Fact]
    public void ExternalNodes_AreCreatedForDiscoveredEntities()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Order
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable(""Orders"");
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (_, externalNodes) = pass.Execute(compilation, "/root", new HashSet<string>());

        var orderNode = Assert.Single(externalNodes, n => n.Id == "MyApp.Order");
        Assert.Equal("Order", orderNode.Name);
        Assert.Equal(NodeKind.Type, orderNode.Kind);
    }

    [Fact]
    public void KnownNodeIds_SuppressExternalNodeCreation()
    {
        var source = MakeFullSource(@"
namespace MyApp
{
    public class Order
    {
        public int Id { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable(""Orders"");
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var knownIds = new HashSet<string> { "MyApp.Order" };
        var (edges, externalNodes) = pass.Execute(compilation, "/root", knownIds);

        Assert.Single(edges);
        Assert.DoesNotContain(externalNodes, n => n.Id == "MyApp.Order");
    }

    [Fact]
    public void FullScenario_ToTableAndNavigation_EmitsBothEdgeTypes()
    {
        var source = MakeFullSource(@"
using System.Collections.Generic;

namespace MyApp
{
    public class Order
    {
        public int Id { get; set; }
        public List<OrderLine> Lines { get; set; }
    }
    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order Order { get; set; }
    }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Order> Orders { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<OrderLine> OrderLines { get; set; }

        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable(""Orders"");
            modelBuilder.Entity<Order>().HasMany(o => o.Lines).WithOne(l => l.Order);
        }
    }
}");

        var compilation = CreateCompilation(source);
        var pass = new DbContextPass();
        var (edges, _) = pass.Execute(compilation, "/root", new HashSet<string>());

        var tableEdge = Assert.Single(edges, e => e.Type == EdgeType.MapsToTable && e.Metadata["tableName"] == "Orders");
        Assert.Equal("MyApp.Order", tableEdge.FromId);

        var navEdge = Assert.Single(edges, e => e.Type == EdgeType.NavigatesTo);
        Assert.Equal("MyApp.Order", navEdge.FromId);
        Assert.Equal("MyApp.OrderLine", navEdge.ToId);
        Assert.Equal("one-to-many", navEdge.Metadata["relationship"]);

        // OrderLine should have convention table mapping since no explicit ToTable
        var conventionEdge = Assert.Single(edges, e => e.Type == EdgeType.MapsToTable && e.FromId == "MyApp.OrderLine");
        Assert.Equal(EdgeConfidence.Inferred, conventionEdge.Confidence);
    }
}
