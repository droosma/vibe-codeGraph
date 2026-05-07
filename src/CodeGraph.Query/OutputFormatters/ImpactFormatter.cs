using System.Text;

namespace CodeGraph.Query.OutputFormatters;

public static class ImpactFormatter
{
    public static string Format(ImpactResult result)
    {
        var sb = new StringBuilder();

        if (result.Target is null)
        {
            sb.AppendLine($"No nodes found matching '{result.Pattern}'.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"# Impact analysis: {result.Target.Id}");
        sb.AppendLine($"Total affected: {result.TotalAffected} nodes across {result.Layers.Count} layer(s)");
        sb.AppendLine();

        foreach (var layer in result.Layers)
        {
            sb.AppendLine($"## Layer {layer.Depth} ({layer.Nodes.Count} nodes)");
            foreach (var node in layer.Nodes)
            {
                sb.Append($"- [{node.Kind}] {node.Id}");
                if (!string.IsNullOrEmpty(node.FilePath))
                    sb.Append($"  ({node.FilePath}:{node.StartLine})");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
