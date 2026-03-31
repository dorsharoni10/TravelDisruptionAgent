using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Services;

public sealed record AgenticToolContext(string UserMessage, DateTime RequestTimeUtc);

/// <summary>Outcome of one agent-chosen tool or retrieval for loop observation + telemetry.</summary>
public sealed class AgenticToolInvocationResult
{
    public required string Observation { get; init; }
    public List<ToolExecutionResult> ToolRecords { get; init; } = [];
    public bool WasPolicyRetrieval { get; init; }
    public string? NormalizedPolicyQuery { get; init; }
    public int PolicyChunkCount { get; init; }
    public double? BestSimilarity { get; init; }
    public IReadOnlyList<string> PolicyChunkTexts { get; init; } = [];
    /// <summary>Retrieved policy chunks with ids (for grounding and citation validation).</summary>
    public IReadOnlyList<PolicyRetrievalChunk> PolicyRetrievalChunks { get; init; } = [];
}
