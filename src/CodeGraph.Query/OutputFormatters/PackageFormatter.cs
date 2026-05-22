using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGraph.Query.OutputFormatters;

public static class PackageFormatter
{
    public static string FormatUsage(IReadOnlyList<PackageUsage> usages)
    {
        var sb = new StringBuilder();

        if (usages.Count == 0)
        {
            sb.AppendLine("No package usage found.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"# Package usage ({usages.Count} entries)");
        sb.AppendLine();

        var byProject = usages.GroupBy(u => u.ProjectName).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var projectGroup in byProject)
        {
            sb.AppendLine($"## {projectGroup.Key}");
            foreach (var usage in projectGroup.OrderBy(u => u.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                var version = usage.Version != null ? $" v{usage.Version}" : string.Empty;
                sb.AppendLine($"- {usage.PackageId}{version}");
                sb.AppendLine($"  Types: {usage.ExternalTypeCount}, Usages: {usage.InternalUsageCount}");

                if (usage.ExampleExternalSymbols.Count > 0)
                    sb.AppendLine($"  External symbols: {string.Join(", ", usage.ExampleExternalSymbols)}");

                if (usage.ExampleInternalUsers.Count > 0)
                    sb.AppendLine($"  Internal users: {string.Join(", ", usage.ExampleInternalUsers)}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatDependents(IReadOnlyList<PackageDependent> dependents)
    {
        var sb = new StringBuilder();

        if (dependents.Count == 0)
        {
            sb.AppendLine("No package dependents found.");
            return sb.ToString().TrimEnd();
        }

        var packageId = dependents[0].PackageId;
        sb.AppendLine($"# Package dependents for {packageId} ({dependents.Count} entries)");
        sb.AppendLine();

        foreach (var projectGroup in dependents.GroupBy(d => d.ProjectName).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {projectGroup.Key}");
            foreach (var dependent in projectGroup.OrderBy(d => d.ConsumerKind).ThenBy(d => d.ConsumerId, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {dependent.ConsumerKind}: {dependent.ConsumerId}");
                if (dependent.ExternalSymbols.Count > 0)
                    sb.AppendLine($"  External symbols: {string.Join(", ", dependent.ExternalSymbols)}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatConflicts(IReadOnlyList<PackageConflict> conflicts)
    {
        var sb = new StringBuilder();

        if (conflicts.Count == 0)
        {
            sb.AppendLine("No version conflicts detected.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"# Version conflicts ({conflicts.Count} packages)");
        sb.AppendLine();

        foreach (var conflict in conflicts.OrderBy(c => c.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {conflict.PackageId}");
            foreach (var (project, version) in conflict.VersionsByProject.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {project}: {version ?? "(unknown)"}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatUsageAsJson(IReadOnlyList<PackageUsage> usages) => JsonSerializer.Serialize(usages, JsonOptions);

    public static string FormatDependentsAsJson(IReadOnlyList<PackageDependent> dependents) => JsonSerializer.Serialize(dependents, JsonOptions);

    public static string FormatConflictsAsJson(IReadOnlyList<PackageConflict> conflicts) => JsonSerializer.Serialize(conflicts, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
