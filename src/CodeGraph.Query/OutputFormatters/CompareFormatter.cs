using System.Text;
using CodeGraph.Core.Models;
using CodeGraph.Query;

namespace CodeGraph.Query.OutputFormatters;

public static class CompareFormatter
{
    public static string Format(CompareResult result)
    {
        var sb = new StringBuilder();

        var nameA = result.NodeA?.Id ?? "(not found)";
        var nameB = result.NodeB?.Id ?? "(not found)";

        sb.AppendLine($"# Compare: {nameA} vs {nameB}");
        sb.AppendLine();

        if (result.NodeA is null || result.NodeB is null)
        {
            if (result.NodeA is null)
                sb.AppendLine($"⚠ Symbol A not found");
            if (result.NodeB is null)
                sb.AppendLine($"⚠ Symbol B not found");
            return sb.ToString().TrimEnd();
        }

        // Shared interfaces
        if (result.SharedInterfaces.Count > 0)
        {
            sb.AppendLine("## Shared Interfaces");
            foreach (var iface in result.SharedInterfaces)
                sb.AppendLine($"  - {iface}");
            sb.AppendLine();
        }

        // Shared bases
        if (result.SharedBases.Count > 0)
        {
            sb.AppendLine("## Shared Base Types");
            foreach (var baseType in result.SharedBases)
                sb.AppendLine($"  - {baseType}");
            sb.AppendLine();
        }

        // Shared dependencies
        if (result.SharedEdges.Count > 0)
        {
            sb.AppendLine("## Shared Dependencies");
            foreach (var edge in result.SharedEdges)
                sb.AppendLine($"  - {FormatEdge(edge)}");
            sb.AppendLine();
        }

        // Unique to A
        if (result.UniqueToA.Count > 0)
        {
            sb.AppendLine($"## Unique to {nameA}");
            foreach (var edge in result.UniqueToA)
                sb.AppendLine($"  - {FormatEdge(edge)}");
            sb.AppendLine();
        }

        // Unique to B
        if (result.UniqueToB.Count > 0)
        {
            sb.AppendLine($"## Unique to {nameB}");
            foreach (var edge in result.UniqueToB)
                sb.AppendLine($"  - {FormatEdge(edge)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatEdge(GraphEdge edge)
    {
        var type = edge.Type switch
        {
            EdgeType.Calls => "calls",
            EdgeType.Inherits => "inherits",
            EdgeType.Implements => "implements",
            EdgeType.DependsOn => "depends-on",
            EdgeType.ResolvesTo => "resolves-to",
            EdgeType.Covers => "covers",
            EdgeType.References => "references",
            EdgeType.Contains => "contains",
            EdgeType.Overrides => "overrides",
            _ => edge.Type.ToString().ToLowerInvariant()
        };
        return $"{type} → {edge.ToId}";
    }
}