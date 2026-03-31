namespace TravelDisruptionAgent.Application.DTOs;

public record SelfCorrectionStep(
    string Issue,
    string Action,
    string Outcome,
    DateTime Timestamp
);
