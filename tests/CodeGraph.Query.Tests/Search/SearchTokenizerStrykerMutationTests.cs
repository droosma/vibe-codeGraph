using CodeGraph.Core.Models;
using CodeGraph.Query.Search;

namespace CodeGraph.Query.Tests.Search;

public class SearchTokenizerStrykerMutationTests
{
    [Fact]
    public void TokenizeSymbol_MultiDotFileName_IncludesFullStemToken()
    {
        var node = new GraphNode
        {
            Id = string.Empty,
            Name = string.Empty,
            Kind = NodeKind.Type,
            FilePath = @"src\Generated\Order.Service.cs",
            AssemblyName = "TestAssembly"
        };

        var tokens = SearchTokenizer.TokenizeSymbol(node);

        Assert.Contains("order.service", tokens);
    }
}
