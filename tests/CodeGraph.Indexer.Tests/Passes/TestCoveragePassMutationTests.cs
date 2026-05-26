using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class TestCoveragePassMutationTests
{
    private const string ProductionSource = @"
namespace MyApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
        public int Subtract(int a, int b) => a - b;
    }
}";

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();
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
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpCompilation CreateTwoAssemblyScenario(string productionSource, string testSource)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeDll = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(runtimeDll))
            references.Add(MetadataReference.CreateFromFile(runtimeDll));

        var prodTree = CSharpSyntaxTree.ParseText(productionSource);
        var prodCompilation = CSharpCompilation.Create("ProductionAssembly",
            new[] { prodTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var testTree = CSharpSyntaxTree.ParseText(testSource);
        var testReferences = new List<MetadataReference>(references)
        {
            prodCompilation.ToMetadataReference()
        };

        return CSharpCompilation.Create("TestAssembly",
            new[] { testTree },
            testReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static HashSet<string> GetKnownNodeIds(CSharpCompilation compilation)
    {
        var (nodes, _) = new SyntaxPass().Execute(compilation, string.Empty);
        return new HashSet<string>(nodes.Select(n => n.Id));
    }

    private static string MakeTestSource(string attributeLine, string methodName = "Add_ReturnsSum", string invocation = "c.Add(1, 2);") => $@"
namespace Xunit {{ public class FactAttribute : System.Attribute {{ }} }}
namespace MyApp.Tests
{{
    public class CalculatorTests
    {{
        [{attributeLine}]
        public void {methodName}()
        {{
            var c = new MyApp.Calculator();
            {invocation}
        }}
    }}
}}";

    [Fact]
    public void SameAssemblyCall_IsIgnoredAsTestInfrastructure()
    {
        var source = @"
namespace Xunit { public class FactAttribute : System.Attribute { } }
namespace MyApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}
namespace MyApp.Tests
{
    public class CalculatorTests
    {
        [Xunit.Fact]
        public void Add_ReturnsSum()
        {
            var c = new MyApp.Calculator();
            c.Add(1, 2);
        }
    }
}";
        var compilation = CreateCompilation(source);
        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, string.Empty, GetKnownNodeIds(compilation));

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void SingleExternalCall_CreatesExactBidirectionalEdgesAndExternalNode()
    {
        var compilation = CreateTwoAssemblyScenario(ProductionSource, MakeTestSource("Xunit.Fact"));
        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, string.Empty, GetKnownNodeIds(compilation));

        Assert.Equal(2, edges.Count);

        var covers = Assert.Single(edges, e => e.Type == EdgeType.Covers);
        Assert.Equal("MyApp.Tests.CalculatorTests.Add_ReturnsSum()", covers.FromId);
        Assert.Equal("MyApp.Calculator.Add(int, int)", covers.ToId);
        Assert.True(covers.IsExternal);
        Assert.Equal(EdgeConfidence.Verified, covers.Confidence);
        Assert.Equal("xUnit", covers.Metadata["testFramework"]);

        var coveredBy = Assert.Single(edges, e => e.Type == EdgeType.CoveredBy);
        Assert.Equal("MyApp.Calculator.Add(int, int)", coveredBy.FromId);
        Assert.Equal("MyApp.Tests.CalculatorTests.Add_ReturnsSum()", coveredBy.ToId);
        Assert.True(coveredBy.IsExternal);
        Assert.Equal(EdgeConfidence.Verified, coveredBy.Confidence);
        Assert.Equal("xUnit", coveredBy.Metadata["testFramework"]);

        var externalNode = Assert.Single(externalNodes);
        Assert.Equal("MyApp.Calculator.Add(int, int)", externalNode.Id);
        Assert.Equal("Add", externalNode.Name);
        Assert.Equal(NodeKind.Method, externalNode.Kind);
        Assert.Equal(string.Empty, externalNode.FilePath);
        Assert.Equal("MyApp.Calculator.Add(int, int)", externalNode.Signature);
        Assert.Equal(CodeGraph.Core.Models.Accessibility.Public, externalNode.Accessibility);
        Assert.Equal("ProductionAssembly", externalNode.Metadata["assembly"]);
    }

    [Fact]
    public void KnownTargetId_SuppressesExternalNodeCreation()
    {
        var compilation = CreateTwoAssemblyScenario(ProductionSource, MakeTestSource("Xunit.Fact"));
        var knownNodeIds = GetKnownNodeIds(compilation);
        knownNodeIds.Add("MyApp.Calculator.Add(int, int)");

        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, string.Empty, knownNodeIds);

        Assert.Equal(2, edges.Count);
        Assert.Empty(externalNodes);
    }

    [Fact]
    public void MultipleTestsCallingSameMethod_CreateOneExternalNode()
    {
        var testSource = @"
namespace Xunit { public class FactAttribute : System.Attribute { } }
namespace MyApp.Tests
{
    public class CalculatorTests
    {
        [Xunit.Fact]
        public void Add_First()
        {
            var c = new MyApp.Calculator();
            c.Add(1, 2);
        }

        [Xunit.Fact]
        public void Add_Second()
        {
            var c = new MyApp.Calculator();
            c.Add(3, 4);
        }
    }
}";
        var compilation = CreateTwoAssemblyScenario(ProductionSource, testSource);
        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, string.Empty, GetKnownNodeIds(compilation));

        Assert.Equal(4, edges.Count);
        Assert.Equal(2, edges.Count(e => e.Type == EdgeType.Covers));
        Assert.Equal(2, edges.Count(e => e.Type == EdgeType.CoveredBy));
        var externalNode = Assert.Single(externalNodes);
        Assert.Equal("MyApp.Calculator.Add(int, int)", externalNode.Id);
    }

    [Fact]
    public void GlobalQualifiedFactAttribute_IsRecognized()
    {
        var compilation = CreateTwoAssemblyScenario(ProductionSource, MakeTestSource("global::Xunit.FactAttribute", "Qualified_Test"));
        var pass = new TestCoveragePass();
        var (edges, _) = pass.Execute(compilation, string.Empty, GetKnownNodeIds(compilation));

        Assert.Equal(2, edges.Count);
        Assert.All(edges, edge => Assert.Equal("xUnit", edge.Metadata["testFramework"]));
    }

    [Fact]
    public void UnresolvedInvocationTarget_IsIgnored()
    {
        var testSource = @"
namespace Xunit { public class FactAttribute : System.Attribute { } }
namespace MyApp.Tests
{
    public class BrokenTests
    {
        [Xunit.Fact]
        public void MissingCall()
        {
            Missing();
        }
    }
}";
        var compilation = CreateTwoAssemblyScenario(ProductionSource, testSource);
        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, string.Empty, GetKnownNodeIds(compilation));

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }
}
