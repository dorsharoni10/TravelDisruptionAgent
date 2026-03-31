namespace TravelDisruptionAgent.Application.DTOs;

public class AgentResponse
{
    /// <summary>Session key for multi-turn chat. Client should send the same value as <c>sessionId</c> on the next request.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Authoritative session transcript after this turn (in-memory or MongoDB when configured).</summary>
    public List<ChatMessage> ConversationHistory { get; init; } = [];

    public bool InScope { get; init; }
    /// <summary>Granular routing intent (e.g. reimbursement_expenses, mixed_disruption).</summary>
    public string Intent { get; init; } = string.Empty;
    /// <summary>Workflow intent for planning and validation (e.g. flight_cancellation, general_travel_disruption).</summary>
    public string WorkflowIntent { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> AgentPlan { get; init; } = [];
    public List<ToolExecutionResult> ToolExecutions { get; init; } = [];
    public List<SelfCorrectionStep> SelfCorrectionSteps { get; init; } = [];
    public string FinalRecommendation { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
    public double Confidence { get; init; }
    public List<string> DataSources { get; init; } = [];
    public string TraceId { get; init; } = string.Empty;
    public long DurationMs { get; init; }
    public List<AgentEvent> Events { get; init; } = [];

    // Routing transparency
    public bool MemoryUsed { get; init; }
    public bool RagUsed { get; init; }
    public string RoutingDecision { get; init; } = string.Empty;
    public List<string> RagContext { get; init; } = [];
    public List<string> LlmErrors { get; init; } = [];

    /// <summary>True when the agentic tool/RAG loop produced the recommendation (legacy path skipped for tools/RAG).</summary>
    public bool AgenticLoopUsed { get; init; }
    public string? AgenticStopReason { get; init; }
    public int AgenticIterations { get; init; }
    public int AgenticPolicyRetrievalCount { get; init; }
    public List<string> AgenticPolicyQueries { get; init; } = [];
    public List<AgentLoopIterationRecord>? AgenticTrace { get; init; }
    public bool AnswerGroundedOnPolicy { get; init; }
    public bool AnswerGroundedOnTools { get; init; }

    /// <summary>True when all LLM steps used provider structured JSON (no legacy text JSON fallback).</summary>
    public bool AgenticStructuredLlmOnly { get; init; }

    /// <summary>Final answer was replaced because required policy evidence was missing from KB.</summary>
    public bool AgenticPolicyGroundingFallback { get; init; }

    public List<string> AgenticCitationWarnings { get; init; } = [];
}
