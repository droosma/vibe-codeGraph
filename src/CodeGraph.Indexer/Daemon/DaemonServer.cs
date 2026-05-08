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
        var pipeName = GetPipeName(_graphDir);

        PidFile.Write(_graphDir, Environment.ProcessId);

        try
        {
            // Pre-load the engine so the first query is fast
            await GetOrLoadEngineAsync().ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    break;
                }

                // Handle client on a background task so we can accept new connections
                _ = HandleClientAsync(pipe, token);
            }
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
        var absolute = Path.GetFullPath(graphDir);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(absolute));
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

        // Verify the pipe is actually connectable
        var pipeName = GetPipeName(graphDir);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(timeout: 500);
            return true;
        }
        catch
        {
            return false;
        }
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

                    line = line.Trim();
                    if (line.Length == 0)
                        continue;

                    JsonNode? request;
                    try
                    {
                        request = JsonNode.Parse(line);
                    }
                    catch
                    {
                        var error = CreateError(null, -32700, "Parse error");
                        await writer.WriteLineAsync(error.ToJsonString(JsonOptions)).ConfigureAwait(false);
                        continue;
                    }

                    var response = await HandleRequestAsync(request!).ConfigureAwait(false);
                    await writer.WriteLineAsync(response.ToJsonString(JsonOptions)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (IOException)
        {
            // Client disconnected
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
            var argsNode = request["args"];
            var args = argsNode is JsonArray arr
                ? arr.Select(a => a?.GetValue<string>() ?? "").ToArray()
                : Array.Empty<string>();

            var result = command switch
            {
                "ping" => "pong",
                "query" => ExecuteQuery(engine, args),
                "search" => ExecuteSearch(engine, args),
                "list" => ExecuteList(engine, args),
                "stats" => ExecuteStats(engine),
                _ => throw new InvalidOperationException($"Unknown command: {command}")
            };

            return CreateResponse(id, result);
        }
        catch (Exception ex)
        {
            return CreateError(id, -32603, ex.Message);
        }
    }

    private static string ExecuteQuery(QueryEngine engine, string[] args)
    {
        if (args.Length == 0)
            return "Error: symbol pattern required";

        var pattern = args[0];
        var depth = 1;
        var maxNodes = 50;

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--depth" && int.TryParse(args[i + 1], out var d))
                depth = d;
            if (args[i] == "--max-nodes" && int.TryParse(args[i + 1], out var m))
                maxNodes = m;
        }

        var options = new QueryOptions
        {
            Pattern = pattern,
            Depth = depth,
            MaxNodes = maxNodes,
            Format = OutputFormat.Compact,
            Rank = true,
            IgnoreCase = true
        };

        var queryResult = engine.Query(options);
        return CompactFormatter.Format(queryResult);
    }

    private static string ExecuteSearch(QueryEngine engine, string[] args)
    {
        var query = string.Join(" ", args);
        if (string.IsNullOrWhiteSpace(query))
            return "Error: search query required";

        var results = engine.Search(query, maxResults: 20);
        var sb = new StringBuilder();
        foreach (var node in results)
            sb.AppendLine($"{node.Kind}: {node.Id}");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No results found.";
    }

    private static string ExecuteList(QueryEngine engine, string[] args)
    {
        var scope = args.Length > 0 ? args[0] : "assemblies";
        var list = new ListEngine(engine.Nodes, engine.Edges);
        var sb = new StringBuilder();

        if (scope == "assemblies")
        {
            var assemblies = list.ListAssemblies();
            sb.AppendLine($"{"Assembly",-40} {"Types",6} {"Methods",8} {"Total",6}");
            sb.AppendLine(new string('-', 62));
            foreach (var a in assemblies)
                sb.AppendLine($"{a.Name,-40} {a.TypeCount,6} {a.MethodCount,8} {a.TotalNodeCount,6}");
        }
        else
        {
            sb.AppendLine($"Unknown scope: {scope}. Supported: assemblies");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExecuteStats(QueryEngine engine)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Nodes: {engine.Nodes.Count}");
        sb.AppendLine($"Edges: {engine.Edges.Count}");
        return sb.ToString().TrimEnd();
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
