namespace CodeGraph.Query.Metrics;

/// <summary>
/// Estimated cost of running a query, useful for agents to predict token usage.
/// </summary>
public record QueryCostEstimate(
    int EstimatedNodes,
    int EstimatedEdges,
    int EstimatedTokensCompact,
    int EstimatedTokensContext);
