using CodeGraph.Core.Models;
using CodeGraph.Indexer.Passes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Tests.Passes;

public class TestCoveragePassTests
{
    private static readonly string ProductionSource = @"
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

    private static string MakeTestSource(string attrNamespace, string attrName, string fullyQualifiedAttr) => $@"
namespace {attrNamespace} {{ public class {attrName} : System.Attribute {{ }} }}
namespace MyApp.Tests
{{
    public class CalculatorTests
    {{
        [{fullyQualifiedAttr}]
        public void Add_ReturnsSum()
        {{
            var c = new MyApp.Calculator();
            c.Add(1, 2);
        }}
    }}
}}";

    private static (List<GraphEdge> edges, List<GraphNode> externalNodes) RunTestCoveragePass(
        string productionSource, string testSource)
    {
        var compilation = CreateTwoAssemblyScenario(productionSource, testSource);
        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(compilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var pass = new TestCoveragePass();
        return pass.Execute(compilation, "", knownIds);
    }

    // ── xUnit: Fact and FactAttribute ──

    [Fact]
    public void XUnit_Fact_EmitsEdges_WithXUnitFramework()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("xUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void XUnit_FactAttribute_EmitsEdges_WithXUnitFramework()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.FactAttribute");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("xUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void XUnit_Theory_EmitsEdges_WithXUnitFramework()
    {
        var testSource = MakeTestSource("Xunit", "TheoryAttribute", "Xunit.Theory");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("xUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void XUnit_TheoryAttribute_EmitsEdges_WithXUnitFramework()
    {
        var testSource = MakeTestSource("Xunit", "TheoryAttribute", "Xunit.TheoryAttribute");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("xUnit", e.Metadata["testFramework"]));
    }

    // ── NUnit: Test, TestAttribute, TestCase, TestCaseAttribute ──

    [Fact]
    public void NUnit_Test_EmitsEdges_WithNUnitFramework()
    {
        var testSource = MakeTestSource("NUnit.Framework", "TestAttribute", "NUnit.Framework.Test");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("NUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void NUnit_TestAttribute_EmitsEdges_WithNUnitFramework()
    {
        var testSource = MakeTestSource("NUnit.Framework", "TestAttribute", "NUnit.Framework.TestAttribute");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("NUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void NUnit_TestCase_EmitsEdges_WithNUnitFramework()
    {
        var testSource = MakeTestSource("NUnit.Framework", "TestCaseAttribute", "NUnit.Framework.TestCase");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("NUnit", e.Metadata["testFramework"]));
    }

    [Fact]
    public void NUnit_TestCaseAttribute_EmitsEdges_WithNUnitFramework()
    {
        var testSource = MakeTestSource("NUnit.Framework", "TestCaseAttribute", "NUnit.Framework.TestCaseAttribute");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("NUnit", e.Metadata["testFramework"]));
    }

    // ── MSTest: TestMethod and TestMethodAttribute ──

    [Fact]
    public void MSTest_TestMethod_EmitsEdges_WithMSTestFramework()
    {
        var testSource = MakeTestSource("Microsoft.VisualStudio.TestTools.UnitTesting",
            "TestMethodAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("MSTest", e.Metadata["testFramework"]));
    }

    [Fact]
    public void MSTest_TestMethodAttribute_EmitsEdges_WithMSTestFramework()
    {
        var testSource = MakeTestSource("Microsoft.VisualStudio.TestTools.UnitTesting",
            "TestMethodAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal("MSTest", e.Metadata["testFramework"]));
    }

    // ── Edge direction and type verification ──

    [Fact]
    public void CoversEdge_DirectionIsTestToTarget()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e =>
        {
            Assert.Contains("CalculatorTests", e.FromId);
            Assert.Contains("Calculator", e.ToId);
        });
    }

    [Fact]
    public void CoveredByEdge_DirectionIsTargetToTest()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var coveredBy = edges.Where(e => e.Type == EdgeType.CoveredBy).ToList();
        Assert.NotEmpty(coveredBy);
        Assert.All(coveredBy, e =>
        {
            Assert.Contains("Calculator", e.FromId);
            Assert.Contains("CalculatorTests", e.ToId);
        });
    }

    [Fact]
    public void Bidirectional_EachCoversHasMatchingCoveredBy()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        var coveredBy = edges.Where(e => e.Type == EdgeType.CoveredBy).ToList();

        Assert.NotEmpty(covers);
        Assert.Equal(covers.Count, coveredBy.Count);
        foreach (var c in covers)
        {
            Assert.Contains(coveredBy, cb => cb.FromId == c.ToId && cb.ToId == c.FromId);
        }
    }

    // ── IsExternal flag ──

    [Fact]
    public void AllEdges_HaveIsExternalTrue()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        Assert.NotEmpty(edges);
        Assert.All(edges, e => Assert.True(e.IsExternal));
    }

    // ── testFramework metadata key ──

    [Fact]
    public void TestFramework_MetadataKeyIsExact()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        Assert.NotEmpty(edges);
        Assert.All(edges, e =>
        {
            Assert.True(e.Metadata.ContainsKey("testFramework"), "Missing 'testFramework' metadata key");
            Assert.NotEmpty(e.Metadata["testFramework"]);
        });
    }

    // ── External nodes with assembly metadata ──

    [Fact]
    public void ExternalNodes_HaveAssemblyMetadata()
    {
        var testSource = MakeTestSource("Xunit", "FactAttribute", "Xunit.Fact");
        var compilation = CreateTwoAssemblyScenario(ProductionSource, testSource);
        var pass = new TestCoveragePass();
        // Pass empty knownIds so target methods become external nodes
        var (_, externalNodes) = pass.Execute(compilation, "", new HashSet<string>());

        Assert.NotEmpty(externalNodes);
        Assert.All(externalNodes, n =>
        {
            Assert.True(n.Metadata.ContainsKey("assembly"), "Missing 'assembly' metadata key");
            Assert.NotEmpty(n.Metadata["assembly"]);
        });
    }

    // ── Non-test methods produce no edges ──

    [Fact]
    public void NoTestAttributes_ReturnsEmpty()
    {
        var source = @"
namespace MyApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }

    public class NotATest
    {
        public void DoSomething()
        {
            var c = new Calculator();
            c.Add(1, 2);
        }
    }
}";

