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
        solutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var solutionDir = Path.GetDirectoryName(solutionPath)!;

        // 1. Restore phase
        if (!skipRestore)
        {
            await RestoreSolutionAsync(solutionPath, cancellationToken);
        }

        // 2. Discovery phase
        var projectEntries = SolutionParser.Parse(solutionPath);
        var projectInfos = new List<ProjectInfo>();

        for (int i = 0; i < projectEntries.Count; i++)
        {
            var entry = projectEntries[i];
            var csprojPath = Path.GetFullPath(Path.Combine(solutionDir, entry.RelativePath));
            if (!File.Exists(csprojPath))
            {
                Console.Error.WriteLine($"Warning: Project file not found, skipping: {csprojPath}");
                continue;
            }

            Console.Error.WriteLine($"  Parsing project {i + 1}/{projectEntries.Count}: {entry.Name}");
            var info = ProjectParser.Parse(csprojPath);
            projectInfos.Add(info);
        }
        Console.Error.WriteLine();

        // 3. Resolution + 4. Compilation phase (parallelized)
        var referenceCache = new MetadataReferenceCache();
        var compiledCount = 0;
        var results = new ConcurrentBag<(int Index, ProjectCompilation Compilation)>();

        await Parallel.ForEachAsync(
            projectInfos.Select((info, index) => (info, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (item, ct) =>
            {
                var (info, index) = item;
                var count = Interlocked.Increment(ref compiledCount);
                Console.Error.WriteLine($"  Compiling {count}/{projectInfos.Count}: {Path.GetFileNameWithoutExtension(info.ProjectPath)} ({info.SourceFiles.Count} files)");

                // Resolve package references
                var packages = AssetsFileResolver.Resolve(info.ProjectDirectory, info.TargetFramework);
                var packageDlls = packages.Select(p => p.DllPath).ToList();

                // Resolve framework references
                var frameworkDlls = FrameworkRefResolver.Resolve(info.TargetFramework);

                // Combine all reference DLLs
                var allRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dll in frameworkDlls) allRefs.Add(dll);
                foreach (var dll in packageDlls) allRefs.Add(dll);

                // Build preprocessor symbols
                var symbols = preprocessorSymbols?.ToList() ?? new List<string>();
                if (configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase))
                {
                    if (!symbols.Contains("DEBUG")) symbols.Add("DEBUG");
                }
                if (!symbols.Contains("TRACE")) symbols.Add("TRACE");

                // Create compilation with shared reference cache
                var compilation = CompilationFactory.Create(
                    info.AssemblyName,
                    info.SourceFiles,
                    allRefs,
                    info.LangVersion,
                    info.NullableEnabled,
                    symbols.ToArray(),
                    referenceCache);

                results.Add((index, new ProjectCompilation(
                    Path.GetFileNameWithoutExtension(info.ProjectPath),
                    info.ProjectPath,
                    info.AssemblyName,
                    info.TargetFramework,
                    compilation)));

                await Task.CompletedTask;
            });

        Console.Error.WriteLine();
        return results.OrderBy(r => r.Index).Select(r => r.Compilation).ToList();
    }

    private static async Task RestoreSolutionAsync(
        string solutionPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{solutionPath}\" -v quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet restore process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            Console.Error.WriteLine(
                $"Warning: dotnet restore failed with exit code {process.ExitCode}. Continuing with best-effort indexing.\n{output}");
        }
    }
}
