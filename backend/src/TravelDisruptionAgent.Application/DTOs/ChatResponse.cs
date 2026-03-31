namespace TravelDisruptionAgent.Application.DTOs;

public record ChatResponse(
    string SessionId,
    string Message,
    IReadOnlyList<AgentEvent> Events
);
