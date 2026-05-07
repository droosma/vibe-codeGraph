using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class PathFormatter
{
    public static string Format(PathResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Path: {result.FromId} → {result.ToId}");
        sb.AppendLine($"Steps: {result.Steps.Count}");
        sb.AppendLine();

        if (result.FromNode is not null)
        {
            sb.AppendLine($"  [{result.FromNode.Kind}] {result.FromId}");
            if (!string.IsNullOrEmpty(result.FromNode.FilePath))
                sb.AppendLine($"    File: {result.FromNode.FilePath}:{result.FromNode.StartLine}");
        }
        else
        {
            sb.AppendLine($"  {result.FromId}");
        }

        foreach (var step in result.Steps)
        {
            sb.AppendLine($"    --{step.Edge.Type}--> ");
            if (step.ToNode is not null)
            {
                sb.AppendLine($"  [{step.ToNode.Kind}] {step.ToId}");
                if (!string.IsNullOrEmpty(step.ToNode.FilePath))
                    sb.AppendLine($"    File: {step.ToNode.FilePath}:{step.ToNode.StartLine}");
            }
            else
            {
                sb.AppendLine($"  {step.ToId}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
