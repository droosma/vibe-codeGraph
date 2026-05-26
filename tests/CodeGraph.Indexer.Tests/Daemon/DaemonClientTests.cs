using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using CodeGraph.Indexer.Daemon;

namespace CodeGraph.Indexer.Tests.Daemon;

public sealed class DaemonClientTests : IDisposable
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
        PidFile.Write(_graphDir, 99999999);

        var result = DaemonClient.TryConnect(_graphDir, out var client);

        Assert.False(result);
        Assert.Null(client);

        PidFile.Delete(_graphDir);
    }

    [Fact]
    public async Task QueryAsync_ReturnsServerResult_AndSendsExpectedPayload()
    {
        using var transport = new ScriptedDuplexStream("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"pong\"}\n");
        var client = CreateClient(transport);

        var result = await client.QueryAsync("ping", new[] { "one", "two" });

        Assert.Equal("pong", result);

        var request = JsonNode.Parse(transport.GetWrittenText())!;
        Assert.Equal("2.0", request["jsonrpc"]?.GetValue<string>());
        Assert.Equal(1, request["id"]?.GetValue<int>());
        Assert.Equal("ping", request["command"]?.GetValue<string>());
        Assert.Equal("one", request["args"]?[0]?.GetValue<string>());
        Assert.Equal("two", request["args"]?[1]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryAsync_ErrorResponse_ThrowsInvalidOperationException()
    {
        using var transport = new ScriptedDuplexStream("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603,\"message\":\"boom\"}}\n");
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryAsync("ping", Array.Empty<string>()));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_ClosedConnection_ThrowsIOException()
    {
        using var transport = new ScriptedDuplexStream(string.Empty);
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<IOException>(() => client.QueryAsync("ping", Array.Empty<string>()));

        Assert.Equal("Daemon closed the connection.", exception.Message);
    }

    private static DaemonClient CreateClient(Stream transport)
    {
        var client = (DaemonClient)RuntimeHelpers.GetUninitializedObject(typeof(DaemonClient));
        SetField(client, "_pipe", null);
        SetField(client, "_reader", new StreamReader(transport, Encoding.UTF8, leaveOpen: true));
        SetField(client, "_writer", new StreamWriter(transport, Encoding.UTF8, leaveOpen: true) { AutoFlush = true });
        SetField(client, "_nextId", 0);
        return client;
    }

    private static void SetField(object instance, string name, object? value)
    {
        typeof(DaemonClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    }

    private sealed class ScriptedDuplexStream(string readableText) : Stream
    {
        private readonly byte[] _readBuffer = Encoding.UTF8.GetBytes(readableText);
        private readonly MemoryStream _written = new();
        private int _readPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string GetWrittenText()
        {
            var text = Encoding.UTF8.GetString(_written.ToArray());
            return text.TrimStart('\uFEFF').TrimEnd('\r', '\n');
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _readBuffer.Length - _readPosition;
            if (remaining <= 0)
                return 0;

            var bytesToCopy = Math.Min(count, remaining);
            Array.Copy(_readBuffer, _readPosition, buffer, offset, bytesToCopy);
            _readPosition += bytesToCopy;
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var temp = new byte[buffer.Length];
            var read = Read(temp, 0, temp.Length);
            temp.AsMemory(0, read).CopyTo(buffer);
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _written.WriteAsync(buffer, cancellationToken);
        }
    }
}
