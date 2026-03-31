using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IConversationRouter
{
    Task<ConversationRouteResult> RouteAsync(
        string userMessage,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>One LLM call for routing + short plan when the kernel is configured; otherwise signals + deterministic plan.</summary>
    Task<RouteAndPlanResult> RouteAndPlanAsync(
        string userMessage,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default);
}
