using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CodeGraph.Indexer.Daemon;

namespace CodeGraph.Indexer.Tests.Daemon;

public class DaemonServerTests : IDisposable
{
    private readonly string _graphDir;

    public DaemonServerTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(DaemonServerTests).Assembly.Location)!,
            "DaemonTestData_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
            Directory.Delete(_graphDir, recursive: true);
    }

    [Fact]
    public void GetPipeName_IsDeterministic()
    {
        var name1 = DaemonServer.GetPipeName(_graphDir);
        var name2 = DaemonServer.GetPipeName(_graphDir);

        Assert.Equal(name1, name2);
    }

    [Fact]
    public void GetPipeName_DifferentDirs_ProduceDifferentNames()
    {
        var name1 = DaemonServer.GetPipeName(@"D:\some\path");
        var name2 = DaemonServer.GetPipeName(@"D:\other\path");

        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void GetPipeName_HasExpectedPrefix()
    {
        var name = DaemonServer.GetPipeName(_graphDir);

        Assert.StartsWith("codegraph-", name);
    }

    [Fact]
    public void GetPipeName_HasFixedLength()
    {
        // "codegraph-" (10) + 16 hex chars = 26
        var name = DaemonServer.GetPipeName(_graphDir);

        Assert.Equal(26, name.Length);
    }

    [Fact]
    public void GetPipeName_RelativePath_UsesAbsolutePath()
    {
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), _graphDir);

        Assert.Equal(DaemonServer.GetPipeName(_graphDir), DaemonServer.GetPipeName(relativePath));
    }

    [Fact]
    public void GetPipeName_UsesExpectedLowercaseHash()
    {
        var absolutePath = Path.GetFullPath(_graphDir);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(absolutePath));
        var expected = $"codegraph-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";

        Assert.Equal(expected, DaemonServer.GetPipeName(_graphDir));
        Assert.Matches("^codegraph-[0-9a-f]{16}$", DaemonServer.GetPipeName(_graphDir));
    }

    [Fact]
    public void IsRunning_NoDaemon_ReturnsFalse()
    {
        Assert.False(DaemonServer.IsRunning(_graphDir));
    }

    [Fact]
    public void IsRunning_PidFileForLiveProcessWithoutPipe_ReturnsFalse()
    {
        PidFile.Write(_graphDir, Environment.ProcessId);

        try
        {
            Assert.False(DaemonServer.IsRunning(_graphDir));
        }
        finally
        {
            PidFile.Delete(_graphDir);
        }
    }

    [Fact]
    public void PidFile_WriteReadDelete()
    {
        PidFile.Write(_graphDir, 12345);

        var pid = PidFile.Read(_graphDir);
        Assert.Equal(12345, pid);

        PidFile.Delete(_graphDir);
        Assert.Null(PidFile.Read(_graphDir));
    }

    [Fact]
    public void PidFile_ReadMissing_ReturnsNull()
    {
        Assert.Null(PidFile.Read(_graphDir));
    }

    [Fact]
    public void PidFile_IsProcessRunning_CurrentProcess_ReturnsTrue()
    {
        var currentPid = Environment.ProcessId;
        PidFile.Write(_graphDir, currentPid);

        Assert.True(PidFile.IsProcessRunning(_graphDir));

        PidFile.Delete(_graphDir);
    }

    [Fact]
    public void PidFile_IsProcessRunning_NoPidFile_ReturnsFalse()
    {
        Assert.False(PidFile.IsProcessRunning(_graphDir));
    }

    [Fact]
    public void PidFile_IsProcessRunning_DeadProcess_ReturnsFalse()
    {
        // Use a PID that almost certainly doesn't exist
        PidFile.Write(_graphDir, 99999999);

        Assert.False(PidFile.IsProcessRunning(_graphDir));

        PidFile.Delete(_graphDir);
    }

    [Fact]
    public void PidFile_Delete_WhenNoFile_DoesNotThrow()
    {
        PidFile.Delete(_graphDir);
    }

    [Fact]
    public void CreateResponse_WithObjectId_ClonesIdAndIncludesResult()
    {
        var id = new JsonObject { ["value"] = 1 };

        var response = InvokeStaticJsonNodeMethod("CreateResponse", id, "pong");
        id["value"] = 99;

        Assert.Equal("2.0", response["jsonrpc"]?.GetValue<string>());
        Assert.Equal("pong", response["result"]?.GetValue<string>());
        Assert.Equal(1, response["id"]?["value"]?.GetValue<int>());
    }

    [Fact]
    public void CreateError_WithoutId_OmitsIdAndIncludesErrorDetails()
    {
        var response = InvokeStaticJsonNodeMethod("CreateError", null, -32700, "Parse error");

        Assert.Equal("2.0", response["jsonrpc"]?.GetValue<string>());
        Assert.Null(response["id"]);
        Assert.Equal(-32700, response["error"]?["code"]?.GetValue<int>());
        Assert.Equal("Parse error", response["error"]?["message"]?.GetValue<string>());
    }

    private static JsonNode InvokeStaticJsonNodeMethod(string name, params object?[] args)
    {
        return (JsonNode)typeof(DaemonServer)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, args)!;
    }
}
