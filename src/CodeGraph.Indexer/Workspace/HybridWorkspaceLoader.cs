using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGraph.Indexer.Workspace;

public record ProjectCompilation(
    string ProjectName,
    string ProjectPath,
    string AssemblyName,
    string TargetFramework,
    CSharpCompilation Compilation);

public class HybridWorkspaceLoader
{
    public async Task<IReadOnlyList<ProjectCompilation>> LoadAsync(
        string solutionPath,
        bool skipRestore = false,
        string configuration = "Debug",
        string[]? preprocessorSymbols = null,
        CancellationToken cancellationToken = default)
    {
        var fullSolutionPath = GetExistingSolutionPath(solutionPath);

        if (!skipRestore)
            await RestoreSolutionAsync(fullSolutionPath, cancellationToken).ConfigureAwait(false);

        var projectInfos = DiscoverProjects(fullSolutionPath);
        Console.Error.WriteLine();

        var compilations = await CompileProjectsAsync(
            projectInfos,
            configuration,
            preprocessorSymbols,
            cancellationToken).ConfigureAwait(false);

        Console.Error.WriteLine();
        return compilations;
    }

    private static string GetExistingSolutionPath(string solutionPath)
    {
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullSolutionPath))
            throw new FileNotFoundException($"Solution file not found: {fullSolutionPath}");

        return fullSolutionPath;
    }

    private static List<ProjectInfo> DiscoverProjects(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var projectEntries = SolutionParser.Parse(solutionPath);
        var projectInfos = new List<ProjectInfo>(projectEntries.Count);

        for (var index = 0; index < projectEntries.Count; index++)
        {
            var entry = projectEntries[index];
            var projectPath = Path.GetFullPath(Path.Combine(solutionDir, entry.RelativePath));
            if (!File.Exists(projectPath))
            {
                Console.Error.WriteLine($"Warning: Project file not found, skipping: {projectPath}");
                continue;
            }

            Console.Error.WriteLine($"  Parsing project {index + 1}/{projectEntries.Count}: {entry.Name}");
            projectInfos.Add(ProjectParser.Parse(projectPath));
        }

        return projectInfos;
    }

    private static async Task<IReadOnlyList<ProjectCompilation>> CompileProjectsAsync(
        List<ProjectInfo> projectInfos,
        string configuration,
        string[]? preprocessorSymbols,
        CancellationToken cancellationToken)
    {
        var referenceCache = new MetadataReferenceCache();
        var compiledCount = 0;
        var results = new ConcurrentBag<(int Index, ProjectCompilation Compilation)>();
        var compileItems = projectInfos.Select((info, index) => (Info: info, Index: index)).ToList();

        await Parallel.ForEachAsync(
            compileItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            (item, _) =>
            {
                var info = item.Info;
                var projectName = Path.GetFileNameWithoutExtension(info.ProjectPath);
                var count = Interlocked.Increment(ref compiledCount);

                Console.Error.WriteLine(
                    $"  Compiling {count}/{projectInfos.Count}: {projectName} ({info.SourceFiles.Count} files)");

                var compilation = CreateCompilation(
                    info,
                    configuration,
                    preprocessorSymbols,
                    referenceCache);

                results.Add((
                    item.Index,
                    new ProjectCompilation(
                        projectName,
                        info.ProjectPath,
                        info.AssemblyName,
                        info.TargetFramework,
                        compilation)));

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return results
            .OrderBy(result => result.Index)
            .Select(result => result.Compilation)
            .ToList();
    }

    private static CSharpCompilation CreateCompilation(
        ProjectInfo info,
        string configuration,
        string[]? preprocessorSymbols,
        MetadataReferenceCache referenceCache)
    {
        var referenceDlls = BuildReferenceDllPaths(info);
        var symbols = BuildPreprocessorSymbols(configuration, preprocessorSymbols);

        return CompilationFactory.Create(
            info.AssemblyName,
            info.SourceFiles,
            referenceDlls,
            info.LangVersion,
            info.NullableEnabled,
            symbols,
            referenceCache);
    }

    private static IReadOnlyCollection<string> BuildReferenceDllPaths(ProjectInfo info)
    {
        var referenceDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in FrameworkRefResolver.Resolve(info.TargetFramework))
            referenceDlls.Add(dll);

        foreach (var dll in AssetsFileResolver.Resolve(info.ProjectDirectory, info.TargetFramework).Select(package => package.DllPath))
            referenceDlls.Add(dll);

        return referenceDlls;
    }

    private static string[] BuildPreprocessorSymbols(string configuration, string[]? preprocessorSymbols)
    {
        var symbols = preprocessorSymbols?.ToList() ?? new List<string>();

        if (configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            AddSymbol(symbols, "DEBUG");

        AddSymbol(symbols, "TRACE");
        return symbols.ToArray();
    }

    private static void AddSymbol(List<string> symbols, string symbol)
    {
        if (!symbols.Contains(symbol))
            symbols.Add(symbol);
    }

    private static async Task RestoreSolutionAsync(string solutionPath, CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateRestoreProcessInfo(solutionPath))
            ?? throw new InvalidOperationException("Failed to start dotnet restore process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            Console.Error.WriteLine(
                $"Warning: dotnet restore failed with exit code {process.ExitCode}. Continuing with best-effort indexing.\n{output}");
        }
    }

    private static ProcessStartInfo CreateRestoreProcessInfo(string solutionPath)
    {
        return new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{solutionPath}\" -v quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
