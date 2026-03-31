using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;

namespace TravelDisruptionAgent.Tests;

/// <summary>Deterministic LLM steps for agent loop integration tests.</summary>
internal sealed class QueueAgentLoopStepLlmInvoker : IAgentLoopStepLlmInvoker
{
    private readonly Queue<AgentLlmInvocationResult> _queue;

    public QueueAgentLoopStepLlmInvoker(IEnumerable<AgentLlmInvocationResult> results) =>
        _queue = new Queue<AgentLlmInvocationResult>(results);

    public bool IsConfigured => true;

    public Task<AgentLlmInvocationResult> InvokeStructuredStepAsync(
        string systemInstructions,
        string userTurnContent,
        CancellationToken cancellationToken = default)
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("QueueAgentLoopStepLlmInvoker: no more scripted results.");
        return Task.FromResult(_queue.Dequeue());
    }
}

internal static class AgentLlmStepTestData
{
    public static AgentLlmInvocationResult StructuredTool(string capability, string argumentsJson = "{}") =>
        new()
        {
            Succeeded = true,
            Kind = AgentLlmInvocationKind.StructuredSchema,
            Output = new AgentLoopStructuredOutput
            {
                Thought = "t",
                KnownSummary = "k",
                StillMissing = "m",
                SufficientEvidence = false,
                Action = "invoke_tool",
                Capability = capability,
                ArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson,
                FinalAnswer = "",
                Confidence = 0.8
            }
        };

    public static AgentLlmInvocationResult StructuredFinal(string answer) =>
        new()
        {
            Succeeded = true,
            Kind = AgentLlmInvocationKind.StructuredSchema,
            Output = new AgentLoopStructuredOutput
            {
                Thought = "done",
                KnownSummary = "k",
                StillMissing = "none",
                SufficientEvidence = true,
                Action = "final_answer",
                Capability = "",
                ArgumentsJson = "{}",
                FinalAnswer = answer,
                Confidence = 0.9
            }
        };

    public static AgentLlmInvocationResult Failed(string error = "fail") =>
        new()
        {
            Succeeded = false,
            Error = error,
            Kind = AgentLlmInvocationKind.StructuredSchema
        };
}
