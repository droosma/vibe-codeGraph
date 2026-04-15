using System.Text.Json;
using CodeGraph.Core.IO;

namespace CodeGraph.Core.Configuration;

public static class ConfigLoader
{
    public const string DefaultFileName = "codegraph.json";

    /// <summary>
    /// Load config from specified path, or search working directory and parents.
    /// </summary>
    public static CodeGraphConfig Load(string? configPath = null)
    {
        if (configPath is not null)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"Configuration file not found: {configPath}", configPath);

            return DeserializeFile(configPath);
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, DefaultFileName);
            if (File.Exists(candidate))
                return DeserializeFile(candidate);

            directory = directory.Parent;
        }

        return new CodeGraphConfig();
    }

    /// <summary>
    /// Save config to file.
    /// </summary>
    public static async Task SaveAsync(CodeGraphConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, GraphSerializationOptions.Default);
        await File.WriteAllTextAsync(path, json);
    }

    private static CodeGraphConfig DeserializeFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CodeGraphConfig>(json, GraphSerializationOptions.Default)
                   ?? new CodeGraphConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse configuration file '{path}': {ex.Message}", ex);
        }
    }
}