        var compilation = CreateCompilation(source);
        var syntaxPass = new SyntaxPass();
        var (nodes, _) = syntaxPass.Execute(compilation, "");
        var knownIds = new HashSet<string>(nodes.Select(n => n.Id));

        var pass = new TestCoveragePass();
        var (edges, externalNodes) = pass.Execute(compilation, "", knownIds);

        Assert.Empty(edges);
        Assert.Empty(externalNodes);
    }

    // ── Edge deduplication ──

    [Fact]
    public void DuplicateCallInTestMethod_ProducesOneCoversAndOneCoveredBy()
    {
        var testSource = @"
namespace Xunit { public class FactAttribute : System.Attribute { } }
namespace MyApp.Tests
{
    public class CalculatorTests
    {
        [Xunit.Fact]
        public void Add_CalledTwice()
        {
            var c = new MyApp.Calculator();
            c.Add(1, 2);
            c.Add(3, 4);
        }
    }
}";
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var addCovers = edges.Where(e => e.Type == EdgeType.Covers && e.ToId.Contains("Add")).ToList();
        var addCoveredBy = edges.Where(e => e.Type == EdgeType.CoveredBy && e.FromId.Contains("Add")).ToList();

        Assert.Single(addCovers);
        Assert.Single(addCoveredBy);
    }

    // ── Framework-specific discrimination (ensures each attr set maps to exactly the right framework) ──

    [Theory]
    [InlineData("Xunit", "FactAttribute", "Xunit.Fact", "xUnit")]
    [InlineData("Xunit", "TheoryAttribute", "Xunit.Theory", "xUnit")]
    [InlineData("NUnit.Framework", "TestAttribute", "NUnit.Framework.Test", "NUnit")]
    [InlineData("NUnit.Framework", "TestCaseAttribute", "NUnit.Framework.TestCase", "NUnit")]
    [InlineData("Microsoft.VisualStudio.TestTools.UnitTesting", "TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod", "MSTest")]
    public void Framework_Detection_ProducesCorrectFrameworkValue(
        string attrNamespace, string attrName, string fullyQualifiedAttr, string expectedFramework)
    {
        var testSource = MakeTestSource(attrNamespace, attrName, fullyQualifiedAttr);
        var (edges, _) = RunTestCoveragePass(ProductionSource, testSource);

        var covers = edges.Where(e => e.Type == EdgeType.Covers).ToList();
        Assert.NotEmpty(covers);
        Assert.All(covers, e => Assert.Equal(expectedFramework, e.Metadata["testFramework"]));
    }
}
