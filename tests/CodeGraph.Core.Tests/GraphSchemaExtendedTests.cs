namespace CodeGraph.Core.Tests;

public class GraphSchemaExtendedTests
{
    [Fact]
    public void CurrentVersion_IsOne()
    {
        Assert.Equal(1, GraphSchema.CurrentVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Validate_NonCurrentVersion_Throws(int version)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GraphSchema.Validate(version));
        Assert.Contains(version.ToString(), ex.Message);
        Assert.Contains(GraphSchema.CurrentVersion.ToString(), ex.Message);
    }

    [Fact]
    public void Validate_CurrentVersion_DoesNotThrow()
    {
        var exception = Record.Exception(() => GraphSchema.Validate(GraphSchema.CurrentVersion));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ErrorMessage_ContainsReindexHint()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GraphSchema.Validate(0));
        Assert.Contains("codegraph index", ex.Message);
    }
}
