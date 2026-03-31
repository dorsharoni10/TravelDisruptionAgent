using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Domain.Entities;
using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IAgentLoopService
{
    /// <summary>
    /// Model-driven loop: tools + policy retrieval until final answer or stop condition.
    /// </summary>
    Task<AgentLoopRunResult> RunAsync(
        string userMessage,
        ConversationRouteResult route,
        string workflowIntent,
        IReadOnlyList<string> planSteps,
        UserPreferences? preferences,
        Func<AgentStepType, string, string, Task>? emit,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default);
}
