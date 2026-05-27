using System.Diagnostics;
using System.Text;
using BindingFlags = System.Reflection.BindingFlags;
using MethodInfo = System.Reflection.MethodInfo;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeGraph.Core;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;
using CodeGraph.Indexer.Mcp;
using CodeGraph.Query;

namespace CodeGraph.Indexer.Tests.Mcp;

public sealed class McpServerAdditionalMutationTests : IDisposable
{
    private readonly string _graphDir;

    public McpServerAdditionalMutationTests()
    {
        _graphDir = Path.Combine(
            Path.GetDirectoryName(typeof(McpServerAdditionalMutationTests).Assembly.Location)!,
            "McpAddMut_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_graphDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_graphDir))
        {
            Directory.Delete(_graphDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_JsonLineProtocol_SkipsInvalidInputAndSuppressesNotificationResponses()
    {
        var output = await RunServerAsync(
            string.Join(
                "\n",
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}",
                string.Empty,
                "not json",
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}"));

        var responses = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!)
            .ToList();

        Assert.Equal(2, responses.Count);
        Assert.Equal(1, responses[0]!["id"]!.GetValue<int>());
        Assert.Equal("2024-11-05", responses[0]!["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal(2, responses[1]!["id"]!.GetValue<int>());
        Assert.NotNull(responses[1]!["result"]);
    }

    [Fact]
    public async Task RunAsync_ContentLengthProtocol_ReturnsFramedResponses()
    {
        var initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}";
        var toolsList = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}";
        var payload = BuildFrame(initialize) + BuildFrame(toolsList);

        var output = await RunServerAsync(payload);
        var responses = ParseFrames(output).ToList();

        Assert.Equal(2, responses.Count);
        Assert.Equal(1, responses[0]["id"]!.GetValue<int>());
        Assert.Equal(2, responses[1]["id"]!.GetValue<int>());
        Assert.Equal(13, responses[1]["result"]!["tools"]!.AsArray().Count);
        Assert.Equal("codegraph_summary", responses[1]["result"]!["tools"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void FormatTypes_WithPagination_ReturnsExactRangeAndHint()
    {
        var result = new ListTypesResult
        {
            Types =
            [
                new CodeGraph.Query.TypeInfo { Name = "Alpha.Service", InDegree = 3, OutDegree = 2, Assembly = "App" },
                new CodeGraph.Query.TypeInfo { Name = "Beta.Service", InDegree = 1, OutDegree = 1, Assembly = "App" }
            ],
            TotalCount = 4,
            Skip = 1,
            Top = 2
        };

        var output = (string)InvokePrivateStatic("FormatTypes", result, 2, 1)!;
        var normalized = NormalizeNewlines(output);

        Assert.Contains("Type", normalized);
        Assert.Contains("Alpha.Service", normalized);
        Assert.Contains("Beta.Service", normalized);
        Assert.Contains("Showing 2-3 of 4 types. Use --skip 3 for next page.", normalized);
    }

    [Fact]
    public void FormatAssemblies_ReturnsExactTable()
    {
        var assemblies = new[]
        {
            new AssemblyInfo { Name = "App", TypeCount = 3, MethodCount = 2, TotalNodeCount = 5 },
            new AssemblyInfo { Name = "Tests", TypeCount = 1, MethodCount = 4, TotalNodeCount = 5 }
        };

        var output = (string)InvokePrivateStatic("FormatAssemblies", (object)assemblies)!;
        var normalized = NormalizeNewlines(output);

        Assert.Contains("Assembly", normalized);
        Assert.Contains("App", normalized);
        Assert.Contains("Tests", normalized);
        Assert.Contains("5", normalized);
    }

    [Fact]
    public void FormatInterfaces_ReturnsExactTable()
    {
        var interfaces = new[]
        {
            new InterfaceInfo { Name = "IRepo", ImplementationCount = 2, Assembly = "App" },
            new InterfaceInfo { Name = "ICache", ImplementationCount = 0, Assembly = "Infra" }
        };

        var output = (string)InvokePrivateStatic("FormatInterfaces", (object)interfaces)!;
        var normalized = NormalizeNewlines(output);

        Assert.Contains("Interface", normalized);
        Assert.Contains("IRepo", normalized);
        Assert.Contains("ICache", normalized);
        Assert.Contains("Infra", normalized);
    }

    [Fact]
    public void FormatNamespaces_ReturnsExactTable()
    {
        var namespaces = new[]
        {
            new NamespaceInfo { Name = "App.Services", TypeCount = 2, MethodCount = 4 },
            new NamespaceInfo { Name = "App.Tests", TypeCount = 1, MethodCount = 2 }
        };

        var output = (string)InvokePrivateStatic("FormatNamespaces", (object)namespaces)!;
        var normalized = NormalizeNewlines(output);

        Assert.Contains("Namespace", normalized);
        Assert.Contains("App.Services", normalized);
        Assert.Contains("App.Tests", normalized);
        Assert.Contains("Methods", normalized);
    }

    [Fact]
    public async Task ToolsCall_Query_IncludeExternalFlag_TogglesExternalSymbols()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var withoutExternal = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(1),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["format"] = "context",
                        ["include_external"] = false
                    }
                }));
        var withExternal = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(2),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["format"] = "context",
                        ["include_external"] = true
                    }
                }));

        var withoutText = withoutExternal!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var withText = withExternal!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.DoesNotContain("Newtonsoft.Json.JsonConvert", withoutText);
        Assert.Contains("Newtonsoft.Json.JsonConvert", withText);
    }

    [Fact]
    public async Task ToolsCall_Query_IncludeSourceFlag_EmbedsSourceSnippetOnlyWhenRequested()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var withoutSource = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(3),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["format"] = "compact",
                        ["include_source"] = false
                    }
                }));
        var withSource = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(4),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Execute",
                        ["format"] = "compact",
                        ["include_source"] = true
                    }
                }));

        var withoutText = withoutSource!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var withText = withSource!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.DoesNotContain("```csharp", withoutText);
        Assert.DoesNotContain("System.Console.WriteLine(\"run\");", withoutText);
        Assert.Contains(graph.ServiceFile, withText);
        Assert.Contains("```csharp", withText);
        Assert.Contains("System.Console.WriteLine(\"run\");", withText);
    }

    [Fact]
    public async Task ToolsCall_Query_SolutionFilter_RestrictsFederatedResults()
    {
        var appDir = Path.Combine(_graphDir, "app");
        var otherDir = Path.Combine(_graphDir, "other");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(otherDir);

        var appGraph = await CreateSolutionGraphAsync(appDir, "app", "App.Service", "Service", "App");
        var otherGraph = await CreateSolutionGraphAsync(otherDir, "other", "Other.Service", "Service", "Other");
        await WriteGraphDataAsync(appDir, appGraph.Nodes, appGraph.Edges, appGraph.Metadata);
        await WriteGraphDataAsync(otherDir, otherGraph.Nodes, otherGraph.Edges, otherGraph.Metadata);

        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(5),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_query",
                    ["arguments"] = new JsonObject
                    {
                        ["symbol"] = "Service",
                        ["solution"] = "app",
                        ["format"] = "context"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.Contains("App.Service", text);
        Assert.DoesNotContain("Other.Service", text);
    }

    [Fact]
    public async Task ToolsCall_Path_MaxDepthBoundary_ControlsWhetherPathIsFound()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var tooShallow = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(6),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_path",
                    ["arguments"] = new JsonObject
                    {
                        ["from"] = "Handle",
                        ["to"] = "IRepo",
                        ["maxDepth"] = 1
                    }
                }));
        var deepEnough = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(7),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_path",
                    ["arguments"] = new JsonObject
                    {
                        ["from"] = "Handle",
                        ["to"] = "IRepo",
                        ["maxDepth"] = 2
                    }
                }));

        Assert.True(tooShallow!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("No path found from 'Handle' to 'IRepo'.", tooShallow["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.False(deepEnough!["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("App.Controller.Handle()", deepEnough["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Contains("App.IRepo", deepEnough["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Diff_HeadArgument_UsesSpecifiedHeadGraph()
    {
        var rootGraph = await CreateBehaviorGraphAsync(Path.Combine(_graphDir, "root-files"), "root-head");
        await WriteGraphDataAsync(_graphDir, rootGraph.Nodes, rootGraph.Edges, rootGraph.Metadata);

        var baseDir = Path.Combine(_graphDir, "base");
        Directory.CreateDirectory(baseDir);
        await WriteGraphDataAsync(
            baseDir,
            rootGraph.Nodes.Where(node => node.Id != "App.Controller" && node.Id != "App.Controller.Handle()").ToList(),
            rootGraph.Edges.Where(edge => edge.FromId != "App.Controller" && edge.ToId != "App.Controller.Handle()" && edge.FromId != "App.Controller.Handle()").ToList(),
            rootGraph.Metadata with { CommitHash = "base-commit" });

        var alternateHeadDir = Path.Combine(_graphDir, "alternate-head");
        Directory.CreateDirectory(alternateHeadDir);
        var alternateHeadNodes = rootGraph.Nodes.Where(node => node.Id != "App.Controller" && node.Id != "App.Controller.Handle()").ToList();
        alternateHeadNodes.Add(
            new GraphNode
            {
                Id = "Alt.Marker",
                Name = "Marker",
                Kind = NodeKind.Type,
                FilePath = Path.Combine(alternateHeadDir, "Marker.cs"),
                StartLine = 1,
                EndLine = 1,
                Signature = "Alt.Marker",
                Accessibility = Accessibility.Public,
                AssemblyName = "Alt",
                ContainingNamespaceId = "Alt",
                Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" }
            });
        await File.WriteAllTextAsync(Path.Combine(alternateHeadDir, "Marker.cs"), "namespace Alt; public class Marker { }");
        var alternateHeadEdges = rootGraph.Edges.Where(edge => edge.FromId != "App.Controller" && edge.ToId != "App.Controller.Handle()" && edge.FromId != "App.Controller.Handle()").ToList();
        alternateHeadEdges.Add(new GraphEdge { FromId = "Alt", ToId = "Alt.Marker", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified });
        alternateHeadNodes.Add(new GraphNode { Id = "Alt", Name = "Alt", Kind = NodeKind.Namespace, FilePath = string.Empty, Signature = "Alt", Accessibility = Accessibility.Public, AssemblyName = "Alt" });
        await WriteGraphDataAsync(
            alternateHeadDir,
            alternateHeadNodes,
            alternateHeadEdges,
            rootGraph.Metadata with { CommitHash = "alt-head" });

        var server = CreateServer();
        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(8),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_diff",
                    ["arguments"] = new JsonObject
                    {
                        ["base"] = baseDir,
                        ["head"] = alternateHeadDir,
                        ["format"] = "context"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.DoesNotContain("App.Controller", text);
        Assert.Contains("Alt.Marker", text);
        Assert.Contains("alt-hea", text);
    }

    [Fact]
    public async Task ToolsCall_Packages_ProjectFilter_RestrictsProjectSections()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(9),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_packages",
                    ["arguments"] = new JsonObject
                    {
                        ["project"] = "App"
                    }
                }));

        var text = response!["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.Contains("## App", text);
        Assert.DoesNotContain("## OtherApp", text);
    }

    [Fact]
    public async Task ToolsCall_File_KindAll_ReturnsExactOrderedSymbols()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(10),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_file",
                    ["arguments"] = new JsonObject
                    {
                        ["path"] = graph.ServiceFile,
                        ["kind"] = "all"
                    }
                }));

        var text = NormalizeNewlines(response!["result"]!["content"]![0]!["text"]!.GetValue<string>());

        Assert.Equal(
            $"Symbols in '{graph.ServiceFile}' (2 found):\n\n" +
            "  Type: App.Service\n" +
            "    sig: App.Service\n" +
            "    lines: 1-20\n" +
            "  Method: App.Service.Execute()\n" +
            "    sig: App.Service.Execute()\n" +
            "    lines: 5-12",
            text);
    }

    [Fact]
    public async Task ToolsCall_Search_NoMatches_ReturnsExactNonErrorMessage()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(11),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_search",
                    ["arguments"] = new JsonObject
                    {
                        ["query"] = "DoesNotExist"
                    }
                }));

        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("No results for 'DoesNotExist'.", response["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Compare_OneMissingSymbol_ReturnsComparisonInsteadOfNoNodesError()
    {
        var graph = await CreateBehaviorGraphAsync(_graphDir, "head");
        await WriteGraphDataAsync(_graphDir, graph.Nodes, graph.Edges, graph.Metadata);
        var server = CreateServer();

        var response = await server.HandleMessageAsync(
            MakeRequest(
                "tools/call",
                id: JsonValue.Create(12),
                @params: new JsonObject
                {
                    ["name"] = "codegraph_compare",
                    ["arguments"] = new JsonObject
                    {
                        ["symbolA"] = "Controller",
                        ["symbolB"] = "MissingType"
                    }
                }));

        Assert.False(response!["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("## Compare: Controller vs MissingType", response["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    private McpServer CreateServer() => new(_graphDir);

    private static JsonNode MakeRequest(string method, JsonNode? id = null, JsonNode? @params = null)
    {
        var message = new JsonObject { ["method"] = method };
        if (id is not null)
        {
            message["id"] = id.DeepClone();
        }

        if (@params is not null)
        {
            message["params"] = @params.DeepClone();
        }

        return message;
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(McpServer).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }

    private async Task<string> RunServerAsync(string input)
    {
        var assemblyPath = typeof(McpServer).Assembly.Location;
        var net10AssemblyPath = assemblyPath.Replace("net8.0", "net10.0", StringComparison.OrdinalIgnoreCase);
        if (File.Exists(net10AssemblyPath))
        {
            assemblyPath = net10AssemblyPath;
        }
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("mcp");
        startInfo.ArgumentList.Add("--graph-dir");
        startInfo.ArgumentList.Add(_graphDir);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        await process!.StandardInput.WriteAsync(input);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return output;
    }

    private static string BuildFrame(string body)
        => $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

    private static IEnumerable<JsonNode> ParseFrames(string output)
    {
        var index = 0;
        while (index < output.Length)
        {
            var headerEnd = output.IndexOf("\r\n\r\n", index, StringComparison.Ordinal);
            Assert.True(headerEnd >= 0, "Missing frame separator.");

            var header = output[index..headerEnd];
            Assert.StartsWith("Content-Length:", header, StringComparison.OrdinalIgnoreCase);
            var length = int.Parse(header["Content-Length:".Length..].Trim());
            var bodyStart = headerEnd + 4;
            var body = output.Substring(bodyStart, length);
            yield return JsonNode.Parse(body)!;
            index = bodyStart + length;
        }
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static async Task WriteGraphDataAsync(string graphDir, IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges, GraphMetadata metadata)
    {
        var writer = new GraphWriter();
        await writer.WriteAsync(graphDir, nodes, edges, metadata);
    }

    private static async Task<(List<GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Metadata, string ServiceFile, string ControllerFile)> CreateBehaviorGraphAsync(string rootDir, string commitHash)
    {
        var sourceDir = Path.Combine(rootDir, "sources");
        Directory.CreateDirectory(sourceDir);
        var serviceFile = Path.Combine(sourceDir, "Service.cs");
        var controllerFile = Path.Combine(sourceDir, "Controller.cs");
        var repoFile = Path.Combine(sourceDir, "IRepo.cs");
        var otherFile = Path.Combine(sourceDir, "OtherService.cs");

        await File.WriteAllTextAsync(serviceFile, string.Join(Environment.NewLine,
        [
            "namespace App;",
            "public class Service",
            "{",
            "    public void Execute()",
            "    {",
            "        System.Console.WriteLine(\"run\");",
            "    }",
            "}"
        ]));
        await File.WriteAllTextAsync(controllerFile, string.Join(Environment.NewLine,
        [
            "namespace App;",
            "public class Controller",
            "{",
            "    public void Handle()",
            "    {",
            "        new Service().Execute();",
            "    }",
            "}"
        ]));
        await File.WriteAllTextAsync(repoFile, "namespace App; public interface IRepo { }");
        await File.WriteAllTextAsync(otherFile, "namespace OtherApp; public class Service { }");

        var nodes = new List<GraphNode>
        {
            new() { Id = "App", Name = "App", Kind = NodeKind.Namespace, FilePath = string.Empty, Signature = "App", Accessibility = Accessibility.Public, AssemblyName = "App" },
            new() { Id = "OtherApp", Name = "OtherApp", Kind = NodeKind.Namespace, FilePath = string.Empty, Signature = "OtherApp", Accessibility = Accessibility.Public, AssemblyName = "OtherApp" },
            new() { Id = "App.Service", Name = "Service", Kind = NodeKind.Type, FilePath = serviceFile, StartLine = 1, EndLine = 20, Signature = "App.Service", Accessibility = Accessibility.Public, AssemblyName = "App", ContainingNamespaceId = "App", Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" } },
            new() { Id = "App.Service.Execute()", Name = "Execute", Kind = NodeKind.Method, FilePath = serviceFile, StartLine = 5, EndLine = 12, Signature = "App.Service.Execute()", Accessibility = Accessibility.Public, AssemblyName = "App", ContainingTypeId = "App.Service", ContainingNamespaceId = "App", Metadata = new Dictionary<string, string> { ["returnType"] = "void", ["parameterCount"] = "0" } },
            new() { Id = "App.Controller", Name = "Controller", Kind = NodeKind.Type, FilePath = controllerFile, StartLine = 1, EndLine = 20, Signature = "App.Controller", Accessibility = Accessibility.Public, AssemblyName = "App", ContainingNamespaceId = "App", Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" } },
            new() { Id = "App.Controller.Handle()", Name = "Handle", Kind = NodeKind.Method, FilePath = controllerFile, StartLine = 5, EndLine = 12, Signature = "App.Controller.Handle()", Accessibility = Accessibility.Public, AssemblyName = "App", ContainingTypeId = "App.Controller", ContainingNamespaceId = "App", Metadata = new Dictionary<string, string> { ["returnType"] = "void", ["parameterCount"] = "0" } },
            new() { Id = "App.IRepo", Name = "IRepo", Kind = NodeKind.Type, FilePath = repoFile, StartLine = 1, EndLine = 1, Signature = "App.IRepo", Accessibility = Accessibility.Public, AssemblyName = "App", ContainingNamespaceId = "App", Metadata = new Dictionary<string, string> { ["typeKind"] = "Interface" } },
            new() { Id = "OtherApp.Service", Name = "Service", Kind = NodeKind.Type, FilePath = otherFile, StartLine = 1, EndLine = 1, Signature = "OtherApp.Service", Accessibility = Accessibility.Public, AssemblyName = "OtherApp", ContainingNamespaceId = "OtherApp", Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" } }
        };

        var edges = new List<GraphEdge>
        {
            new() { FromId = "App", ToId = "App.Service", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.Controller", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App", ToId = "App.IRepo", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "OtherApp", ToId = "OtherApp.Service", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service", ToId = "App.Service.Execute()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Controller", ToId = "App.Controller.Handle()", Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Controller.Handle()", ToId = "App.Service.Execute()", Type = EdgeType.Calls, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service.Execute()", ToId = "App.IRepo", Type = EdgeType.DependsOn, Confidence = EdgeConfidence.Verified },
            new() { FromId = "App.Service.Execute()", ToId = "Newtonsoft.Json.JsonConvert", Type = EdgeType.Calls, Confidence = EdgeConfidence.Verified, IsExternal = true, PackageSource = "Newtonsoft.Json/13.0.1" },
            new() { FromId = "OtherApp.Service", ToId = "Newtonsoft.Json.Linq.JObject", Type = EdgeType.DependsOn, Confidence = EdgeConfidence.Verified, IsExternal = true, PackageSource = "Newtonsoft.Json/12.0.3" }
        };

        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = commitHash,
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "0.1.0",
            Solution = "app.sln",
            SolutionName = "app"
        };

        return (nodes, edges, metadata, serviceFile, controllerFile);
    }

    private static async Task<(List<GraphNode> Nodes, List<GraphEdge> Edges, GraphMetadata Metadata)> CreateSolutionGraphAsync(
        string graphDir,
        string solutionName,
        string symbolId,
        string symbolName,
        string assemblyName)
    {
        var filePath = Path.Combine(graphDir, solutionName + ".cs");
        await File.WriteAllTextAsync(filePath, $"namespace {assemblyName}; public class {symbolName} {{ }}");

        var namespaceId = assemblyName;
        var nodes = new List<GraphNode>
        {
            new() { Id = namespaceId, Name = assemblyName, Kind = NodeKind.Namespace, FilePath = string.Empty, Signature = assemblyName, Accessibility = Accessibility.Public, AssemblyName = assemblyName },
            new() { Id = symbolId, Name = symbolName, Kind = NodeKind.Type, FilePath = filePath, StartLine = 1, EndLine = 1, Signature = symbolId, Accessibility = Accessibility.Public, AssemblyName = assemblyName, ContainingNamespaceId = namespaceId, Metadata = new Dictionary<string, string> { ["typeKind"] = "Class" } }
        };
        var edges = new List<GraphEdge>
        {
            new() { FromId = namespaceId, ToId = symbolId, Type = EdgeType.Contains, Confidence = EdgeConfidence.Verified }
        };
        var metadata = new GraphMetadata
        {
            SchemaVersion = GraphSchema.CurrentVersion,
            CommitHash = solutionName + "-commit",
            Branch = "main",
            GeneratedAt = DateTimeOffset.UtcNow,
            IndexerVersion = "0.1.0",
            Solution = solutionName + ".sln",
            SolutionName = solutionName
        };

        return (nodes, edges, metadata);
    }
}
