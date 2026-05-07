using System.Text;
using CodeGraph.Core.Models;

namespace CodeGraph.Query.OutputFormatters;

public static class ExplainFormatter
{
    public static string Format(ExplainResult result)
    {
        var sb = new StringBuilder();
        var node = result.Node;

        sb.AppendLine($"# {node.Id}");
        sb.AppendLine($"Kind: {node.Kind}");
        if (!string.IsNullOrEmpty(node.FilePath))
            sb.AppendLine($"File: {node.FilePath}:{node.StartLine}-{node.EndLine}");
        if (!string.IsNullOrEmpty(node.Signature))
            sb.AppendLine($"Signature: {node.Signature}");
        if (!string.IsNullOrEmpty(node.DocComment))
            sb.AppendLine($"Doc: {node.DocComment}");
        sb.AppendLine();

        if (result.Members.Count > 0)
        {
            sb.AppendLine($"## Members ({result.Members.Count})");
            foreach (var m in result.Members)
                sb.AppendLine($"- [{m.Kind}] {m.Name}");
            sb.AppendLine();
        }

        // Group outgoing by type (exclude Contains, already shown as Members)
        var outByType = result.OutgoingEdges
            .Where(e => e.Type != EdgeType.Contains)
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in outByType)
        {
            sb.AppendLine($"## {group.Key} (outgoing, {group.Count()})");
            foreach (var edge in group)
                sb.AppendLine($"- → {edge.ToId}");
            sb.AppendLine();
        }

        // Group incoming by type
        var inByType = result.IncomingEdges
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key);

        foreach (var group in inByType)
        {
            sb.AppendLine($"## {group.Key} (incoming, {group.Count()})");
            foreach (var edge in group)
                sb.AppendLine($"- ← {edge.FromId}");
            sb.AppendLine();
        }

        if (result.Tests.Count > 0)
        {
            sb.AppendLine($"## Test coverage ({result.Tests.Count})");
            foreach (var t in result.Tests)
                sb.AppendLine($"- {t.Id}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
