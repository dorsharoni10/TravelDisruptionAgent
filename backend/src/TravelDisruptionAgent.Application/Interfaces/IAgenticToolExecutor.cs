using TravelDisruptionAgent.Application.Services;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IAgenticToolExecutor
{
    Task<AgenticToolInvocationResult> InvokeAsync(
        string capability,
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken cancellationToken = default);
}
