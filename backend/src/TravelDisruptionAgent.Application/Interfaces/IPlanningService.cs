namespace TravelDisruptionAgent.Application.Interfaces;

public interface IPlanningService
{
    Task<AgentPlan> CreatePlanAsync(
        string userMessage,
        string intent,
        bool policyKnowledgeWillBeRetrieved,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rule-based plan (no LLM). Used when unified route+plan parsing fails or kernel is unavailable.</summary>
    AgentPlan CreateDeterministicPlan(string intent, bool policyKnowledgeWillBeRetrieved, string? userMessage = null);
}

public record AgentPlan(string Goal, IReadOnlyList<string> Steps, string? LlmFallbackReason = null);
