namespace TravelDisruptionAgent.Application.DTOs;

public enum AgenticStopReason
{
    None,
    FinalAnswer,
    MaxIterations,
    ParseFailures,
    Stagnation,
    DuplicatePolicyQuery,
    ModelError,
    InsufficientEvidenceAbort,
    Disabled,
    GroundingFallback,
    LegacyJsonFallback
}

/// <summary>One iteration of the agentic loop (for logs and API debug).</summary>
public sealed class AgentLoopIterationRecord
{
    public int Index { get; init; }
    public string Thought { get; init; } = "";
    public string KnownSummary { get; init; } = "";
    public string StillMissing { get; init; } = "";
    public string Action { get; init; } = "";
    public string Capability { get; init; } = "";
    public string ArgumentsSummary { get; init; } = "";
    public string ObservationSummary { get; init; } = "";
    public double? RetrievalBestSimilarity { get; init; }
    public int RetrievalChunkCount { get; init; }
}

public sealed class AgentLoopRunResult
{
    public bool Succeeded { get; init; }
    public AgenticStopReason StopReason { get; init; }
    public string? FinalAnswer { get; init; }
    public double Confidence { get; init; }
    public List<ToolExecutionResult> ToolResults { get; init; } = [];
    public List<string> PolicyChunks { get; init; } = [];
    public List<AgentLoopIterationRecord> Trace { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public int PolicyRetrievalCount { get; init; }
    public List<string> PolicyQueriesExecuted { get; init; } = [];
    public int IterationCount { get; init; }
    /// <summary>Telemetry: final answer claims grounded on retrieved policy text.</summary>
    public bool AnswerGroundedOnPolicy { get; init; }
    /// <summary>Telemetry: live flight/weather tools were invoked during the loop.</summary>
    public bool AnswerGroundedOnTools { get; init; }

    /// <summary>Structured schema path was used for the last successful LLM step (or entire run).</summary>
    public bool UsedStructuredLlmOutput { get; init; }

    /// <summary>Final text was replaced due to missing KB evidence while policy was required.</summary>
    public bool PolicyGroundingFallbackApplied { get; init; }

    public List<string> CitationSanitizationWarnings { get; init; } = [];
}
