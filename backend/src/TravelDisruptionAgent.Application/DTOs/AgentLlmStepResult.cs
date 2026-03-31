namespace TravelDisruptionAgent.Application.DTOs;

public enum AgentLlmInvocationKind
{
    StructuredSchema,
    LegacyTextJson
}

public sealed class AgentLlmInvocationResult
{
    public bool Succeeded { get; init; }
    public AgentLoopStructuredOutput? Output { get; init; }
    public string? Error { get; init; }
    public string? RawSnippet { get; init; }
    public AgentLlmInvocationKind Kind { get; init; }
}
