using CodeGraph.Indexer.Daemon;

namespace CodeGraph.Indexer.Tests.Daemon;

public sealed class PidFileTests : IDisposable
{
    private readonly string _graphDir;

    public PidFileTests()
    {
        _graphDir = Path.Combine(Path.GetTempPath(), "pid-file-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    [Fact]
    public void GetPath_ReturnsDaemonPidPath()
    {
        Assert.Equal(Path.Combine(_graphDir, "daemon.pid"), PidFile.GetPath(_graphDir));
    }

    [Fact]
    public void Write_CreatesDirectory_AndPersistsPid()
    {
        PidFile.Write(_graphDir, 4321);

        Assert.True(Directory.Exists(_graphDir));
        Assert.Equal("4321", File.ReadAllText(PidFile.GetPath(_graphDir)));
    }

    [Fact]
    public void Read_InvalidContents_ReturnsNull()
    {
        Directory.CreateDirectory(_graphDir);
        File.WriteAllText(PidFile.GetPath(_graphDir), "not-a-pid");

        Assert.Null(PidFile.Read(_graphDir));
    }
}
