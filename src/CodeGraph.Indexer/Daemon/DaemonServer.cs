using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Query;
using CodeGraph.Query.OutputFormatters;

namespace CodeGraph.Indexer.Daemon;

/// <summary>
/// Named-pipe daemon that keeps a QueryEngine in memory, eliminating process-spawn overhead
/// for repeated queries. Multiple clients can connect concurrently.
/// </summary>
internal sealed class DaemonServer : IDisposable
{
    private const int PipeProbeAttempts = 5;
    private const int PipeProbeTimeoutMs = 500;

    private readonly string _graphDir;
    private readonly CancellationTokenSource _cts = new();
    private QueryEngine? _engine;
    private readonly SemaphoreSlim _engineLock = new(1, 1);

    internal DaemonServer(string graphDir)
    {
        _graphDir = Path.GetFullPath(graphDir);
    }

    /// <summary>
    /// Start listening on a named pipe. Blocks until cancelled.
    /// </summary>
    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var token = linked.Token;

        PidFile.Write(_graphDir, Environment.ProcessId);

        try
        {
            await GetOrLoadEngineAsync().ConfigureAwait(false);
            await AcceptClientsAsync(GetPipeName(_graphDir), token).ConfigureAwait(false);
        }
        finally
        {
            PidFile.Delete(_graphDir);
        }
    }

    /// <summary>
    /// Stop the daemon gracefully.
    /// </summary>
    internal void Stop()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
    }

    /// <summary>
    /// Deterministic pipe name derived from the absolute graph directory path.
    /// </summary>
    internal static string GetPipeName(string graphDir)
    {
        var absolutePath = Path.GetFullPath(graphDir);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(absolutePath));
        var hex = Convert.ToHexString(hash)[..16].ToLowerInvariant();
        return $"codegraph-{hex}";
    }

    /// <summary>
    /// Check whether a daemon is already running for the given graph directory.
    /// </summary>
    internal static bool IsRunning(string graphDir)
    {
        if (!PidFile.IsProcessRunning(graphDir))
            return false;

        return CanConnectToPipe(GetPipeName(graphDir));
    }

    private static bool CanConnectToPipe(string pipeName)
    {
        for (var attempt = 0; attempt < PipeProbeAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                client.Connect(timeout: PipeProbeTimeoutMs);
                return true;
            }
            catch
            {
                if (attempt < PipeProbeAttempts - 1)
                    Thread.Sleep(50);
            }
        }

        return false;
    }

    private async Task AcceptClientsAsync(string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var pipe = CreateServerPipe(pipeName);

            try
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            _ = HandleClientAsync(pipe, token);
        }
    }

    private static NamedPipeServerStream CreateServerPipe(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            using (pipe)
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (!TryParseRequest(line, out var request))
                    {
                        await writer.WriteLineAsync(
                            CreateError(null, -32700, "Parse error").ToJsonString(JsonOptions)).ConfigureAwait(false);
                        continue;
                    }

                    var response = await HandleRequestAsync(request!).ConfigureAwait(false);
                    await writer.WriteLineAsync(response.ToJsonString(JsonOptions)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static bool TryParseRequest(string line, out JsonNode? request)
    {
        try
        {
            request = JsonNode.Parse(line.Trim());
            return request is not null;
        }
        catch
        {
            request = null;
            return false;
        }
    }

    private async Task<JsonNode> HandleRequestAsync(JsonNode request)
    {
        var id = request["id"];
        var command = request["command"]?.GetValue<string>();

        if (string.IsNullOrEmpty(command))
            return CreateError(id, -32600, "Missing 'command' field");

        try
        {
            var engine = await GetOrLoadEngineAsync().ConfigureAwait(false);
            var args = GetArguments(request["args"]);
            var result = ExecuteCommand(engine, command, args);
            return CreateResponse(id, result);
        }
        catch (Exception ex)
        {
            return CreateError(id, -32603, ex.Message);
        }
    }

    private static string[] GetArguments(JsonNode? argsNode)
    {
        return argsNode is JsonArray arguments
            ? arguments.Select(argument => argument?.GetValue<string>() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }

    private static string ExecuteCommand(QueryEngine engine, string command, string[] args)
    {
        return command switch
        {
            "ping" => "pong",
            "query" => ExecuteQuery(engine, args),
            "search" => ExecuteSearch(engine, args),
            "list" => ExecuteList(engine, args),
            "stats" => ExecuteStats(engine),
            _ => throw new InvalidOperationException($"Unknown command: {command}")
        };
    }

    private static string ExecuteQuery(QueryEngine engine, string[] args)
    {
        if (args.Length == 0)
            return "Error: symbol pattern required";

        var options = new QueryOptions
        {
            Pattern = args[0],
            Depth = ReadIntegerOption(args, "--depth", 1),
            MaxNodes = ReadIntegerOption(args, "--max-nodes", 50),
            Format = OutputFormat.Compact,
            Rank = true,
            IgnoreCase = true
        };

        return CompactFormatter.Format(engine.Query(options));
    }

    private static int ReadIntegerOption(string[] args, string optionName, int defaultValue)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (args[index] == optionName && int.TryParse(args[index + 1], out var value))
                return value;
        }

        return defaultValue;
    }

    private static string ExecuteSearch(QueryEngine engine, string[] args)
    {
        var query = string.Join(" ", args);
        if (string.IsNullOrWhiteSpace(query))
            return "Error: search query required";

        var builder = new StringBuilder();
        foreach (var node in engine.Search(query, maxResults: 20))
            builder.AppendLine($"{node.Kind}: {node.Id}");

        return builder.Length > 0 ? builder.ToString().TrimEnd() : "No results found.";
    }

    private static string ExecuteList(QueryEngine engine, string[] args)
    {
        var scope = args.Length > 0 ? args[0] : "assemblies";
        if (!string.Equals(scope, "assemblies", StringComparison.Ordinal))
            return $"Unknown scope: {scope}. Supported: assemblies";

        var listEngine = new ListEngine(engine.Nodes, engine.Edges);
        var builder = new StringBuilder();
        builder.AppendLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
        builder.AppendLine(new string('-', 62));

        foreach (var assembly in listEngine.ListAssemblies())
            builder.AppendLine($"{assembly.Name,-40} {assembly.TypeCount,6} {assembly.MethodCount,8} {assembly.TotalNodeCount,6}");

        return builder.ToString().TrimEnd();
    }

    private static string ExecuteStats(QueryEngine engine)
    {
        return $"Nodes: {engine.Nodes.Count}{Environment.NewLine}Edges: {engine.Edges.Count}";
    }

    private async Task<QueryEngine> GetOrLoadEngineAsync()
    {
        if (_engine is not null)
            return _engine;

        await _engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _engine ??= await QueryEngine.LoadAsync(_graphDir).ConfigureAwait(false);
            return _engine;
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private static JsonNode CreateResponse(JsonNode? id, string result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };

        if (id is not null)
            response["id"] = id.DeepClone();

        return response;
    }

    private static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (id is not null)
            response["id"] = id.DeepClone();

        return response;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public void Dispose()
    {
        Stop();
        _engineLock.Dispose();
        _cts.Dispose();
    }
}
