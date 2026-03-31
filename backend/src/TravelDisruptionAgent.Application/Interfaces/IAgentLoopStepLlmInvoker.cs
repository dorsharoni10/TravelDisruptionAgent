using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

/// <summary>
/// One LLM turn for the agentic loop using structured output (Gemini/OpenAI) with optional legacy fallback.
/// </summary>
public interface IAgentLoopStepLlmInvoker
{
    bool IsConfigured { get; }

    Task<AgentLlmInvocationResult> InvokeStructuredStepAsync(
        string systemInstructions,
        string userTurnContent,
        CancellationToken cancellationToken = default);
}
