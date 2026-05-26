using System.Text.Json;
using CodeGraph.Core.IO;
using CodeGraph.Core.Models;

namespace CodeGraph.Indexer.View;

public class HtmlGraphGenerator
{
    private readonly string _graphDir;
    private readonly int _maxNodes;

    public HtmlGraphGenerator(string graphDir, int maxNodes = 5000)
    {
        _graphDir = graphDir;
        _maxNodes = maxNodes;
    }

    public async Task<string> GenerateAsync()
    {
        var (metadata, nodes, edges) = await GraphReader.ReadAsync(_graphDir).ConfigureAwait(false);
        var (sampledNodes, sampledEdges) = Sample(nodes, edges);
        return BuildHtml(metadata, sampledNodes, sampledEdges);
    }

    internal (Dictionary<string, GraphNode> Nodes, List<GraphEdge> Edges) Sample(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        if (nodes.Count <= _maxNodes)
            return (nodes, edges);

        var retainedNodes = GetRetainedNodes(nodes, edges);
        var retainedEdges = edges
            .Where(edge => retainedNodes.ContainsKey(edge.FromId) && retainedNodes.ContainsKey(edge.ToId))
            .ToList();

        return (retainedNodes, retainedEdges);
    }

    private Dictionary<string, GraphNode> GetRetainedNodes(
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        var retainedNodes = nodes
            .Where(static pair => pair.Value.Kind is NodeKind.Type or NodeKind.Namespace)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (retainedNodes.Count >= _maxNodes)
            return nodes.Take(_maxNodes).ToDictionary(pair => pair.Key, pair => pair.Value);

        var degrees = CalculateDegrees(edges, retainedNodes);
        var remainingNodes = nodes
            .Where(pair => !retainedNodes.ContainsKey(pair.Key))
            .OrderByDescending(pair => degrees.GetValueOrDefault(pair.Key))
            .Take(_maxNodes - retainedNodes.Count);

        foreach (var (id, node) in remainingNodes)
            retainedNodes[id] = node;

        return retainedNodes;
    }

    private static Dictionary<string, int> CalculateDegrees(
        IEnumerable<GraphEdge> edges,
        IReadOnlyDictionary<string, GraphNode> retainedNodes)
    {
        var degrees = new Dictionary<string, int>();

        foreach (var edge in edges)
        {
            if (!retainedNodes.ContainsKey(edge.FromId))
                degrees[edge.FromId] = degrees.GetValueOrDefault(edge.FromId) + 1;

            if (!retainedNodes.ContainsKey(edge.ToId))
                degrees[edge.ToId] = degrees.GetValueOrDefault(edge.ToId) + 1;
        }

        return degrees;
    }

