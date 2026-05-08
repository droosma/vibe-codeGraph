using CodeGraph.Indexer.Daemon;

namespace CodeGraph.Indexer.Tests.Daemon;

public class DaemonClientTests : IDisposable
{
    private readonly string _graphDir;

    public DaemonClientTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(DaemonClientTests).Assembly.Location)!,
            "DaemonClientTestData_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    [Fact]
    public void TryConnect_NoDaemon_ReturnsFalse()
    {
        var result = DaemonClient.TryConnect(_graphDir, out var client);

        Assert.False(result);
        Assert.Null(client);
    }

    [Fact]
    public void TryConnect_StalePidFile_ReturnsFalse()
    {
        // Write a PID that doesn't exist
        PidFile.Write(_graphDir, 99999999);

        var result = DaemonClient.TryConnect(_graphDir, out var client);

        Assert.False(result);
        Assert.Null(client);

        PidFile.Delete(_graphDir);
    }
}
