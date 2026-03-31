using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IAgentOrchestrator
{
    Task<AgentResponse> ProcessAsync(
        ChatRequest request,
        Func<AgentEvent, Task>? onEvent = null,
        CancellationToken cancellationToken = default);
}
