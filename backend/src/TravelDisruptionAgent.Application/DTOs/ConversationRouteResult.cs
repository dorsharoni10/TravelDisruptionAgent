namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>
/// Single routing outcome after scope + tool/RAG needs (replaces separate scope + routing decisions).
/// </summary>
public sealed record ConversationRouteResult(
    bool InScope,
    string? RejectionMessage,
    /// <summary>Granular intent (<see cref="Constants.AgentIntents"/>).</summary>
    string Intent,
    bool NeedsTools,
    bool NeedsRag,
    bool UseInternalOnly,
    string Reason,
    List<string> SuggestedRagTopics,
    string? LlmFallbackReason);
