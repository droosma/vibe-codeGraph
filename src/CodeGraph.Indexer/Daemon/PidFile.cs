using System.Diagnostics;

namespace CodeGraph.Indexer.Daemon;

/// <summary>
/// Manages a PID file at &lt;graphDir&gt;/daemon.pid for daemon lifecycle tracking.
/// </summary>
internal static class PidFile
{
    private const string FileName = "daemon.pid";

    public static string GetPath(string graphDir) =>
        Path.Combine(graphDir, FileName);

    public static void Write(string graphDir, int pid)
    {
        Directory.CreateDirectory(graphDir);
        File.WriteAllText(GetPath(graphDir), pid.ToString());
    }

    public static int? Read(string graphDir)
    {
        var path = GetPath(graphDir);
        if (!File.Exists(path))
            return null;

        var text = File.ReadAllText(path).Trim();
        return int.TryParse(text, out var pid) ? pid : null;
    }

    public static void Delete(string graphDir)
    {
        var path = GetPath(graphDir);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static bool IsProcessRunning(string graphDir)
    {
        var pid = Read(graphDir);
        if (pid is null)
            return false;

        try
        {
            var process = Process.GetProcessById(pid.Value);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process does not exist
            return false;
        }
    }
}
