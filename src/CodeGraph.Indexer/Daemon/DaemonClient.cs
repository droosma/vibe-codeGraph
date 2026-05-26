using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeGraph.Indexer.Daemon;

/// <summary>
/// Client that connects to a running DaemonServer over a named pipe.
/// </summary>
internal sealed class DaemonClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _nextId;

    private DaemonClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }

    /// <summary>
    /// Try to connect to a daemon for the given graph directory.
    /// Returns false if no daemon is running.
    /// </summary>
    public static bool TryConnect(string graphDir, out DaemonClient? client)
    {
        client = null;

        if (!PidFile.IsProcessRunning(graphDir))
            return false;

        var pipe = new NamedPipeClientStream(".", DaemonServer.GetPipeName(graphDir), PipeDirection.InOut);
        try
        {
            pipe.Connect(timeout: 2000);
            client = new DaemonClient(pipe);
            return true;
        }
        catch
        {
            pipe.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Send a command with arguments and return the result string.
    /// </summary>
    public async Task<string> QueryAsync(string command, string[] args)
    {
        var request = CreateRequest(command, args);
        await _writer.WriteLineAsync(request.ToJsonString(JsonOptions)).ConfigureAwait(false);

        var responseLine = await _reader.ReadLineAsync().ConfigureAwait(false);
        if (responseLine is null)
            throw new IOException("Daemon closed the connection.");

        var response = JsonNode.Parse(responseLine);
        if (response?["error"] is JsonNode error)
            throw new InvalidOperationException(error["message"]?.GetValue<string>() ?? "Daemon error");

        return response?["result"]?.GetValue<string>() ?? string.Empty;
    }

    private JsonObject CreateRequest(string command, string[] args)
    {
        var id = Interlocked.Increment(ref _nextId);
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["command"] = command,
            ["args"] = new JsonArray(args.Select(arg => (JsonNode)JsonValue.Create(arg)!).ToArray())
        };
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
    }
}