    private static string BuildHtml(
        GraphMetadata metadata,
        Dictionary<string, GraphNode> nodes,
        List<GraphEdge> edges)
    {
        var solutionName = string.IsNullOrEmpty(metadata.SolutionName)
            ? metadata.Solution
            : metadata.SolutionName;

        return $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>CodeGraph — {EscapeHtml(solutionName)}</title>
<style>
{GetCss()}
</style>
</head>
<body>
{GetBodyHtml(solutionName, nodes.Count, edges.Count)}
<script src="https://unpkg.com/3d-force-graph@1"></script>
<script src="https://unpkg.com/three-spritetext@1"></script>
<script>
const graphData = {BuildGraphJson(nodes, edges)};
{GetJs()}
</script>
</body>
</html>
""";
    }

    private static string BuildGraphJson(Dictionary<string, GraphNode> nodes, List<GraphEdge> edges)
    {
        var graphData = new
        {
            nodes = nodes.Values.Select(CreateNodeData),
            links = edges.Select(CreateLinkData)
        };

        return JsonSerializer.Serialize(graphData, new JsonSerializerOptions { WriteIndented = false });
    }

    private static object CreateNodeData(GraphNode node)
    {
        return new
        {
            id = node.Id,
            name = node.Name,
            kind = node.Kind.ToString(),
            assembly = node.AssemblyName,
            accessibility = node.Accessibility.ToString(),
            signature = node.Signature,
            filePath = node.FilePath,
            startLine = node.StartLine,
            ns = node.ContainingNamespaceId ?? string.Empty
        };
    }

    private static object CreateLinkData(GraphEdge edge)
    {
        return new
        {
            source = edge.FromId,
            target = edge.ToId,
            type = edge.Type.ToString(),
            isExternal = edge.IsExternal
        };
    }

    private static string GetCss() => """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #1a1a2e; color: #eee; overflow: hidden; display: flex; flex-direction: column; height: 100vh; }
        #header { display: flex; align-items: center; padding: 8px 16px; background: #16213e; border-bottom: 1px solid #0f3460; gap: 16px; }
        #header h1 { font-size: 16px; font-weight: 600; color: #e94560; }
        #header .stats { font-size: 13px; color: #a0a0b0; margin-left: auto; }
        #main { display: flex; flex: 1; overflow: hidden; }
        #sidebar { width: 240px; background: #16213e; border-right: 1px solid #0f3460; padding: 12px; overflow-y: auto; flex-shrink: 0; }
        #sidebar h3 { font-size: 12px; text-transform: uppercase; color: #a0a0b0; margin: 12px 0 6px; letter-spacing: 0.5px; }
        #sidebar h3:first-child { margin-top: 0; }
        .filter-item { display: flex; align-items: center; gap: 8px; padding: 4px 8px; border-radius: 4px; cursor: pointer; font-size: 13px; user-select: none; }
        .filter-item:hover { background: #0f3460; }
        .filter-item.disabled { opacity: 0.4; }
        .filter-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
        .filter-line { width: 14px; height: 3px; border-radius: 2px; flex-shrink: 0; }
        .filter-count { margin-left: auto; font-size: 11px; color: #666; }
        #search { width: 100%; padding: 6px 10px; border-radius: 4px; border: 1px solid #0f3460; background: #1a1a2e; color: #eee; font-size: 13px; margin-top: 8px; }
        #search:focus { outline: none; border-color: #e94560; }
        .toggle-row { display: flex; align-items: center; gap: 8px; padding: 4px 8px; font-size: 13px; cursor: pointer; user-select: none; }
        .toggle-row input { accent-color: #e94560; }
        #graph-container { flex: 1; position: relative; }
        .assembly-item { padding: 4px 8px; font-size: 13px; display: flex; justify-content: space-between; border-radius: 4px; cursor: pointer; }
        .assembly-item:hover { background: #0f3460; }
        .assembly-count { color: #666; font-size: 11px; }
        """;

    private static string GetBodyHtml(string solutionName, int nodeCount, int edgeCount) => $"""
        <div id="header">
            <h1>CodeGraph</h1>
            <span class="stats">{nodeCount} nodes / {edgeCount} edges — {EscapeHtml(solutionName)}</span>
        </div>
        <div id="main">
            <div id="sidebar">
                <h3>Node Kinds</h3>
                <div id="node-filters"></div>
                <h3>Edge Types</h3>
                <div id="edge-filters"></div>
                <h3>Options</h3>
                <label class="toggle-row"><input type="checkbox" id="toggle-labels"> Show labels</label>
                <h3>Search</h3>
                <input type="text" id="search" placeholder="Filter nodes..." />
                <h3>Assemblies</h3>
                <div id="assembly-list"></div>
            </div>
            <div id="graph-container"></div>
        </div>
        """;

    private static string GetJs() => """
        const nodeKindColors = {
            Namespace: '#9b59b6', Type: '#3498db', Method: '#2ecc71',
            Property: '#f39c12', Field: '#e74c3c', Event: '#1abc9c', Constructor: '#e67e22'
        };
        const edgeTypeColors = {
            Contains: '#555', Calls: '#2ecc71', Inherits: '#3498db',
            Implements: '#9b59b6', DependsOn: '#e74c3c', ResolvesTo: '#f39c12',
            Covers: '#1abc9c', CoveredBy: '#16a085', References: '#95a5a6', Overrides: '#e67e22'
        };

        // State
        let activeNodeKinds = new Set(Object.keys(nodeKindColors));
        let activeEdgeTypes = new Set(Object.keys(edgeTypeColors));
        let searchPattern = '';
        let selectedAssembly = null;

        // Build filters
        const nodeKindCounts = {};
        graphData.nodes.forEach(n => { nodeKindCounts[n.kind] = (nodeKindCounts[n.kind] || 0) + 1; });
        const edgeTypeCounts = {};
        graphData.links.forEach(e => { edgeTypeCounts[e.type] = (edgeTypeCounts[e.type] || 0) + 1; });

        const nodeFiltersEl = document.getElementById('node-filters');
        Object.keys(nodeKindColors).forEach(kind => {
            if (!nodeKindCounts[kind]) return;
            const div = document.createElement('div');
            div.className = 'filter-item';
            div.innerHTML = `<span class="filter-dot" style="background:${nodeKindColors[kind]}"></span>${kind}<span class="filter-count">${nodeKindCounts[kind]}</span>`;
            div.onclick = () => {
                if (activeNodeKinds.has(kind)) activeNodeKinds.delete(kind);
                else activeNodeKinds.add(kind);
                div.classList.toggle('disabled');
                updateGraph();
            };
            nodeFiltersEl.appendChild(div);
        });

        const edgeFiltersEl = document.getElementById('edge-filters');
        Object.keys(edgeTypeColors).forEach(type => {
            if (!edgeTypeCounts[type]) return;
            const div = document.createElement('div');
            div.className = 'filter-item';
            div.innerHTML = `<span class="filter-line" style="background:${edgeTypeColors[type]}"></span>${type}<span class="filter-count">${edgeTypeCounts[type]}</span>`;
            div.onclick = () => {
                if (activeEdgeTypes.has(type)) activeEdgeTypes.delete(type);
                else activeEdgeTypes.add(type);
                div.classList.toggle('disabled');
                updateGraph();
            };
            edgeFiltersEl.appendChild(div);
        });

        // Assemblies
        const assemblyCounts = {};
        graphData.nodes.forEach(n => { if (n.assembly) assemblyCounts[n.assembly] = (assemblyCounts[n.assembly] || 0) + 1; });
        const assemblyListEl = document.getElementById('assembly-list');
        Object.entries(assemblyCounts).sort((a,b) => b[1]-a[1]).forEach(([asm, count]) => {
            const div = document.createElement('div');
            div.className = 'assembly-item';
            div.innerHTML = `<span>${asm}</span><span class="assembly-count">${count}</span>`;
            div.onclick = () => {
                selectedAssembly = selectedAssembly === asm ? null : asm;
                document.querySelectorAll('.assembly-item').forEach(el => el.style.background = '');
                if (selectedAssembly) div.style.background = '#0f3460';
                updateGraph();
            };
            assemblyListEl.appendChild(div);
        });

        // Search
        document.getElementById('search').addEventListener('input', e => {
            searchPattern = e.target.value.toLowerCase();
            updateGraph();
        });

        // Graph
        const container = document.getElementById('graph-container');
        const graph = ForceGraph3D()(container)
            .backgroundColor('#1a1a2e')
            .nodeLabel(n => `<b>${n.name}</b><br/>Kind: ${n.kind}<br/>Assembly: ${n.assembly}<br/>${n.signature || ''}<br/>${n.filePath ? n.filePath + ':' + n.startLine : ''}`)
            .nodeColor(n => nodeKindColors[n.kind] || '#999')
            .nodeVal(n => n.kind === 'Type' ? 4 : n.kind === 'Namespace' ? 6 : 2)
            .nodeOpacity(0.9)
            .linkColor(l => edgeTypeColors[l.type] || '#555')
            .linkOpacity(l => l.isExternal ? 0.2 : 0.6)
            .linkDirectionalArrowLength(3)
            .linkDirectionalArrowRelPos(1)
            .nodeVisibility(n => isNodeVisible(n))
            .linkVisibility(l => isLinkVisible(l));

        // Labels
        document.getElementById('toggle-labels').addEventListener('change', e => {
            if (e.target.checked) {
                graph.nodeThreeObject(n => {
                    const sprite = new SpriteText(n.name);
                    sprite.color = '#ccc';
                    sprite.textHeight = 2;
                    return sprite;
                }).nodeThreeObjectExtend(true);
            } else {
                graph.nodeThreeObject(null).nodeThreeObjectExtend(false);
            }
        });

        graph.graphData(graphData);

        function isNodeVisible(n) {
            if (!activeNodeKinds.has(n.kind)) return false;
            if (selectedAssembly && n.assembly !== selectedAssembly) return false;
            if (searchPattern && !n.name.toLowerCase().includes(searchPattern) && !n.id.toLowerCase().includes(searchPattern)) return false;
            return true;
        }

        function isLinkVisible(l) {
            if (!activeEdgeTypes.has(l.type)) return false;
            const src = typeof l.source === 'object' ? l.source : graphData.nodes.find(n => n.id === l.source);
            const tgt = typeof l.target === 'object' ? l.target : graphData.nodes.find(n => n.id === l.target);
            if (src && !isNodeVisible(src)) return false;
            if (tgt && !isNodeVisible(tgt)) return false;
            return true;
        }

        function updateGraph() {
            graph.nodeVisibility(n => isNodeVisible(n));
            graph.linkVisibility(l => isLinkVisible(l));
        }

        // Keyboard shortcuts
        document.addEventListener('keydown', e => {
            if (e.key === '/' && document.activeElement.tagName !== 'INPUT') {
                e.preventDefault();
                document.getElementById('search').focus();
            }
            if (e.key === 'Escape') {
                document.getElementById('search').value = '';
                searchPattern = '';
                selectedAssembly = null;
                document.querySelectorAll('.assembly-item').forEach(el => el.style.background = '');
                updateGraph();
            }
        });
        """;

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
