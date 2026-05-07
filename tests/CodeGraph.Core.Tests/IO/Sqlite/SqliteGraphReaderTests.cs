using CodeGraph.Core.IO.Sqlite;

namespace CodeGraph.Core.Tests.IO.Sqlite;

public class SqliteGraphReaderTests
{
    [Fact]
    public async Task ReadAsync_NonExistentDb_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => SqliteGraphReader.ReadAsync(fakePath));
    }
}
