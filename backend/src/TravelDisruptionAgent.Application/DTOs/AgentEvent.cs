using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.DTOs;

public record AgentEvent(
    AgentStepType StepType,
    string Title,
    string Content,
    DateTime Timestamp
)
{
    public static AgentEvent Create(AgentStepType stepType, string title, string content) =>
        new(stepType, title, content, DateTime.UtcNow);
}
